import { DtoWrapper } from "./Interfaces/Dtos.js";
import { CreateGUID } from "./Utilities.js";
const Chunks = {};
const MsgPack = window["MessagePack"];
export function ChunkDto(dto, dtoType, requestId = null, chunkSize = 50000) {
    const messageBytes = MsgPack.encode(dto);
    const instanceId = CreateGUID();
    const chunks = [];
    const wrappers = [];
    for (let i = 0; i < messageBytes.length; i += chunkSize) {
        const chunk = messageBytes.subarray(i, i + chunkSize);
        chunks.push(chunk);
    }
    for (let i = 0; i < chunks.length; i++) {
        const chunk = chunks[i];
        const wrapper = new DtoWrapper();
        wrapper.DtoChunk = chunk;
        wrapper.DtoType = dtoType;
        wrapper.InstanceId = instanceId;
        wrapper.IsFirstChunk = i == 0;
        wrapper.IsLastChunk = i == chunks.length - 1;
        wrapper.RequestId = requestId;
        wrapper.SequenceId = i;
        wrappers.push(wrapper);
    }
    return wrappers;
}
export function TryComplete(wrapper) {
    if (!Chunks[wrapper.InstanceId]) {
        Chunks[wrapper.InstanceId] = [];
    }
    Chunks[wrapper.InstanceId].push(wrapper);
    if (!wrapper.IsLastChunk) {
        return;
    }
    const parts = Chunks[wrapper.InstanceId]
        .sort((a, b) => a.SequenceId - b.SequenceId)
        .map(x => x.DtoChunk);
    delete Chunks[wrapper.InstanceId];
    // Один буфер нужного размера. Прежний reduce с копированием на каждом шаге давал
    // квадратичную сложность и падал с RangeError на больших сообщениях.
    const buffers = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
    let offset = 0;
    for (const part of parts) {
        buffers.set(part, offset);
        offset += part.length;
    }
    var decoded = MsgPack.decode(buffers);
    return decoded;
}
//# sourceMappingURL=DtoChunker.js.map