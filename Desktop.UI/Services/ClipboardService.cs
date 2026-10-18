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

            // Тот же текст не должен вернуться к зрителю как «изменение буфера».
            ClipboardText = clipboardText ?? string.Empty;

            await _dispatcher.InvokeAsync(async () =>
            {
                var clipboard = _dispatcher.Clipboard;
                if (clipboard is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    await clipboard.ClearAsync();
                }
                else
                {
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, clipboardText);
                }
            });
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
                // Пока главное окно не создано (Clipboard == null), UI ещё не запущен. Обращаться к
                // Dispatcher.UIThread из фонового потока на этом этапе нельзя: диспетчер привязывается
                // не к тому потоку, и окно клиента зависает при запуске. Просто ждём.
                if (_dispatcher.Clipboard is not null)
                {
                    // Буфер Avalonia нужно читать в UI-потоке. Из фонового потока чтение иногда
                    // падало или возвращало null, из-за чего текст «иногда» не доходил до зрителя.
                    var currentText = await _dispatcher.InvokeAsync<string?>(async () =>
                    {
                        var clipboard = _dispatcher.Clipboard;
                        return clipboard is null ?
                            null :
                            await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
                    });

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
