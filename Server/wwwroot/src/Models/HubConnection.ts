import { HubConnectionState } from "../Enums/HubConnectionState.js";
import { IStreamResult } from "../Stream.js";

export type HubConnection = {
    start: () => Promise<any>;
    onclose: (callback: () => any) => any;
    state: HubConnectionState;
    /** Как часто клиент шлёт keep-alive (мс). По умолчанию 15000. */
    keepAliveIntervalInMilliseconds: number;
    /** Сколько ждать сообщений от сервера, прежде чем закрыть соединение (мс). По умолчанию 30000. */
    serverTimeoutInMilliseconds: number;
    invoke<T = void>(...rest): Promise<T>;
    send(...rest): Promise<void>;
    stop(): Promise<void>;
    stream<T = any>(methodName: string, ...args: any[]): IStreamResult<T>;
}
