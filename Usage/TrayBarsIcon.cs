using System.Buffers.Binary;

namespace TraiUsage.Usage;

/// <summary>One bar of the tray icon: how full it is (0–1) and its 0xAARRGGBB color.</summary>
internal readonly record struct TrayBar(double Fraction, uint Color);

/// <summary>
/// Draws the tray icon as two vertical bars, session on the left and week on the right. Vertical
/// because bar lengths are hard to compare along a row of horizontal tray icons. The result is
/// the image data of a 32-bit icon (a DIB header, pixels, and mask), which is what
/// <c>CreateIconFromResourceEx</c> takes.
/// </summary>
internal static class TrayBarsIcon
{
    private const int HeaderSize = 40; // BITMAPINFOHEADER

    /// <summary>Margin around the bars, the gap between them, and the width of each, in pixels.</summary>
    public static (int Margin, int Gap, int BarWidth) Layout(int size)
    {
        int margin = Math.Max(1, size / 16);
        int gap = Math.Max(1, size / 8);
        int barWidth = Math.Max(1, (size - 2 * margin - gap) / 2);
        return (margin, gap, barWidth);
    }

    /// <summary>Pixel rows (top to bottom) from which a bar of this fraction is filled, within a track of <paramref name="trackHeight"/>.</summary>
    public static int FilledRows(double fraction, int trackHeight) =>
        fraction <= 0 ? 0 : Math.Clamp((int)Math.Round(fraction * trackHeight), 1, trackHeight); // Any use keeps a sliver.

    /// <param name="track">The empty part of each bar, usually translucent so it suits any taskbar.</param>
    public static byte[] Create(int size, TrayBar session, TrayBar weekly, uint track)
    {
        (int margin, int gap, int barWidth) = Layout(size);
        int trackHeight = size - 2 * margin;
        int maskStride = (size + 31) / 32 * 4;
        int pixelBytes = size * size * 4;

        byte[] image = new byte[HeaderSize + pixelBytes + maskStride * size];
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0), HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(4), size);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(8), size * 2); // XOR bitmap plus AND mask.
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(14), 32);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(20), pixelBytes + maskStride * size);

        // Left to right: session, then week. The AND mask stays zero; the alpha channel does the cutout.
        (TrayBar Bar, int Left)[] bars = [(session, margin), (weekly, margin + barWidth + gap)];
        foreach ((TrayBar bar, int left) in bars)
        {
            int filled = FilledRows(bar.Fraction, trackHeight);
            for (int row = 0; row < trackHeight; row++)
            {
                uint color = row >= trackHeight - filled ? bar.Color : track; // Fills from the bottom.
                int y = margin + row;
                int bitmapRow = size - 1 - y; // DIB rows run bottom to top.
                for (int x = left; x < left + barWidth; x++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        image.AsSpan(HeaderSize + (bitmapRow * size + x) * 4), color);
                }
            }
        }

        return image;
    }
}
