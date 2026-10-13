import { ViewerApp } from "./App.js";
import { Screen2DContext } from "./UI.js";
import { GetUint64 } from "./Utilities.js";
const FrameHeaderSize = 28;
// Защита от испорченного заголовка: кадр не может быть больше этого размера.
const MaxImageSize = 64 * 1024 * 1024;
function AppendChunk(streamingState, chunk) {
    const used = streamingState.End - streamingState.Start;
    if (streamingState.End + chunk.length > streamingState.Buffer.length) {
        if (used + chunk.length <= streamingState.Buffer.length) {
            // Места достаточно, нужно только сдвинуть данные в начало буфера.
            streamingState.Buffer.copyWithin(0, streamingState.Start, streamingState.End);
        }
        else {
            const grown = new Uint8Array(Math.max((used + chunk.length) * 2, streamingState.Buffer.length));
            grown.set(streamingState.Buffer.subarray(streamingState.Start, streamingState.End), 0);
            streamingState.Buffer = grown;
        }
        streamingState.Start = 0;
        streamingState.End = used;
    }
    streamingState.Buffer.set(chunk, streamingState.End);
    streamingState.End += chunk.length;
}
function TryTakeFrame(streamingState) {
    const available = streamingState.End - streamingState.Start;
    if (available < FrameHeaderSize) {
        return null;
    }
    const header = new DataView(streamingState.Buffer.buffer, streamingState.Buffer.byteOffset + streamingState.Start, FrameHeaderSize);
    const imageSize = header.getInt32(0, true);
    if (imageSize <= 0 || imageSize > MaxImageSize) {
        throw new Error(`Invalid frame size: ${imageSize}`);
    }
    if (available - FrameHeaderSize < imageSize) {
        return null;
    }
    const imageStart = streamingState.Start + FrameHeaderSize;
    const frame = {
        X: header.getFloat32(4, true),
        Y: header.getFloat32(8, true),
        Width: header.getFloat32(12, true),
        Height: header.getFloat32(16, true),
        Timestamp: GetUint64(header, 20, true),
        // slice копирует байты, поэтому буфер можно переиспользовать.
        Image: streamingState.Buffer.slice(imageStart, imageStart + imageSize)
    };
    streamingState.Start = imageStart + imageSize;
    if (streamingState.Start === streamingState.End) {
        streamingState.Start = 0;
        streamingState.End = 0;
    }
    return frame;
}
async function DrawFrames(frames) {
    // Кадры содержат только изменённые области, поэтому рисовать нужно строго по порядку.
    // Декодируем все сразу (параллельно), а рисуем по очереди.
    const decoded = frames.map(x => createImageBitmap(new Blob([x.Image])));
    let index = 0;
    try {
        for (; index < frames.length; index++) {
            const frame = frames[index];
            const bitmap = await decoded[index];
            try {
                Screen2DContext.drawImage(bitmap, frame.X, frame.Y, frame.Width, frame.Height);
            }
            finally {
                bitmap.close();
            }
            ViewerApp.MessageSender.SendFrameReceived(frame.Timestamp);
        }
    }
    catch (ex) {
        // Освобождаем оставшиеся битмапы.
        for (let i = index + 1; i < decoded.length; i++) {
            decoded[i].then(x => x.close(), () => { });
        }
        throw ex;
    }
}
/**
 * Разбирает накопленные байты на кадры и рисует их.
 * Запускается при получении каждого чанка и не зависит от requestAnimationFrame,
 * поэтому не замирает в фоновой вкладке и не ограничивает частоту кадров частотой монитора.
 */
export async function ProcessStream(streamingState) {
    if (streamingState.IsProcessing) {
        return;
    }
    streamingState.IsProcessing = true;
    try {
        while (!streamingState.StreamEnded) {
            for (const chunk of streamingState.ReceivedChunks.splice(0)) {
                AppendChunk(streamingState, chunk);
            }
            const frames = [];
            let frame;
            while ((frame = TryTakeFrame(streamingState)) !== null) {
                frames.push(frame);
            }
            if (frames.length == 0) {
                break;
            }
            await DrawFrames(frames);
        }
    }
    catch (ex) {
        console.error("Capture processing error.  Resetting stream buffer.", ex);
        streamingState.Start = 0;
        streamingState.End = 0;
    }
    finally {
        streamingState.IsProcessing = false;
    }
}
//# sourceMappingURL=CaptureProcessor.js.map