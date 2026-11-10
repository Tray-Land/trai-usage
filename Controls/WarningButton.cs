using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TraiUsage.Controls;

/// <summary>A caution icon that opens a flyout with the problem affecting a provider's data.</summary>
public sealed class WarningButton : Button
{
    private readonly TextBlock _title = new()
    {
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly TextBlock _message = new()
    {
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };

    public WarningButton()
    {
        Width = 24;
        Height = 24;
        Padding = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        BorderThickness = new Thickness(0);
        Visibility = Visibility.Collapsed;
        Content = new FontIcon
        {
            FontSize = 14,
            Glyph = "",
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        };

        StackPanel panel = new() { MaxWidth = 280, Spacing = 6 };
        panel.Children.Add(_title);
        panel.Children.Add(_message);
        Flyout = new Flyout { Content = panel };
    }

    /// <summary>Shows the button for a problem, or hides it when there is none.</summary>
    public void Show((string Title, string Message)? warning, string owner)
    {
        Visibility = warning.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (warning is not { } problem)
        {
            return;
        }

        _title.Text = problem.Title;
        _message.Text = problem.Message;
        string name = $"Show warning for {owner}";
        AutomationProperties.SetName(this, name);
        ToolTipService.SetToolTip(this, "Show usage warning");
    }
}
