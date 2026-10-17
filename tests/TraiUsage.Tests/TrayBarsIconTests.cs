using System.Buffers.Binary;
using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class TrayBarsIconTests
{
    private const uint Blue = 0xFF0000FF;
    private const uint Red = 0xFFFF0000;
    private const uint Track = 0x40808080;
    private const int Size = 16;

    // Reads a pixel by its position from the top left, undoing the DIB's bottom-up rows.
    private static uint Pixel(byte[] image, int x, int y) =>
        BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(40 + ((Size - 1 - y) * Size + x) * 4));

    [TestMethod]
    public void LayoutFitsTwoBarsInTheIcon()
    {
        (int margin, int gap, int barWidth) = TrayBarsIcon.Layout(Size);

        Assert.AreEqual((1, 2, 6), (margin, gap, barWidth));
        Assert.IsLessThanOrEqualTo(Size, 2 * margin + gap + 2 * barWidth);
    }

    [TestMethod]
    public void BarsFillFromTheBottom()
    {
        byte[] image = TrayBarsIcon.Create(Size, new TrayBar(0.5, Blue), new TrayBar(1, Red), Track);

        Assert.AreEqual(Track, Pixel(image, 2, 1), "top of a half-full session bar is empty");
        Assert.AreEqual(Blue, Pixel(image, 2, Size - 2), "bottom of the session bar is filled");
        Assert.AreEqual(Red, Pixel(image, 10, 1), "a full week bar is filled to the top");
        Assert.AreEqual(0u, Pixel(image, 0, 8), "outside the bars is transparent");
    }

    [TestMethod]
    public void AnyUsageKeepsASliver()
    {
        Assert.AreEqual(0, TrayBarsIcon.FilledRows(0, 14));
        Assert.AreEqual(1, TrayBarsIcon.FilledRows(0.01, 14));
        Assert.AreEqual(14, TrayBarsIcon.FilledRows(1.5, 14));
    }

    [TestMethod]
    public void HeaderDescribesAStackedXorAndMaskBitmap()
    {
        byte[] image = TrayBarsIcon.Create(Size, new TrayBar(0, Blue), new TrayBar(0, Red), Track);

        Assert.AreEqual(Size, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4)));
        Assert.AreEqual(Size * 2, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8)));
        Assert.AreEqual(40 + Size * Size * 4 + 4 * Size, image.Length);
    }
}
