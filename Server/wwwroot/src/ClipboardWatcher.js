import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";
export class ClipboardWatcher {
    constructor() {
        this.IsBusy = false;
        this.LastFailureToast = 0;
    }
    ReportFailure() {
        // Уведомляем не чаще раза в 30 секунд, чтобы не засыпать экран.
        if (Date.now() - this.LastFailureToast < 30000) {
            return;
        }
        this.LastFailureToast = Date.now();
        ShowToast("Clipboard sync failed. Click the page and allow clipboard access.");
    }
    async WatchClipboard() {
        var _a;
        if (!location.protocol.includes("https") &&
            !location.origin.includes("localhost")) {
            console.warn("Clipboard API only works in a secure context (i.e. HTTPS or localhost).");
            return;
        }
        if (!((_a = navigator.clipboard) === null || _a === void 0 ? void 0 : _a.readText)) {
            console.warn("Clipboard API not supported.");
            return;
        }
        if (this.ClipboardTimer) {
            console.log("ClipboardWatcher is already running.");
            return;
        }
        try {
            // This will throw on Firefox, Safari, and possibly other browsers.
            // MDN states that they have no intention of implementing clipboard
            // permissions.
            await navigator.permissions.query({
                name: "clipboard-read"
            });
        }
        catch (ex) {
            console.error(ex);
            if (!localStorage.getItem("clipboardWarning")) {
                localStorage.setItem("clipboardWarning", "true");
                alert("Clipboard sync is unavailable for this browser. Please see the console for details.");
            }
            console.warn("New restrictions have been placed on the Clipboard API in " +
                "some browsers that make it impossible to sync the clipboard " +
                "in the background.  You can read more about this topic here: " +
                "https://developer.mozilla.org/en-US/docs/Web/API/Clipboard_API#security_considerations");
            return;
        }
        this.ClipboardTimer = window.setInterval(async () => {
            // Не запускаем новый тик, пока предыдущий не закончил (например, ждёт разрешения браузера).
            if (this.IsBusy || !document.hasFocus()) {
                return;
            }
            this.IsBusy = true;
            try {
                if (this.NewClipboardText && navigator.clipboard.writeText) {
                    const text = this.NewClipboardText;
                    await navigator.clipboard.writeText(text);
                    this.LastClipboardText = text;
                    // Если за время записи пришёл более новый текст, его не затираем.
                    if (this.NewClipboardText === text) {
                        this.NewClipboardText = null;
                    }
                    ShowToast("Clipboard updated.");
                    return;
                }
                var newText = await navigator.clipboard.readText();
                if (this.LastClipboardText != newText) {
                    this.LastClipboardText = newText;
                    ViewerApp.MessageSender.SendTextTransfer(newText, false);
                }
            }
            catch (ex) {
                // Браузер может отказать (страница потеряла фокус, не выдано разрешение).
                // Текст остаётся в очереди и будет записан при следующей попытке, а не пропадёт молча.
                console.warn("Clipboard sync failed.", ex);
                this.ReportFailure();
            }
            finally {
                this.IsBusy = false;
            }
        }, 500);
    }
    SetClipboardText(text) {
        if (text == this.LastClipboardText) {
            return;
        }
        this.NewClipboardText = text;
    }
}
//# sourceMappingURL=ClipboardWatcher.js.map