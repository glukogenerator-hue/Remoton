using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Desktop.Shared.Services;
using SkiaSharp;

namespace Remotely.Desktop.Shared.Tests;

[TestClass]
public class FrameStripsTests
{
    [TestMethod]
    public void SmallArea_UsesSingleStrip()
    {
        Assert.AreEqual(1, FrameStrips.GetStripCount(new SKRect(0, 0, 300, 200), processorCount: 16));
        Assert.AreEqual(1, FrameStrips.GetStripCount(new SKRect(10, 10, 700, 500), processorCount: 16));
    }

    [TestMethod]
    public void LargeArea_StripCountDependsOnProcessors_AndIsCapped()
    {
        var large = new SKRect(0, 0, 1920, 1080);

        Assert.AreEqual(1, FrameStrips.GetStripCount(large, 1));
        Assert.AreEqual(1, FrameStrips.GetStripCount(large, 2));
        Assert.AreEqual(2, FrameStrips.GetStripCount(large, 4));
        Assert.AreEqual(3, FrameStrips.GetStripCount(large, 6));
        Assert.AreEqual(4, FrameStrips.GetStripCount(large, 8));
        Assert.AreEqual(4, FrameStrips.GetStripCount(large, 64));
    }

    [TestMethod]
    public void Split_CoversAreaExactly_WithoutGapsOrOverlaps()
    {
        var areas = new[]
        {
            new SKRect(0, 0, 1920, 1080),
            new SKRect(37, 11, 1903, 1049),
            new SKRect(5, 3, 600, 7),
            new SKRect(0, 0, 2, 1),
        };

        foreach (var area in areas)
        {
            foreach (var count in new[] { 1, 2, 3, 4, 5, 7 })
            {
                var strips = FrameStrips.Split(area, count);

                Assert.IsTrue(strips.Length >= 1 && strips.Length <= count);
                Assert.AreEqual(area.Top, strips[0].Top, $"Верх, {area} / {count}");
                Assert.AreEqual(area.Bottom, strips[^1].Bottom, $"Низ, {area} / {count}");

                for (var i = 0; i < strips.Length; i++)
                {
                    Assert.AreEqual(area.Left, strips[i].Left);
                    Assert.AreEqual(area.Right, strips[i].Right);
                    Assert.IsTrue(strips[i].Height >= 1, $"Пустая полоса {i}, {area} / {count}");

                    if (i > 0)
                    {
                        Assert.AreEqual(strips[i - 1].Bottom, strips[i].Top, $"Стык полос {i - 1}/{i}, {area} / {count}");
                    }
                }

                Assert.AreEqual(area.Height, strips.Sum(x => x.Height), $"Сумма высот, {area} / {count}");
            }
        }
    }

    [TestMethod]
    public void Split_NeverProducesMoreStripsThanRows()
    {
        var strips = FrameStrips.Split(new SKRect(0, 0, 1000, 3), 4);

        Assert.AreEqual(3, strips.Length);
        Assert.IsTrue(strips.All(x => x.Height == 1));
    }
}
