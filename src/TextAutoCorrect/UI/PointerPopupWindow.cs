using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;

namespace TextAutoCorrect.UI;

internal abstract class PointerPopupWindow : Window
{
    protected Border RootBorder { get; }

    protected PointerPopupWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        RootBorder = new Border { Opacity = 0 };
        Content = RootBorder;
        PopupPlacementHelper.ApplyNoActivateTopmost(this);
    }

    internal void ShowNearPointer()
    {
        Show();
        PopupPlacementHelper.PlaceNearPointer(this);
        FadeIn();
    }

    protected void FadeIn()
    {
        RootBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    protected void FadeOutAndClose(Action? onClosed = null)
    {
        if (!IsLoaded)
        {
            try { Close(); } catch { /* ignore */ }
            onClosed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(RootBorder.Opacity, 0, TimeSpan.FromMilliseconds(180));
        animation.Completed += (_, _) =>
        {
            try { Close(); } catch { /* ignore */ }
            onClosed?.Invoke();
        };
        RootBorder.BeginAnimation(OpacityProperty, animation);
    }
}

internal static class PopupIcons
{
    public const string Progress = "⟳";
    public const string Success = "✓";
    public const string NoChanges = "≡";
    public const string Error = "✕";
    public const string Info = "ℹ";
}
