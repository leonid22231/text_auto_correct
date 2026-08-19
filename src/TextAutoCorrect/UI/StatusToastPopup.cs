using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace TextAutoCorrect.UI;

internal enum StatusPopupKind
{
    NoChanges,
    Info,
    Error
}

internal sealed class StatusToastPopup : PointerPopupWindow
{
    private readonly PopupDismissWatcher? _dismissWatcher;
    private readonly DispatcherTimer? _closeTimer;

    public StatusToastPopup(StatusPopupKind kind, string message, int autoCloseSeconds, bool dismissible)
    {
        RootBorder.MinWidth = 180;
        RootBorder.MaxWidth = 420;
        RootBorder.Padding = new Thickness(12, 10, 14, 10);
        RootBorder.CornerRadius = new CornerRadius(12);
        RootBorder.Effect = PopupStyles.DropShadow();

        var (bg, border, icon, iconColor, title) = kind switch
        {
            StatusPopupKind.NoChanges => (
                Color.FromRgb(248, 250, 252), Color.FromRgb(203, 213, 225),
                PopupIcons.NoChanges, Color.FromRgb(100, 116, 139), "Без изменений"),
            StatusPopupKind.Error => (
                Color.FromRgb(255, 251, 251), Color.FromRgb(254, 202, 202),
                PopupIcons.Error, Color.FromRgb(220, 38, 38), "Ошибка"),
            _ => (
                Color.FromRgb(239, 246, 255), Color.FromRgb(191, 219, 254),
                PopupIcons.Info, Color.FromRgb(37, 99, 235), "Информация")
        };

        RootBorder.Background = new SolidColorBrush(bg);
        RootBorder.BorderBrush = new SolidColorBrush(border);
        RootBorder.BorderThickness = new Thickness(1);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = kind == StatusPopupKind.NoChanges ? 20 : 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(iconColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(iconBlock, 0);
        grid.Children.Add(iconBlock);

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
        });

        if (!string.IsNullOrWhiteSpace(message) && kind != StatusPopupKind.NoChanges)
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = PopupPlacementHelper.Compact(message, 140),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);
        RootBorder.Child = grid;

        if (dismissible)
        {
            _dismissWatcher = new PopupDismissWatcher();
        }
        else
        {
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(autoCloseSeconds, 2)) };
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                FadeOutAndClose();
            };
        }

        Loaded += (_, _) =>
        {
            ShowNearPointer();
            if (_dismissWatcher is not null)
                _dismissWatcher.Attach(this, () => FadeOutAndClose());
            else
                _closeTimer?.Start();
        };

        Closed += (_, _) => _dismissWatcher?.Dispose();
    }
}
