using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Remotely.Desktop.Shared.Services;
using SkiaSharp;
using System.Diagnostics;

namespace Remotely.Desktop.Shared.Tests;

[TestClass]
public class ImageHelperTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    private readonly ImageHelper _helper = new(NullLogger<ImageHelper>.Instance);

    [TestMethod]
    public void GetDiffArea_NoChanges_ReturnsEmpty()
    {
        using var previous = CreateFrame(1);
        using var current = Copy(previous);

        Assert.AreEqual(SKRect.Empty, _helper.GetDiffArea(current, previous));
    }

    [TestMethod]
    public void GetDiffArea_MatchesReference_ForManyRandomChanges()
    {
        var random = new Random(12345);

        for (var iteration = 0; iteration < 40; iteration++)
        {
            using var previous = CreateFrame(iteration);
            using var current = Copy(previous);

            // От 1 до 6 случайных прямоугольников (включая касающиеся краёв кадра).
            var patches = random.Next(1, 7);
            for (var i = 0; i < patches; i++)
            {
                var x = random.Next(0, Width);
                var y = random.Next(0, Height);
                var w = random.Next(1, Math.Min(400, Width - x) + 1);
                var h = random.Next(1, Math.Min(300, Height - y) + 1);
                Paint(current, x, y, w, h, (uint)random.Next());
            }

            var expected = ReferenceDiffArea(current, previous);
            var actual = _helper.GetDiffArea(current, previous);

            Assert.AreEqual(expected, actual, $"Итерация {iteration}");
        }
    }

    [TestMethod]
    public void GetDiffArea_MatchesReference_ForEdgesAndSinglePixels()
    {
        var points = new (int X, int Y)[]
        {
            (0, 0), (Width - 1, 0), (0, Height - 1), (Width - 1, Height - 1),
            (Width / 2, Height / 2), (1, 1), (Width - 2, Height - 2)
        };

        foreach (var (x, y) in points)
        {
            using var previous = CreateFrame(7);
            using var current = Copy(previous);
            Paint(current, x, y, 1, 1, 0x00123456u);

            Assert.AreEqual(
                ReferenceDiffArea(current, previous),
                _helper.GetDiffArea(current, previous),
                $"Точка ({x},{y})");
        }
    }

    [TestMethod]
    public void GetDiffArea_IgnoresAlphaOnlyChanges()
    {
        using var previous = CreateFrame(3);
        using var current = Copy(previous);

        unsafe
        {
            var pixels = (uint*)current.GetPixels();
            for (var i = 0; i < 5000; i++)
            {
                pixels[i * 37] ^= 0xFF000000u; // меняется только альфа
            }
        }

        Assert.AreEqual(SKRect.Empty, ReferenceDiffArea(current, previous));
        Assert.AreEqual(SKRect.Empty, _helper.GetDiffArea(current, previous));
    }

    [TestMethod]
    public void GetDiffArea_FullChange_ReturnsWholeFrame()
    {
        using var previous = CreateFrame(1);
        using var current = CreateFrame(2);

        Assert.AreEqual(
            ReferenceDiffArea(current, previous),
            _helper.GetDiffArea(current, previous));
    }

    [TestMethod]
    public void GetDiffArea_IsMuchFasterThanPerPixelScan()
    {
        using var previous = CreateFrame(5);
        using var current = Copy(previous);
        Paint(current, 100, 100, 300, 200, 0x00ABCDEFu); // типичное небольшое изменение

        // Прогрев.
        _ = _helper.GetDiffArea(current, previous);
        _ = ReferenceDiffArea(current, previous);

        const int Runs = 15;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            _ = ReferenceDiffArea(current, previous);
        }
        var reference = sw.Elapsed.TotalMilliseconds / Runs;

        sw.Restart();
        for (var i = 0; i < Runs; i++)
        {
            _ = _helper.GetDiffArea(current, previous);
        }
        var optimized = sw.Elapsed.TotalMilliseconds / Runs;

        Console.WriteLine($"GetDiffArea 1920x1080, небольшое изменение: было {reference:F2} мс, стало {optimized:F2} мс, ускорение x{reference / optimized:F1}");

        Assert.IsTrue(optimized * 2 < reference, $"Ожидалось ускорение минимум вдвое: {reference:F2} мс -> {optimized:F2} мс.");
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
                // Похоже на рабочий стол: крупные однотонные области плюс шум.
                uint value = unchecked((uint)((i / Width / 90) * 0x00101010 + (i % Width / 400) * 0x00050505));
                if ((i & 0xFFF) == 0)
                {
                    value = (uint)random.Next();
                }
                pixels[i] = 0xFF000000u | (value & 0x00FFFFFFu);
            }
        }

        return bitmap;
    }

    private static SKBitmap Copy(SKBitmap source)
    {
        var copy = new SKBitmap(source.Info);
        unsafe
        {
            Buffer.MemoryCopy(
                (void*)source.GetPixels(),
                (void*)copy.GetPixels(),
                source.ByteCount,
                source.ByteCount);
        }
        return copy;
    }

    private static void Paint(SKBitmap bitmap, int x, int y, int width, int height, uint color)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels();
            for (var row = y; row < y + height; row++)
            {
                for (var column = x; column < x + width; column++)
                {
                    var index = row * bitmap.Width + column;
                    // Гарантируем отличие по цвету (а не только по альфе).
                    pixels[index] = 0xFF000000u | ((pixels[index] ^ (color | 1u)) & 0x00FFFFFFu);
                }
            }
        }
    }

    /// <summary>
    /// Эталон: прежний попиксельный алгоритм, как в ImageHelper до оптимизации.
    /// </summary>
    private static SKRect ReferenceDiffArea(SKBitmap currentFrame, SKBitmap previousFrame)
    {
        var width = currentFrame.Width;
        var height = currentFrame.Height;
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        var bytesPerPixel = currentFrame.BytesPerPixel;

        unsafe
        {
            byte* scan1 = (byte*)currentFrame.GetPixels().ToPointer();
            byte* scan2 = (byte*)previousFrame.GetPixels().ToPointer();

            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var index = (row * width * bytesPerPixel) + (column * bytesPerPixel);

                    byte* data1 = scan1 + index;
                    byte* data2 = scan2 + index;

                    if (data1[0] != data2[0] ||
                        data1[1] != data2[1] ||
                        data1[2] != data2[2])
                    {
                        if (row < top) top = row;
                        if (row > bottom) bottom = row;
                        if (column < left) left = column;
                        if (column > right) right = column;
                    }
                }
            }

            if (left <= right && top <= bottom)
            {
                left = Math.Max(left - 2, 0);
                top = Math.Max(top - 2, 0);
                right = Math.Min(right + 2, width);
                bottom = Math.Min(bottom + 2, height);
                return new SKRect(left, top, right, bottom);
            }

            return SKRect.Empty;
        }
    }
}
