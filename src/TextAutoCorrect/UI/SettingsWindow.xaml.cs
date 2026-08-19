using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.UI;

public partial class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;
    private readonly string _settingsPath;

    public SettingsWindow(AppSettings settings, string settingsPath)
    {
        InitializeComponent();
        _settings = settings;
        _settingsPath = settingsPath;
        LoadValues();
    }

    private void LoadValues()
    {
        SelectProvider(_settings.Ai.Provider);
        AuthorizationKeyBox.Password = _settings.Ai.AuthorizationKey;
        ApiKeyBox.Password = _settings.Ai.ApiKey;
        ClientIdBox.Text = _settings.Ai.ClientId;
        ScopeBox.Text = _settings.Ai.Scope;
        AuthUrlBox.Text = _settings.Ai.AuthUrl;
        BaseUrlBox.Text = _settings.Ai.BaseUrl;
        ModelBox.Text = _settings.Ai.Model;
        TemperatureBox.Text = _settings.Ai.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MaxTokensBox.Text = _settings.Ai.MaxTokens.ToString();
        TimeoutBox.Text = _settings.Ai.TimeoutSeconds.ToString();
        IgnoreSslBox.IsChecked = _settings.Ai.IgnoreSslErrors;
        AutoStartBox.IsChecked = _settings.Ui.AutoStartOnWindowsStartup;
        HotkeyKeyBox.Text = _settings.Hotkey.Key;
        HotkeyCtrlBox.IsChecked = _settings.Hotkey.Control;
        HotkeyShiftBox.IsChecked = _settings.Hotkey.Shift;
        HotkeyAltBox.IsChecked = _settings.Hotkey.Alt;
        HotkeyWinBox.IsChecked = _settings.Hotkey.Win;

        PromptModeBox.ItemsSource = Enum.GetValues<PromptMode>();
        PromptModeBox.SelectedItem = _settings.Ai.DefaultPromptMode;
    }

    private void SelectProvider(string provider)
    {
        foreach (ComboBoxItem item in ProviderBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
            {
                ProviderBox.SelectedItem = item;
                return;
            }
        }

        ProviderBox.SelectedIndex = 0;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TemperatureBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var temperature))
        {
            MessageBox.Show(this, "Temperature must be a number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MaxTokensBox.Text, out var maxTokens) ||
            !int.TryParse(TimeoutBox.Text, out var timeoutSeconds))
        {
            MessageBox.Show(this, "Max tokens and timeout must be integers.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.Ai.Provider = (ProviderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GigaChat";
        _settings.Ai.AuthorizationKey = AuthorizationKeyBox.Password;
        _settings.Ai.ApiKey = ApiKeyBox.Password;
        _settings.Ai.ClientId = ClientIdBox.Text.Trim();
        _settings.Ai.Scope = ScopeBox.Text.Trim();
        _settings.Ai.AuthUrl = AuthUrlBox.Text.Trim();
        _settings.Ai.BaseUrl = BaseUrlBox.Text.Trim();
        _settings.Ai.Model = ModelBox.Text.Trim();
        _settings.Ai.Temperature = temperature;
        _settings.Ai.MaxTokens = maxTokens;
        _settings.Ai.TimeoutSeconds = timeoutSeconds;
        _settings.Ai.IgnoreSslErrors = IgnoreSslBox.IsChecked == true;
        _settings.Ui.AutoStartOnWindowsStartup = AutoStartBox.IsChecked == true;
        _settings.Ai.DefaultPromptMode = (PromptMode)(PromptModeBox.SelectedItem ?? PromptMode.Auto);
        _settings.Hotkey.Key = HotkeyKeyBox.Text.Trim();
        _settings.Hotkey.Control = HotkeyCtrlBox.IsChecked == true;
        _settings.Hotkey.Shift = HotkeyShiftBox.IsChecked == true;
        _settings.Hotkey.Alt = HotkeyAltBox.IsChecked == true;
        _settings.Hotkey.Win = HotkeyWinBox.IsChecked == true;

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, SettingsJson));

        // Apply auto-start immediately.
        TextAutoCorrect.Infrastructure.AutoStart.WindowsAutoStartManager.SetEnabled(_settings.Ui.AutoStartOnWindowsStartup);

        var hotkeyService = ((App)Application.Current).Services.GetRequiredService<IHotkeyService>();
        hotkeyService.Unregister();
        if (!hotkeyService.Register())
        {
            MessageBox.Show(this, "Settings saved, but hotkey registration failed. Try another combination.", "Hotkey",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
