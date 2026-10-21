import * as UI from "./UI.js";
import { ViewerApp } from "./App.js";
import { CursorInfo } from "./Models/CursorInfo.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
import { ShowToast } from "./UI.js";
import { WindowsSession } from "./Models/WindowsSession.js";
import { DtoType } from "./Enums/DtoType.js";
import { HubConnection } from "./Models/HubConnection.js";
import { ChunkDto } from "./DtoChunker.js";
import { MessagePack } from "./Interfaces/MessagePack.js";
import { ProcessStream } from "./CaptureProcessor.js";
import { HubConnectionState } from "./Enums/HubConnectionState.js";
import { StreamingState } from "./Models/StreamingState.js";
import { Result } from "./Models/Result.js";
import { RemoteControlViewerOptions } from "./Interfaces/Dtos.js";
import { GetSettings, SetSettings } from "./SettingsService.js";

const MsgPack: MessagePack = window["MessagePack"];

var signalR = window["signalR"];

export class ViewerHubConnection {
    Connection: HubConnection;
    PartialCaptureFrames: Uint8Array[] = [];

    // Автоматическое переподключение после обрыва (сеть пропала, VPN переподключился, сервер перезапущен).
    private ReconnectAttempts = 0;
    private ReconnectTimer: number;
    // Установлен, если сеанс закрыт намеренно (отказ, сеанс остановлен, ошибка авторизации): повторять не нужно.
    private NoReconnect = false;
    private static readonly MaxReconnectAttempts = 12;


    Connect() {
        window.clearTimeout(this.ReconnectTimer);

        if (this.Connection) {
            this.Connection.stop();
        }

        this.NoReconnect = false;

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/viewer")
            .withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())
            .configureLogging(signalR.LogLevel.Information)
            .build();
        this.Connection = connection;

        // Окно «живости». По умолчанию браузер закрывает соединение, если от сервера 30 с ничего не пришло.
        // При занятом канале и тяжёлой отрисовке видео этого мало. Должно быть не меньше удвоенного keep-alive.
        connection.keepAliveIntervalInMilliseconds = 15000;
        connection.serverTimeoutInMilliseconds = 120000;

        this.ApplyMessageHandlers(connection);

        connection.start().then(async () => {
            this.SendScreenCastRequestToDevice();
        }).catch(err => {
            console.error(err.toString());
            console.log("Connection closed.");
            if (connection !== this.Connection) {
                return;
            }
            // Не удалось подключиться (например, ещё нет сети после переподключения VPN): пробуем снова.
            if (!this.ScheduleReconnect()) {
                UI.SetStatusMessage(`Connection error: ${err.message}`);
                UI.ToggleConnectUI(true);
            }
        });

        connection.onclose(() => {
            // Закрытие старого соединения при замене новым не должно ничего сбрасывать.
            if (connection !== this.Connection) {
                return;
            }
            if (this.ScheduleReconnect()) {
                return;
            }
            if (!UI.StatusMessage.innerText) {
                UI.SetStatusMessage("Connection closed.");
            }
            UI.ToggleConnectUI(true);
        });

