using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Remotely.Desktop.Shared.Services;
using SkiaSharp;
using System.Diagnostics;

namespace Remotely.Desktop.Shared.Tests;

[TestClass]
public class FrameEncodingTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    private readonly ImageHelper _helper = new(NullLogger<ImageHelper>.Instance);

    [TestMethod]
    public void FramePacker_ProducesSameBytesAsBinaryWriterAssembly()
    {
        var random = new Random(777);
        var area = new SKRect(10, 20, 410, 320);
        var timestamp = 1_760_000_123_456L;

        // Размеры вокруг границ чанков: заголовок 28 байт + изображение кратно/не кратно 50 000.
        foreach (var imageLength in new[] { 1, 5, 27, 28, 29, 49_971, 49_972, 49_973, 50_000, 99_972, 99_973, 123_457, 400_001 })
        {
            var image = new byte[imageLength];
            random.NextBytes(image);

            var expected = ReferencePack(image, area, timestamp);
            var actual = FramePacker.Pack(image, area, timestamp).ToArray();

            Assert.AreEqual(expected.Length, actual.Length, $"Число чанков, длина {imageLength}");
            for (var i = 0; i < expected.Length; i++)
            {
                CollectionAssert.AreEqual(expected[i], actual[i], $"Чанк {i}, длина {imageLength}");
            }
        }
    }

    [TestMethod]
    public void EncodeRegion_ProducesSameJpegAsCropAndEncode()
    {
        using var frame = CreateFrame(11);
        var areas = new[]
        {
            new SKRect(0, 0, Width, Height),            // весь кадр
            new SKRect(100, 100, 500, 400),
            new SKRect(0, 0, 3, 3),
            new SKRect(Width - 40, Height - 30, Width, Height),
            new SKRect(300, 0, 900, 17),
        };

        foreach (var area in areas)
        {
            using var cropped = _helper.CropBitmap(frame, area);
            var expected = _helper.EncodeBitmap(cropped, SKEncodedImageFormat.Jpeg, 80);
            var actual = _helper.EncodeRegion(frame, area, SKEncodedImageFormat.Jpeg, 80);

            CollectionAssert.AreEqual(expected, actual, $"Область {area}");
        }
    }

    [TestMethod]
    public void EncodeRegion_IsNotSlowerThanCropAndEncode()
    {
        using var frame = CreateFrame(12);
        var area = new SKRect(0, 0, Width, Height); // худший случай: весь экран

        _ = _helper.EncodeRegion(frame, area, SKEncodedImageFormat.Jpeg, 80);
        using (var warm = _helper.CropBitmap(frame, area)) { _ = _helper.EncodeBitmap(warm, SKEncodedImageFormat.Jpeg, 80); }

        const int Runs = 10;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            using var cropped = _helper.CropBitmap(frame, area);
            _ = _helper.EncodeBitmap(cropped, SKEncodedImageFormat.Jpeg, 80);
        }
        var reference = sw.Elapsed.TotalMilliseconds / Runs;

        sw.Restart();
        for (var i = 0; i < Runs; i++)
        {
            _ = _helper.EncodeRegion(frame, area, SKEncodedImageFormat.Jpeg, 80);
        }
        var optimized = sw.Elapsed.TotalMilliseconds / Runs;

        Console.WriteLine($"Кодирование всего кадра 1920x1080: было {reference:F2} мс (обрезка + кодирование), стало {optimized:F2} мс, разница {reference - optimized:F2} мс");

        // Запас на шум измерений: главное, чтобы не стало медленнее.
        Assert.IsTrue(optimized <= reference * 1.5, $"{reference:F2} мс -> {optimized:F2} мс");
    }

    [TestMethod]
    public void FramePacker_IsFasterThanStreamAssembly()
    {
        var image = new byte[400_000];
        new Random(5).NextBytes(image);
        var area = new SKRect(0, 0, 1000, 700);

        _ = FramePacker.Pack(image, area, 1).ToArray();
        _ = ReferencePack(image, area, 1);

        const int Runs = 200;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            _ = ReferencePack(image, area, 1);
        }
        var reference = sw.Elapsed.TotalMilliseconds / Runs;

        sw.Restart();
        for (var i = 0; i < Runs; i++)
        {
            _ = FramePacker.Pack(image, area, 1).ToArray();
        }
        var optimized = sw.Elapsed.TotalMilliseconds / Runs;

        Console.WriteLine($"Упаковка кадра 400 КБ: было {reference:F3} мс, стало {optimized:F3} мс");
        Assert.IsTrue(optimized <= reference * 1.5, $"{reference:F3} мс -> {optimized:F3} мс");
    }

    /// <summary>
    /// Эталон: прежний способ сборки кадра (BinaryWriter в поток, ToArray, Chunk).
    /// </summary>
    private static byte[][] ReferencePack(byte[] image, SKRect area, long timestamp)
    {
        var manager = new RecyclableMemoryStreamManager();
        using var frameStream = manager.GetStream();
        using var writer = new BinaryWriter(frameStream);
        writer.Write(image.Length);
        writer.Write(area.Left);
        writer.Write(area.Top);
        writer.Write(area.Width);
        writer.Write(area.Height);
        writer.Write(timestamp);
        writer.Write(image);
        writer.Flush();
        frameStream.Seek(0, SeekOrigin.Begin);
        return frameStream.ToArray().Chunk(50_000).ToArray();
    }

    private static SKBitmap CreateFrame(int seed)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var random = new Random(seed);

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels();
            var count = Width * Height;
            for (var i = 0; i < count; i++)
            {
                uint value = unchecked((uint)((i / Width / 90) * 0x00101010 + (i % Width / 400) * 0x00050505));
                if ((i & 0x3F) == 0)
                {
                    value = (uint)random.Next();
                }
                pixels[i] = 0xFF000000u | (value & 0x00FFFFFFu);
            }
        }

        return bitmap;
    }
}
