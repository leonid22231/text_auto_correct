using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Models;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;

namespace TextAutoCorrect.UI;

internal enum StatusVisualKind
{
    Progress,
    Success,
    Error,
    Neutral
}

internal sealed class WorkflowPopup : PointerPopupWindow
{
    private readonly UiSettings _settings;
    private readonly StackPanel _rootPanel = new();
    private readonly StackPanel _statusHost = new();
    private readonly TextBlock _iconText;
    private readonly TextBlock _statusText;
    private readonly TextBlock _detailText;
    private readonly StackPanel _extraPanel = new();
    private readonly PopupDismissWatcher _dismissWatcher = new();
    private TaskCompletionSource<string?>? _selectionSource;
    private bool _dismissible;
    private bool _isClosing;
    private RotateTransform? _spinner;

    public WorkflowPopup(UiSettings settings)
    {
        _settings = settings;

        RootBorder.MinWidth = 240;
        RootBorder.MaxWidth = 520;
        RootBorder.Padding = new Thickness(12, 10, 14, 10);
        RootBorder.CornerRadius = new CornerRadius(12);
        RootBorder.Background = new SolidColorBrush(Color.FromRgb(250, 252, 255));
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        RootBorder.BorderThickness = new Thickness(1);
        RootBorder.Effect = PopupStyles.DropShadow();

        _statusHost.Orientation = Orientation.Horizontal;
        _statusHost.Opacity = 1;

        _iconText = new TextBlock
        {
            Text = PopupIcons.Progress,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        var textPanel = new StackPanel();
        _statusText = new TextBlock
        {
            Text = "Ищу текст...",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            TextWrapping = TextWrapping.Wrap
        };
        _detailText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            Visibility = Visibility.Collapsed
        };
        textPanel.Children.Add(_statusText);
        textPanel.Children.Add(_detailText);

        _statusHost.Children.Add(_iconText);
        _statusHost.Children.Add(textPanel);

        _rootPanel.Children.Add(_statusHost);
        _rootPanel.Children.Add(_extraPanel);
        RootBorder.Child = _rootPanel;

        StartSpinner();

        Loaded += (_, _) => ShowNearPointer();
        Closed += (_, _) =>
        {
            StopSpinner();
            _dismissWatcher.Dispose();
            if (_selectionSource is not null && !_selectionSource.Task.IsCompleted)
                _selectionSource.TrySetResult(null);
        };
    }

    public void SetStage(CorrectionWorkflowStage stage, string? detail = null)
    {
        switch (stage)
        {
            case CorrectionWorkflowStage.SearchingText:
                ResetExtraPanel();
                TransitionStatus("Ищу текст...", detail, StatusVisualKind.Progress);
                break;

            case CorrectionWorkflowStage.NotFound:
                TransitionStatus("Не найден", detail, StatusVisualKind.Progress);
                break;

            case CorrectionWorkflowStage.TryingCopy:
                TransitionStatus("Пытаюсь скопировать...", detail, StatusVisualKind.Progress);
                break;

            case CorrectionWorkflowStage.TextFound:
                TransitionStatus("Найден", detail, StatusVisualKind.Success);
                break;

            case CorrectionWorkflowStage.Correcting:
                TransitionStatus(
                    string.IsNullOrWhiteSpace(detail) ? "Исправляю..." : detail,
                    null,
                    StatusVisualKind.Progress);
                break;

            case CorrectionWorkflowStage.SelectingVariant:
                TransitionStatus("Выберите вариант", null, StatusVisualKind.Neutral);
                break;

            case CorrectionWorkflowStage.Result:
            case CorrectionWorkflowStage.NoChanges:
            case CorrectionWorkflowStage.Error:
                break;
        }
    }

    public void ShowResult(string original, string applied, CorrectionResult result)
    {
        ResetExtraPanel();
        TransitionStatus("Готово", null, StatusVisualKind.Success, () =>
        {
            _extraPanel.Children.Add(BuildResultBlock(original, applied));

            if (result.AllOptions().Count() > 1)
                AppendAlternateOptions(result, applied);

            EnableDismiss();
        });
    }

    public void ShowNoChanges(string original)
    {
        ResetExtraPanel();
        TransitionStatus("Без изменений", PopupPlacementHelper.Compact(original, 220), StatusVisualKind.Neutral, EnableDismiss);
    }

    public void ShowError(string message)
    {
        ResetExtraPanel();
        TransitionStatus("Ошибка", message, StatusVisualKind.Error, EnableDismiss);
    }

