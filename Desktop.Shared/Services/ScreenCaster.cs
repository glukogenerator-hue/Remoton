using SkiaSharp;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Enums;
using Remotely.Shared.Models;
using Microsoft.Extensions.Logging;
using Remotely.Shared.Helpers;
using Remotely.Shared.Models.Dtos;
using Remotely.Shared.Services;
using Microsoft.IO;
using Bitbound.SimpleMessenger;
using Remotely.Desktop.Shared.Messages;

namespace Remotely.Desktop.Shared.Services;

public interface IScreenCaster : IDisposable
{
    Task BeginScreenCasting(ScreenCastRequest screenCastRequest);
}

internal class ScreenCaster : IScreenCaster
{
    private readonly IAppState _appState;
    private readonly ICursorIconWatcher _cursorIconWatcher;
    private readonly IImageHelper _imageHelper;
    private readonly ILogger<ScreenCaster> _logger;
    private readonly CancellationTokenSource _metricsCts = new();
    private readonly RecyclableMemoryStreamManager _recycleStreams = new();
    private readonly ISessionIndicator _sessionIndicator;
    private readonly IShutdownService _shutdownService;
    private readonly ISystemTime _systemTime;
    private readonly IViewerFactory _viewerFactory;
    private readonly IDisposable[] _messengerRegistrations;
    private bool _isWindowsSessionEnding;

    public ScreenCaster(
        IAppState appState,
        IViewerFactory viewerFactory,
        ICursorIconWatcher cursorIconWatcher,
        ISessionIndicator sessionIndicator,
        IShutdownService shutdownService,
        IImageHelper imageHelper,
        ISystemTime systemTime,
        IMessenger messenger,
        ILogger<ScreenCaster> logger)
    {
        _appState = appState;
        _cursorIconWatcher = cursorIconWatcher;
        _sessionIndicator = sessionIndicator;
        _shutdownService = shutdownService;
        _imageHelper = imageHelper;
        _systemTime = systemTime;
        _viewerFactory = viewerFactory;
        _logger = logger;

        _messengerRegistrations =
        [
            messenger.Register<WindowsSessionSwitchedMessage>(this, HandleWindowsSessionSwitchedMessage),
            messenger.Register<WindowsSessionEndingMessage>(this, HandleWindowsSessionEndingMessage)
        ];
    }

    public async Task BeginScreenCasting(ScreenCastRequest screenCastRequest)
    {
        await BeginScreenCastingImpl(screenCastRequest).ConfigureAwait(false);
    }

    public void Dispose()
    {
        foreach (var registration in _messengerRegistrations)
        {
            try
            {
                registration.Dispose();
            }
            catch { }
        }
        _metricsCts.Cancel();
        _metricsCts.Dispose();

        GC.SuppressFinalize(this);
    }

