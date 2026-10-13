export class StreamingState {
    constructor() {
        this.Buffer = new Uint8Array(1024 * 1024);
        this.Start = 0;
        this.End = 0;
        this.ReceivedChunks = [];
        this.StreamEnded = false;
        this.IsProcessing = false;
    }
}
//# sourceMappingURL=StreamingState.js.map