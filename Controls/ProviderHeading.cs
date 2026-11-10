using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TraiUsage.Controls;

/// <summary>
/// The heading above one provider's cards when several are shown. It stays pinned at the top of
/// the scroller until the next provider's heading pushes it off.
/// </summary>
public sealed class ProviderHeading : Grid
{
    private readonly TextBlock _text = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    private readonly TranslateTransform _pin = new();

    public ProviderHeading()
    {
        Padding = new Thickness(2, 4, 0, 4);
        ColumnSpacing = 4;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Above the cards so they are not drawn over it; frosted only while pinned over scrolling content.
        CornerRadius = new CornerRadius(6);
        Canvas.SetZIndex(this, 1);
        RenderTransform = _pin;

        Children.Add(_text);
        SetColumn(Warning, 1);
        Children.Add(Warning);
    }

    public WarningButton Warning { get; } = new();

    public void Show(string text, (string Title, string Message)? warning)
    {
        _text.Text = text;
        Warning.Show(warning, text);
    }

    /// <summary>Offsets the heading down by how far its section's top has scrolled past the viewport top.</summary>
    public void Pin(double scrollOffset, double sectionEnd)
    {
        double top = ActualOffset.Y;
        double max = Math.Max(0, sectionEnd - top - ActualHeight);
        _pin.Y = Math.Clamp(scrollOffset - top, 0, max);
        Background = scrollOffset > top ? (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"] : null;
    }
}
