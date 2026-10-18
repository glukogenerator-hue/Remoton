using SkiaSharp;

namespace Remotely.Desktop.Shared.Services;

/// <summary>
/// Делит большую изменённую область кадра на горизонтальные полосы, чтобы кодировать их параллельно.
/// JPEG-кодирование целого кадра занимает десятки миллисекунд и упирается в одно ядро.
/// Каждая полоса отправляется как отдельный кадр со своим прямоугольником, браузер рисует их по порядку.
/// </summary>
public static class FrameStrips
{
    /// <summary>
    /// Область меньше этого числа пикселей кодируется одним куском: накладные расходы на параллелизм
    /// и лишние заголовки не окупаются.
    /// </summary>
    public const int MinPixelsForParallel = 400_000;

    public const int MaxStrips = 4;

    /// <summary>
    /// Сколько полос использовать. Берём не больше половины ядер, чтобы не отнимать процессор
    /// у программ пользователя на удалённой машине.
    /// </summary>
    public static int GetStripCount(SKRect area, int processorCount)
    {
        var pixels = (long)area.Width * (long)area.Height;
        if (pixels < MinPixelsForParallel)
        {
            return 1;
        }

        return Math.Clamp(processorCount / 2, 1, MaxStrips);
    }

    /// <summary>
    /// Делит область на count горизонтальных полос без пропусков и перекрытий.
    /// </summary>
    public static SKRect[] Split(SKRect area, int count)
    {
        var rows = (int)area.Height;
        count = Math.Clamp(count, 1, Math.Max(rows, 1));

        if (count == 1)
        {
            return [area];
        }

        var top = (int)area.Top;
        var strips = new SKRect[count];

        for (var i = 0; i < count; i++)
        {
            var stripTop = top + rows * i / count;
            var stripBottom = top + rows * (i + 1) / count;
            strips[i] = new SKRect(area.Left, stripTop, area.Right, stripBottom);
        }

        return strips;
    }
}
