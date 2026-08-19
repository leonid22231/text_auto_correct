using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace TextAutoCorrect.UI;

internal sealed class ProgressPopup : PointerPopupWindow
{
    private readonly TextBlock _spinnerText;

    public ProgressPopup(string message)
    {
        RootBorder.MinWidth = 200;
        RootBorder.MaxWidth = 360;
        RootBorder.Padding = new Thickness(12, 10, 14, 10);
        RootBorder.CornerRadius = new CornerRadius(12);
        RootBorder.Background = new SolidColorBrush(Color.FromRgb(250, 252, 255));
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        RootBorder.BorderThickness = new Thickness(1);
        RootBorder.Effect = PopupStyles.DropShadow();

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        _spinnerText = new TextBlock
        {
            Text = PopupIcons.Progress,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        _spinnerText.RenderTransform = new RotateTransform();
        panel.Children.Add(_spinnerText);
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        RootBorder.Child = panel;
        Loaded += (_, _) => StartSpinner();
        Closed += (_, _) => _spinnerText.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private void StartSpinner()
    {
        if (_spinnerText.RenderTransform is not RotateTransform rotate)
            return;

        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }
}

internal static class PopupStyles
{
    public static System.Windows.Media.Effects.DropShadowEffect DropShadow() => new()
    {
        BlurRadius = 18,
        ShadowDepth = 0,
        Opacity = 0.24,
        Color = Colors.Black
    };
}
