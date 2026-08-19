using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Models;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace TextAutoCorrect.UI;

internal enum CorrectionPopupMode
{
    Select,
    Preview
}

internal sealed class CorrectionResultPopup : PointerPopupWindow
{
    private readonly PopupDismissWatcher _dismissWatcher = new();
    private readonly TaskCompletionSource<string?>? _selectionSource;
    private readonly CorrectionPopupMode _mode;
    private string? _appliedText;
    private bool _isClosing;

    public CorrectionResultPopup(
        string original,
        CorrectionResult result,
        CorrectionPopupMode mode,
        string? appliedText,
        UiSettings settings,
        TaskCompletionSource<string?>? selectionSource = null)
    {
        _mode = mode;
        _selectionSource = selectionSource;
        _appliedText = appliedText;

        RootBorder.MinWidth = 280;
        RootBorder.MaxWidth = 520;
        RootBorder.Padding = new Thickness(12);
        RootBorder.CornerRadius = new CornerRadius(12);
        RootBorder.Background = new SolidColorBrush(Color.FromRgb(248, 255, 251));
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
        RootBorder.BorderThickness = new Thickness(1);
        RootBorder.Effect = PopupStyles.DropShadow();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = mode == CorrectionPopupMode.Select ? "Выберите вариант" : "Исправлено",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = PopupPlacementHelper.Compact(original, 220),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextDecorations = TextDecorations.Strikethrough,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "⇒",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (mode == CorrectionPopupMode.Preview && !string.IsNullOrWhiteSpace(appliedText))
        {
            panel.Children.Add(new TextBlock
            {
                Text = PopupPlacementHelper.Compact(appliedText, 220),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87)),
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        var options = result.AllOptions().ToList();
        if (options.Count > 1 || mode == CorrectionPopupMode.Select)
        {
            panel.Children.Add(new TextBlock
            {
                Text = mode == CorrectionPopupMode.Select ? "Варианты:" : "Другие варианты:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var isApplied = string.Equals(option, appliedText, StringComparison.Ordinal);
                var button = CreateVariantButton(option, i, isApplied, mode);
                panel.Children.Add(button);
            }
        }
        else if (mode == CorrectionPopupMode.Select && options.Count == 1)
        {
            panel.Children.Add(CreateVariantButton(options[0], 0, false, mode));
        }

        if (settings.ShowNotesInDialog && !string.IsNullOrWhiteSpace(result.Notes))
        {
            panel.Children.Add(new TextBlock
            {
                Text = result.Notes,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        RootBorder.Child = panel;

        Loaded += (_, _) =>
        {
            ShowNearPointer();
            _dismissWatcher.Attach(this, DismissWithoutSelection);
        };

        Closed += (_, _) =>
        {
            _dismissWatcher.Dispose();
            if (_selectionSource is not null && !_selectionSource.Task.IsCompleted)
                _selectionSource.TrySetResult(null);
        };
    }

    private Button CreateVariantButton(string option, int index, bool isApplied, CorrectionPopupMode mode)
    {
        var button = new Button
        {
            Tag = option,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = isApplied
                ? new SolidColorBrush(Color.FromRgb(220, 252, 231))
                : Brushes.White,
            BorderBrush = isApplied
                ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                : new SolidColorBrush(Color.FromRgb(214, 222, 232)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Content = new TextBlock
            {
                Text = index < 9 ? $"{index + 1}. {PopupPlacementHelper.Compact(option, 180)}" : PopupPlacementHelper.Compact(option, 180),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                FontSize = 12
            }
        };

        if (mode == CorrectionPopupMode.Select)
        {
            button.Click += (_, _) => CompleteSelection(option);
            button.MouseEnter += (_, _) =>
            {
                button.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253));
            };
            button.MouseLeave += (_, _) =>
            {
                button.Background = Brushes.White;
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232));
            };
        }

        return button;
    }

    private void CompleteSelection(string option)
    {
        if (_selectionSource is null || _selectionSource.Task.IsCompleted)
            return;

        _selectionSource.TrySetResult(option);
        Close();
    }

    private void DismissWithoutSelection()
    {
        if (_isClosing)
            return;

        _isClosing = true;

        if (_mode == CorrectionPopupMode.Preview)
        {
            FadeOutAndClose();
            return;
        }

        if (_selectionSource is not null && !_selectionSource.Task.IsCompleted)
            _selectionSource.TrySetResult(null);

        FadeOutAndClose();
    }
}
