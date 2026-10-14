using Remotely.Desktop.Shared.Abstractions;
using Microsoft.Extensions.Logging;

namespace Remotely.Desktop.UI.Services;

public class ClipboardService : IClipboardService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ClipboardService> _logger;
    private Task? _watcherTask;

    public event EventHandler<string>? ClipboardTextChanged;

    public ClipboardService(
        IUiDispatcher dispatcher,
        ILogger<ClipboardService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    private string ClipboardText { get; set; } = string.Empty;

    public void BeginWatching()
    {
        // Status == Running для async-задач ненадёжен (может быть WaitingForActivation и т. п.),
        // из-за чего можно было запустить несколько наблюдателей.
        if (_watcherTask is { IsCompleted: false })
        {
            return;
        }

        _watcherTask = Task.Run(
            async () => await WatchClipboard(_dispatcher.ApplicationExitingToken),
            _dispatcher.ApplicationExitingToken);
    }

    public async Task SetText(string clipboardText)
    {
        try
        {
            if (_dispatcher?.Clipboard is null)
            {
                _logger.LogWarning("Clipboard is null.");
                return;
            }

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                await _dispatcher.Clipboard.ClearAsync();
            }
            else
            {
                await _dispatcher.Clipboard.SetTextAsync(clipboardText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while setting text.");
        }
    }

    private async Task WatchClipboard(CancellationToken cancelToken)
    {
        while (
            !cancelToken.IsCancellationRequested &&
            !Environment.HasShutdownStarted)
        {
            try
            {
                if (_dispatcher?.Clipboard is not null)
                {
                    var currentText = await Avalonia.Input.Platform.ClipboardExtensions
                        .TryGetTextAsync(_dispatcher.Clipboard);

                    if (!string.IsNullOrEmpty(currentText) && currentText != ClipboardText)
                    {
                        ClipboardText = currentText;
                        ClipboardTextChanged?.Invoke(this, ClipboardText);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Буфер может быть занят другим процессом. Это не повод останавливать слежение навсегда.
                _logger.LogWarning(ex, "Error while reading the clipboard.");
            }

            try
            {
                await Task.Delay(500, cancelToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