    private async Task BeginScreenCastingImpl(ScreenCastRequest screenCastRequest)
    {
        using var viewer = _viewerFactory.CreateViewer(screenCastRequest.RequesterName, screenCastRequest.ViewerId);

        try
        {
            viewer.Name = screenCastRequest.RequesterName;
            viewer.ViewerConnectionId = screenCastRequest.ViewerId;

            var screenBounds = viewer.Capturer.CurrentScreenBounds;

            _logger.LogInformation(
                "Starting screen cast.  Requester: {viewerName}. " +
                "Viewer ID: {viewerViewerConnectionID}.  App Mode: {mode}",
                viewer.Name,
                viewer.ViewerConnectionId,
                _appState.Mode);

            _appState.Viewers.AddOrUpdate(viewer.ViewerConnectionId, viewer, (id, v) => viewer);

            if (_appState.Mode == AppMode.Attended)
            {
                _appState.InvokeViewerAdded(viewer);
            }

            if (_appState.Mode == AppMode.Unattended && screenCastRequest.NotifyUser)
            {
                _sessionIndicator.Show();
            }

            await viewer.SendScreenData(
                viewer.Capturer.SelectedScreen,
                viewer.Capturer.GetDisplayNames(),
                screenBounds.Width,
                screenBounds.Height);

            await viewer.SendCursorChange(_cursorIconWatcher.GetCurrentCursor());

            await viewer.SendWindowsSessions();

            viewer.Capturer.ScreenChanged += async (sender, bounds) =>
            {
                await viewer.SendScreenSize(bounds.Width, bounds.Height);
            };

            _ = Task.Run(() => LogMetrics(viewer, _metricsCts.Token));
            using var sessionEndSignal = new SemaphoreSlim(0, 1);
            await viewer.SendDesktopStream(GetDesktopStream(viewer, sessionEndSignal), screenCastRequest.StreamId);
            if (!await sessionEndSignal.WaitAsync(TimeSpan.FromHours(8)))
            {
                _logger.LogWarning("Timed out while waiting for session to end.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while starting screen casting.");
        }
        finally
        {
            _logger.LogInformation(
                "Ended desktop stream.  " +
                "Requester: {viewerName}. " +
                "Viewer ID: {viewerConnectionID}. " +
                "Viewer Responsive: {isResponsive}.  " +
                "Viewer Disconnected Requested: {viewerDisconnectRequested}. " +
                "Windows Session Ending: {windowsSessionEnding}",
                viewer.Name,
                viewer.ViewerConnectionId,
                viewer.IsResponsive,
                viewer.DisconnectRequested,
                _isWindowsSessionEnding);

            _appState.Viewers.TryRemove(viewer.ViewerConnectionId, out _);
            Disposer.TryDisposeAll(viewer);

            // Close if no one is viewing.
            if (_appState.Viewers.IsEmpty && _appState.Mode == AppMode.Unattended)
            {
                _logger.LogInformation("No more viewers.  Calling shutdown service.");
                await _shutdownService.Shutdown();
            }
        }
    }

    private async IAsyncEnumerable<byte[]> GetDesktopStream(IViewer viewer, SemaphoreSlim sessionEndedSignal)
    {
        await Task.Yield();

        try
        {
            var frameTimer = System.Diagnostics.Stopwatch.StartNew();

            while (!viewer.DisconnectRequested && viewer.IsResponsive && !_isWindowsSessionEnding)
            {
                viewer.IncrementFpsCount();

                await viewer.ApplyAutoQuality();

                if (!await viewer.WaitForViewer())
                {
                    _logger.LogWarning(
                        "Viewer is behind on frames and did not catch up in time.");
                }

                // При перегрузке канала (качество снижено) ограничиваем частоту кадров.
                var minFrameInterval = viewer.MinFrameInterval;
                if (minFrameInterval > TimeSpan.Zero)
                {
                    var remaining = minFrameInterval - frameTimer.Elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining);
                    }
                }
                frameTimer.Restart();

                var result = viewer.Capturer.GetNextFrame();

                if (!result.IsSuccess)
                {
                    await Task.Yield();
                    continue;
                }

                var diffArea = viewer.Capturer.GetFrameDiffArea();

                if (diffArea.IsEmpty)
                {
                    await Task.Yield();
                    continue;
                }

                viewer.Capturer.CaptureFullscreen = false;

                // Большая область (видео, прокрутка) делится на полосы, которые кодируются параллельно:
                // кодирование целого кадра занимает десятки миллисекунд на одном ядре.
                var strips = FrameStrips.Split(
                    diffArea,
                    FrameStrips.GetStripCount(diffArea, Environment.ProcessorCount));
                var encodedStrips = new byte[strips.Length][];
                var frame = result.Value;
                var quality = viewer.ImageQuality;

                if (strips.Length == 1)
                {
                    // Кодируем область без промежуточной копии (раньше: Crop через canvas).
                    encodedStrips[0] = _imageHelper.EncodeRegion(frame, strips[0], SKEncodedImageFormat.Jpeg, quality);
                }
                else
                {
                    Parallel.For(0, strips.Length, i =>
                    {
                        encodedStrips[i] = _imageHelper.EncodeRegion(frame, strips[i], SKEncodedImageFormat.Jpeg, quality);
                    });
                }

                for (var i = 0; i < strips.Length; i++)
                {
                    var encodedImageBytes = encodedStrips[i];

                    if (encodedImageBytes.Length == 0)
                    {
                        continue;
                    }

                    viewer.AppendSentFrame(new SentFrame(encodedImageBytes.Length, _systemTime.Now));

                    // Заголовок и изображение режутся на чанки за одну копию (раньше: поток, ToArray, Chunk).
                    foreach (var chunk in FramePacker.Pack(
                        encodedImageBytes,
                        strips[i],
                        DateTimeOffset.Now.ToUnixTimeMilliseconds()))
                    {
                        yield return chunk;
                    }
                }
            }
        }
        finally
        {
            sessionEndedSignal.Release();
        }

    }

    private Task HandleWindowsSessionEndingMessage(object subscriber, WindowsSessionEndingMessage arg)
    {
        _logger.LogInformation("Windows session ending.  Stopping screen cast.");
        _isWindowsSessionEnding = true;
        return Task.CompletedTask;
    }

    private Task HandleWindowsSessionSwitchedMessage(object subscriber, WindowsSessionSwitchedMessage arg)
    {
        _logger.LogInformation("Windows session switched.  Stopping screen cast.");
        _isWindowsSessionEnding = true;
        return Task.CompletedTask;
    }

    private async Task LogMetrics(IViewer viewer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await viewer.CalculateMetrics();

            var metrics = new SessionMetricsDto(
                Math.Round(viewer.CurrentMbps, 2),
                viewer.CurrentFps,
                viewer.RoundTripLatency.TotalMilliseconds,
                viewer.Capturer.IsGpuAccelerated,
                viewer.ImageQuality);

            _logger.LogDebug(
                "Current Mbps: {currentMbps}.  " +
                "Current FPS: {currentFps}.  " +
                "Roundtrip Latency: {roundTripLatency}ms.  " +
                "Image Quality: {imageQuality}",
                metrics.Mbps,
                metrics.Fps,
                metrics.RoundTripLatency,
                viewer.ImageQuality);


            await viewer.SendSessionMetrics(metrics);
        }
    }
}
