using System.Buffers.Binary;

namespace TraiUsage.Usage;

/// <summary>One bar of the tray icon: how full it is (0–1) and its 0xAARRGGBB color.</summary>
internal readonly record struct TrayBar(double Fraction, uint Color);

/// <summary>
/// Draws the tray icon as horizontal bars stacked top to bottom, each filling from the left, so
/// several accounts and limits fit in a 16 px icon. The result is the image data of a 32-bit icon
/// (a DIB header, pixels, and mask), which is what <c>CreateIconFromResourceEx</c> takes.
/// </summary>
internal static class TrayBarsIcon
{
    private const int HeaderSize = 40; // BITMAPINFOHEADER

    /// <summary>Margin around the bars, the gap between them, the height of each, and the top of the first, in pixels.</summary>
    public static (int Margin, int Gap, int BarHeight, int Top) Layout(int size, int count)
    {
        int margin = Math.Max(1, size / 16);
        int gap = Math.Max(1, size / 16);
        count = Math.Max(1, count);
        int available = size - 2 * margin;
        int maxHeight = Math.Max(1, size * 3 / 8); // A lone bar shouldn't fill the whole icon.
        int barHeight = Math.Clamp((available - (count - 1) * gap) / count, 1, maxHeight);
        int used = count * barHeight + (count - 1) * gap;
        int top = margin + Math.Max(0, (available - used) / 2); // Centered when the bars don't fill the icon.
        return (margin, gap, barHeight, top);
    }

    /// <summary>Pixel columns (from the left) a bar of this fraction fills, within a track of <paramref name="trackWidth"/>.</summary>
    public static int FilledColumns(double fraction, int trackWidth) =>
        fraction <= 0 ? 0 : Math.Clamp((int)Math.Round(fraction * trackWidth), 1, trackWidth); // Any use keeps a sliver.

    /// <param name="bars">Top to bottom.</param>
    /// <param name="track">The empty part of each bar, usually translucent so it suits any taskbar.</param>
    public static byte[] Create(int size, IReadOnlyList<TrayBar> bars, uint track)
    {
        (int margin, int gap, int barHeight, int top) = Layout(size, bars.Count);
        int trackWidth = size - 2 * margin;
        int maskStride = (size + 31) / 32 * 4;
        int pixelBytes = size * size * 4;

        byte[] image = new byte[HeaderSize + pixelBytes + maskStride * size];
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0), HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(4), size);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(8), size * 2); // XOR bitmap plus AND mask.
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(14), 32);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(20), pixelBytes + maskStride * size);

        // The AND mask stays zero; the alpha channel does the cutout.
        for (int i = 0; i < bars.Count; i++)
        {
            int filled = FilledColumns(bars[i].Fraction, trackWidth);
            int barTop = top + i * (barHeight + gap);
            for (int row = 0; row < barHeight && barTop + row < size; row++)
            {
                int bitmapRow = size - 1 - (barTop + row); // DIB rows run bottom to top.
                for (int col = 0; col < trackWidth; col++)
                {
                    uint color = col < filled ? bars[i].Color : track; // Fills from the left.
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        image.AsSpan(HeaderSize + (bitmapRow * size + margin + col) * 4), color);
                }
            }
        }

        return image;
    }
}
