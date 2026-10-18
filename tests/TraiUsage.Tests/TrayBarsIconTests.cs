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
    public void LayoutFitsFourBarsInTheIcon()
    {
        (int margin, int gap, int barHeight, int top) = TrayBarsIcon.Layout(Size, 4);

        Assert.AreEqual((1, 1, 2, 2), (margin, gap, barHeight, top));
        Assert.IsLessThanOrEqualTo(Size - margin, top + 4 * barHeight + 3 * gap);
    }

    [TestMethod]
    public void ALoneBarIsCappedAndCentered()
    {
        (_, _, int barHeight, int top) = TrayBarsIcon.Layout(Size, 1);

        Assert.AreEqual(6, barHeight);
        Assert.AreEqual(5, top);
    }

    [TestMethod]
    public void BarsFillFromTheLeft()
    {
        byte[] image = TrayBarsIcon.Create(Size, [new TrayBar(0.5, Blue), new TrayBar(1, Red)], Track);
        (_, _, int barHeight, int top) = TrayBarsIcon.Layout(Size, 2);
        int second = top + barHeight + 1;

        Assert.AreEqual(Blue, Pixel(image, 1, top), "left of a half-full bar is filled");
        Assert.AreEqual(Track, Pixel(image, 14, top), "right of a half-full bar is empty");
        Assert.AreEqual(Red, Pixel(image, 14, second), "a full second bar is filled to the right");
        Assert.AreEqual(0u, Pixel(image, 0, top), "outside the bars is transparent");
    }

    [TestMethod]
    public void AnyUsageKeepsASliver()
    {
        Assert.AreEqual(0, TrayBarsIcon.FilledColumns(0, 14));
        Assert.AreEqual(1, TrayBarsIcon.FilledColumns(0.01, 14));
        Assert.AreEqual(14, TrayBarsIcon.FilledColumns(1.5, 14));
    }

    [TestMethod]
    public void HeaderDescribesAStackedXorAndMaskBitmap()
    {
        byte[] image = TrayBarsIcon.Create(Size, [new TrayBar(0, Blue), new TrayBar(0, Red)], Track);

        Assert.AreEqual(Size, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4)));
        Assert.AreEqual(Size * 2, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8)));
        Assert.AreEqual(40 + Size * Size * 4 + 4 * Size, image.Length);
    }

    private static UsageReport Report(UsageProvider provider, params UsageLimit[] limits) =>
        new(limits, DateTimeOffset.UtcNow) { Provider = provider };

    [TestMethod]
    public void SelectionFollowsTheChosenKeys()
    {
        UsageReport claude = Report(UsageProvider.Claude,
            new UsageLimit(UsageLimitKind.Session, 10, null), new UsageLimit(UsageLimitKind.Weekly, 20, null),
            new UsageLimit(UsageLimitKind.WeeklyModel, 25, null, "Sonnet"));
        UsageReport copilot = Report(UsageProvider.Copilot, new UsageLimit(UsageLimitKind.Monthly, 30, null, "Premium requests"));

        Assert.HasCount(3, TrayBarSelection.Select([claude, copilot], TrayBarSelection.Defaults));
        Assert.HasCount(1, TrayBarSelection.Select([claude, copilot], [TrayBarSelection.CopilotPremium]));
        Assert.AreEqual(20, TrayBarSelection.Select([claude], [TrayBarSelection.ClaudeWeekly])[0].Percent);
        Assert.IsEmpty(TrayBarSelection.Select([claude, copilot], []));
        Assert.IsEmpty(TrayBarSelection.Select([claude], [TrayBarSelection.CopilotChat]), "a chosen limit the report lacks is skipped");
    }

    [TestMethod]
    public void StoredChoiceRoundTripsAndEmptyIsNotTheDefault()
    {
        CollectionAssert.AreEqual(TrayBarSelection.Defaults.ToArray(), TrayBarSelection.Parse(null).ToArray());
        Assert.IsEmpty(TrayBarSelection.Parse(TrayBarSelection.Format([])));
        CollectionAssert.AreEqual(
            new[] { TrayBarSelection.ClaudeWeekly, TrayBarSelection.CopilotChat },
            TrayBarSelection.Parse(TrayBarSelection.Format([TrayBarSelection.ClaudeWeekly, TrayBarSelection.CopilotChat])).ToArray());
    }
}