        ViewerApp.ClipboardWatcher.WatchClipboard();
    }

    // Планирует повторное подключение с нарастающей паузой (2, 4, 8 ... до 30 с).
    // Возвращает false, если повторять не нужно или попытки закончились.
    private ScheduleReconnect(): boolean {
        if (this.NoReconnect || !ViewerApp.SessionId) {
            return false;
        }

        if (this.ReconnectAttempts >= ViewerHubConnection.MaxReconnectAttempts) {
            UI.SetStatusMessage("Connection lost. Unable to reconnect.");
            return false;
        }

        this.ReconnectAttempts++;
        const delaySeconds = Math.min(30, Math.pow(2, this.ReconnectAttempts));
        UI.SetStatusMessage(
            `Connection lost. Reconnecting in ${delaySeconds} s (attempt ${this.ReconnectAttempts} of ${ViewerHubConnection.MaxReconnectAttempts})...`);
        ShowToast("Connection lost. Reconnecting...");

        window.clearTimeout(this.ReconnectTimer);
        this.ReconnectTimer = window.setTimeout(() => this.Connect(), delaySeconds * 1000);
        return true;
    }

    async ChangeWindowsSession(sessionID: number) {
        if (ViewerApp.Mode == RemoteControlMode.Unattended) {
            await this.Connection.invoke("ChangeWindowsSession", sessionID);
        }
    }

    async GetRemoteControlViewerOptions(): Promise<RemoteControlViewerOptions> {
        const settings = GetSettings();

        try {
            settings.ViewerOptions = await this.Connection.invoke<RemoteControlViewerOptions>("GetViewerOptions");
            SetSettings(settings);
            return settings.ViewerOptions;
        }
        catch (e) {
            console.error("Error while getting viewer options from server.", e);
            return settings.ViewerOptions;
        }
    }

    async InvokeCtrlAltDel() {
        if (this.Connection?.state != HubConnectionState.Connected) {
            return;
        }

        await this.Connection.invoke("InvokeCtrlAltDel");
    }

    async SendDtoToClient<T>(dto: T, type: DtoType): Promise<void> {

        if (this.Connection?.state != HubConnectionState.Connected) {
            return;
        }

        let chunks = ChunkDto(dto, type);

        for (var i = 0; i < chunks.length; i++) {
            const chunk = MsgPack.encode(chunks[i]);
            await this.Connection.invoke("SendDtoToClient", chunk);
        }
    }

    // Subject is an interface that comes from the SignalR library.
    // But we can't use the TypeScript library like we would in
    // React/Vue, so we have to use "any" here.  This won't be an
    // issue when we rewrite the front-end.
    async SendRecordingChunks(subject: any) {
        await this.Connection.send("StoreSessionRecording", subject);
    }

    async SendScreenCastRequestToDevice() {
        const viewerOptions = await this.GetRemoteControlViewerOptions();

        const result = await this.Connection.invoke<Result>(
            "SendScreenCastRequestToDevice",
            ViewerApp.SessionId,
            ViewerApp.AccessKey,
            ViewerApp.RequesterName);

        if (!result.IsSuccess) {
            this.NoReconnect = true;
            this.Connection.stop();
            UI.SetStatusMessage(result.Reason);
            return;
        }

        this.ReconnectAttempts = 0;
        UI.SetStatusMessage("");

        const streamingState = new StreamingState();
        ProcessStream(streamingState);

        if (viewerOptions.ShouldRecordSession) {
            ViewerApp.SessionRecorder.Start();
        }

        this.Connection.stream("GetDesktopStream")
            .subscribe({
                next: (chunk: Uint8Array) => {
                    streamingState.ReceivedChunks.push(chunk);
                    // Разбор запускается на каждый чанк, а не по requestAnimationFrame.
                    ProcessStream(streamingState);
                },
                complete: () => {
                    streamingState.StreamEnded = true;
                    if (!UI.StatusMessage.innerText) {
                        UI.SetStatusMessage("Stream ended.");
                    }
                    ViewerApp.SessionRecorder.Stop();
                    UI.ToggleConnectUI(true);
                },
                error: (err) => {
                    console.warn(err);
                    streamingState.StreamEnded = true;
                    if (!UI.StatusMessage.innerText) {
                        UI.SetStatusMessage("Stream ended.");
                    }
                    ViewerApp.SessionRecorder.Stop();
                    UI.ToggleConnectUI(true);
                },
            });

    }


    private ApplyMessageHandlers(hubConnection) {
        hubConnection.on("SendDtoToViewer", async (dto: ArrayBuffer) => {
            await ViewerApp.DtoMessageHandler.ParseBinaryMessage(dto);
        });

        hubConnection.on("ConnectionFailed", () => {
            UI.ConnectButton.removeAttribute("disabled");
            UI.ConnectButton.innerText = "Connect";
            UI.SetStatusMessage("Connection failed or was denied.");
            ShowToast("Connection failed.  Please reconnect.");
            this.NoReconnect = true;
            this.Connection.stop();
        });
        hubConnection.on("ReconnectFailed", () => {
            UI.ConnectButton.removeAttribute("disabled");
            UI.ConnectButton.innerText = "Connect";
            UI.SetStatusMessage("Unable to reconnect.");
            ShowToast("Unable to reconnect.");
            this.NoReconnect = true;
            this.Connection.stop();
        });
        hubConnection.on("ConnectionRequestDenied", () => {
            UI.ConnectButton.innerText = "Connect";
            this.NoReconnect = true;
            this.Connection.stop();
            UI.SetStatusMessage("Connection request denied.");
            ShowToast("Connection request denied.");
        });
        hubConnection.on("ViewerRemoved", () => {
            UI.ConnectButton.removeAttribute("disabled");
            UI.ConnectButton.innerText = "Connect";
            UI.SetStatusMessage("The session was stopped by your partner.");
            ShowToast("Session ended");
            this.NoReconnect = true;
            this.Connection.stop();
        });
        hubConnection.on("ScreenCasterDisconnected", () => {
            UI.SetStatusMessage("The host has disconnected.");
            this.NoReconnect = true;
            this.Connection.stop();
        });
        hubConnection.on("RelaunchedScreenCasterReady", (newSessionId: string, newAccessKey: string) => {
            const newUrl =
                `${location.origin}${location.pathname}` +
                `?mode=Unattended&sessionId=${newSessionId}&accessKey=${newAccessKey}&viewOnly=${ViewerApp.ViewOnlyMode}`;
            window.history.pushState(null, "", newUrl);
            ViewerApp.SessionId = newSessionId;
            ViewerApp.AccessKey = newAccessKey;
            this.SendScreenCastRequestToDevice();
        });

        hubConnection.on("Reconnecting", () => {
            UI.SetStatusMessage("Reconnecting");
            ShowToast("Reconnecting");
        });

        hubConnection.on("CursorChange", (cursor: CursorInfo) => {
            UI.UpdateCursor(cursor.ImageBytes, cursor.HotSpot.X, cursor.HotSpot.Y, cursor.CssOverride);
        });

        hubConnection.on("ShowMessage", (message: string) => {
            ShowToast(message);
            UI.SetStatusMessage(message);
        });
        hubConnection.on("WindowsSessions", (windowsSessions: Array<WindowsSession>) => {
            UI.UpdateWindowsSessions(windowsSessions);
        });
        hubConnection.on("PingViewer", () => "Pong");
    }
}
