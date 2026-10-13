export class StreamingState {
    constructor() {
        this.Buffer = new Uint8Array(1024 * 1024);
        this.Start = 0;
        this.End = 0;
        this.ReceivedChunks = [];
        this.StreamEnded = false;
        this.IsProcessing = false;
    }

    /** Накопитель принятых байтов. Данные лежат в диапазоне [Start, End). */
    Buffer: Uint8Array;
    Start: number;
    End: number;
    ReceivedChunks: Uint8Array[];
    StreamEnded: boolean;
    /** true, пока идёт разбор и отрисовка кадров. Не даёт запустить второй цикл. */
    IsProcessing: boolean;
}
