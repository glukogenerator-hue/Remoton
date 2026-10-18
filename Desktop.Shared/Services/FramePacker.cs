using SkiaSharp;
using System.Buffers.Binary;

namespace Remotely.Desktop.Shared.Services;

/// <summary>
/// Упаковывает закодированный кадр для отправки в браузер: заголовок и изображение, нарезанные на чанки.
/// Формат: int32 длина изображения, 4 × float32 (x, y, ширина, высота), int64 метка времени в мс (Unix),
/// затем само изображение. Всё в little-endian. Браузер разбирает его в CaptureProcessor.ts.
/// </summary>
public static class FramePacker
{
    public const int HeaderSize = 28;
    public const int DefaultChunkSize = 50_000;

    /// <summary>
    /// Режет заголовок и изображение на чанки за одну копию данных. Раньше кадр сначала собирался в
    /// поток, затем копировался в массив целиком (ToArray), и только потом резался на чанки.
    /// </summary>
    public static IEnumerable<byte[]> Pack(
        byte[] image,
        SKRect area,
        long timestampMilliseconds,
        int chunkSize = DefaultChunkSize)
    {
        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), image.Length);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(4, 4), area.Left);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(8, 4), area.Top);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(12, 4), area.Width);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(16, 4), area.Height);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(20, 8), timestampMilliseconds);

        var totalLength = HeaderSize + image.Length;

        for (var offset = 0; offset < totalLength; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, totalLength - offset);
            var chunk = new byte[length];
            var written = 0;

            if (offset < HeaderSize)
            {
                var headerPart = Math.Min(HeaderSize - offset, length);
                Buffer.BlockCopy(header, offset, chunk, 0, headerPart);
                written = headerPart;
            }

            if (written < length)
            {
                var imageOffset = offset + written - HeaderSize;
                Buffer.BlockCopy(image, imageOffset, chunk, written, length - written);
            }

            yield return chunk;
        }
    }
}