    public void BeginVariantSelection(
        string original,
        CorrectionResult result,
        TaskCompletionSource<string?> selectionSource)
    {
        _selectionSource = selectionSource;
        ResetExtraPanel();
        SetStage(CorrectionWorkflowStage.SelectingVariant);

        _extraPanel.Children.Add(new TextBlock
        {
            Text = PopupPlacementHelper.Compact(original, 220),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextDecorations = TextDecorations.Strikethrough,
            Margin = new Thickness(0, 10, 0, 6)
        });

        var options = result.AllOptions().ToList();
        for (var i = 0; i < options.Count; i++)
            _extraPanel.Children.Add(CreateVariantButton(options[i], i));

        if (_settings.ShowNotesInDialog && !string.IsNullOrWhiteSpace(result.Notes))
        {
            _extraPanel.Children.Add(new TextBlock
            {
                Text = result.Notes,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }
    }

    private void TransitionStatus(
        string status,
        string? detail,
        StatusVisualKind kind,
        Action? afterTransition = null)
    {
        if (!IsLoaded)
        {
            ApplyStatus(status, detail, kind);
            afterTransition?.Invoke();
            return;
        }

        _statusHost.BeginAnimation(UIElement.OpacityProperty, null);

        var fadeOut = new DoubleAnimation(_statusHost.Opacity, 0, TimeSpan.FromMilliseconds(110));
        fadeOut.Completed += (_, _) =>
        {
            ApplyStatus(status, detail, kind);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
            fadeIn.Completed += (_, _) => afterTransition?.Invoke();
            _statusHost.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        _statusHost.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ApplyStatus(string status, string? detail, StatusVisualKind kind)
    {
        _statusText.Text = status;
        StopSpinner();

        switch (kind)
        {
            case StatusVisualKind.Success:
                _iconText.Text = PopupIcons.Success;
                _iconText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                break;

            case StatusVisualKind.Error:
                _iconText.Text = PopupIcons.Error;
                _iconText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                break;

            case StatusVisualKind.Neutral:
                _iconText.Text = PopupIcons.NoChanges;
                _iconText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                break;

            default:
                _iconText.Text = PopupIcons.Progress;
                _iconText.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                StartSpinner();
                break;
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            _detailText.Text = detail;
            _detailText.Visibility = Visibility.Visible;
        }
        else
        {
            _detailText.Text = string.Empty;
            _detailText.Visibility = Visibility.Collapsed;
        }
    }

    private Button CreateVariantButton(string option, int index)
    {
        var button = new Button
        {
            Tag = option,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Content = new TextBlock
            {
                Text = $"{index + 1}. {PopupPlacementHelper.Compact(option, 180)}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                FontSize = 12
            }
        };

        button.Click += (_, _) => CompleteSelection(option);
        return button;
    }

    private void CompleteSelection(string option)
    {
        if (_selectionSource is null || _selectionSource.Task.IsCompleted)
            return;

        _selectionSource.TrySetResult(option);
        ResetExtraPanel();
        SetStage(CorrectionWorkflowStage.Correcting, "Заменяю текст...");
    }

    private UIElement BuildResultBlock(string original, string applied)
    {
        var panel = new StackPanel();
        var borderPanel = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(248, 255, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0),
            Child = panel
        };

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
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = PopupPlacementHelper.Compact(applied, 220),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87))
        });

        return borderPanel;
    }

    private void AppendAlternateOptions(CorrectionResult result, string applied)
    {
        var others = result.AllOptions()
            .Where(x => !string.Equals(x, applied, StringComparison.Ordinal))
            .ToList();

        if (others.Count == 0)
            return;

        _extraPanel.Children.Add(new TextBlock
        {
            Text = "Другие варианты:",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 8, 0, 4)
        });

        foreach (var option in others)
        {
            _extraPanel.Children.Add(new TextBlock
            {
                Text = $"• {PopupPlacementHelper.Compact(option, 180)}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 0, 0, 2)
            });
        }
    }

    private void ResetExtraPanel() => _extraPanel.Children.Clear();

    private void StartSpinner()
    {
        StopSpinner();
        _spinner = new RotateTransform();
        _iconText.RenderTransform = _spinner;
        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _spinner.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void StopSpinner()
    {
        if (_spinner is null)
            return;

        _spinner.BeginAnimation(RotateTransform.AngleProperty, null);
        _iconText.RenderTransform = null;
        _spinner = null;
    }

    private void EnableDismiss()
    {
        if (_dismissible)
            return;

        _dismissible = true;
        _dismissWatcher.Attach(this, () =>
        {
            if (_isClosing)
                return;

            _isClosing = true;
            FadeOutAndClose();
        });
    }
}
