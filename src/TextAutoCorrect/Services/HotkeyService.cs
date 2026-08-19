using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Native;

namespace TextAutoCorrect.Services;

public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 9001;

    private readonly HotkeySettings _settings;
    private readonly ILogger<HotkeyService> _logger;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public HotkeyService(IOptions<AppSettings> options, ILogger<HotkeyService> logger)
    {
        _settings = options.Value.Hotkey;
        _logger = logger;
    }

    public bool Register()
    {
        if (_registered)
            return true;

        EnsureMessageWindow();
        var modifiers = KeyboardSimulator.BuildModifiers(_settings);
        var virtualKey = KeyboardSimulator.ResolveVirtualKey(_settings.Key);

        _registered = NativeMethods.RegisterHotKey(_source!.Handle, HotkeyId, modifiers, virtualKey);
        if (_registered)
        {
            _logger.LogInformation(
                "Hotkey registered: Ctrl={Control}, Shift={Shift}, Alt={Alt}, Win={Win}, Key={Key}",
                _settings.Control, _settings.Shift, _settings.Alt, _settings.Win, _settings.Key);
        }
        else
        {
            _logger.LogError(
                "Hotkey registration failed: Ctrl={Control}, Shift={Shift}, Alt={Alt}, Win={Win}, Key={Key}",
                _settings.Control, _settings.Shift, _settings.Alt, _settings.Win, _settings.Key);
        }

        return _registered;
    }

    public void Unregister()
    {
        if (!_registered || _source is null)
            return;

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        _logger.LogDebug("Hotkey unregistered.");
    }

    private void EnsureMessageWindow()
    {
        if (_source is not null)
            return;

        var parameters = new HwndSourceParameters("TextAutoCorrectHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = IntPtr.Zero
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _logger.LogDebug("Hotkey message window created.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            try
            {
                _logger.LogDebug("Hotkey pressed.");
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hotkey handler failed.");
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
        _source = null;
    }
}
