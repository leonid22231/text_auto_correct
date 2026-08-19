using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Native;

namespace TextAutoCorrect.Services;

public sealed class KeyboardSimulator
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    private readonly ClipboardSettings _settings;
    private readonly ForegroundWindowHolder _foregroundWindowHolder;
    private readonly ILogger<KeyboardSimulator> _logger;

    public KeyboardSimulator(
        IOptions<AppSettings> options,
        ForegroundWindowHolder foregroundWindowHolder,
        ILogger<KeyboardSimulator> logger)
    {
        _settings = options.Value.Clipboard;
        _foregroundWindowHolder = foregroundWindowHolder;
        _logger = logger;
    }

    public Task SendCopyAsync(CancellationToken cancellationToken = default)
    {
        return SendChordAsync(NativeMethods.VkLControl, NativeMethods.VkC, cancellationToken);
    }

    public Task SendPasteAsync(CancellationToken cancellationToken = default)
    {
        return SendChordAsync(NativeMethods.VkLControl, NativeMethods.VkV, cancellationToken);
    }

    public Task RunWithInputFocusAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        return RunWithInputFocusAsync(async () =>
        {
            await action();
            return true;
        }, cancellationToken);
    }

    public Task<T> RunWithInputFocusAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        return InvokeOnUiThreadAsync(async () =>
        {
            var targetWindow = _foregroundWindowHolder.Window;
            if (targetWindow == IntPtr.Zero)
                targetWindow = NativeMethods.GetForegroundWindow();

            await Task.Delay(Math.Max(_settings.ForegroundPrepareDelayMs, 50), cancellationToken);
            EnsureForegroundWindow(targetWindow);

            try
            {
                return await action();
            }
            finally
            {
                if (targetWindow != IntPtr.Zero)
                    EnsureForegroundWindow(targetWindow);
            }
        }, cancellationToken);
    }

    private void EnsureForegroundWindow(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
            return;

        if (NativeMethods.GetForegroundWindow() == targetWindow)
            return;

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        var attachedForeground = false;
        var attachedTarget = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedForeground = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

            if (targetThread != 0 && targetThread != currentThread)
                attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);

            NativeMethods.BringWindowToTop(targetWindow);
            NativeMethods.SetForegroundWindow(targetWindow);

            var active = NativeMethods.GetForegroundWindow() == targetWindow;
            _logger.LogDebug("Foreground prepared for hwnd={Hwnd}, success={Success}", targetWindow, active);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare foreground window hwnd={Hwnd}", targetWindow);
        }
        finally
        {
            if (attachedTarget)
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);

            if (attachedForeground)
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private Task SendChordAsync(ushort modifier, ushort key, CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            SendInputs([
                CreateKeyInput(modifier, keyUp: false),
                CreateKeyInput(key, keyUp: false),
                CreateKeyInput(key, keyUp: true),
                CreateKeyInput(modifier, keyUp: true)
            ]);
            return DelayAsync(cancellationToken);
        }, cancellationToken);
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        var scan = (ushort)NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MapvkVkToVsc);
        return new NativeMethods.INPUT
        {
            Type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                Ki = new NativeMethods.KEYBDINPUT
                {
                    Vk = virtualKey,
                    Scan = scan,
                    Flags = keyUp
                        ? NativeMethods.KeyeventfKeyup
                        : 0,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static void SendInputs(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent == inputs.Length)
            return;

        var error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException(
            $"SendInput failed (sent {sent}/{inputs.Length}, win32={error}, inputSize={InputSize}).");
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (_settings.KeyDelayMs > 0)
            await Task.Delay(_settings.KeyDelayMs, cancellationToken);
    }

    private Task InvokeOnUiThreadAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return dispatcher.InvokeAsync(action, DispatcherPriority.Input).Task.Unwrap();
    }

    private Task<T> InvokeOnUiThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return dispatcher.InvokeAsync(action, DispatcherPriority.Input).Task.Unwrap();
    }

    public static uint BuildModifiers(HotkeySettings settings)
    {
        uint modifiers = 0;
        if (settings.Alt) modifiers |= (uint)NativeMethods.HotkeyModifiers.Alt;
        if (settings.Control) modifiers |= (uint)NativeMethods.HotkeyModifiers.Control;
        if (settings.Shift) modifiers |= (uint)NativeMethods.HotkeyModifiers.Shift;
        if (settings.Win) modifiers |= (uint)NativeMethods.HotkeyModifiers.Win;
        return modifiers;
    }

    public static uint ResolveVirtualKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            throw new ArgumentException("Hotkey key name is required.", nameof(keyName));

        if (Enum.TryParse<Key>(keyName.Trim(), ignoreCase: true, out var wpfKey))
            return (uint)KeyInterop.VirtualKeyFromKey(wpfKey);

        throw new ArgumentException($"Unsupported hotkey key: {keyName}", nameof(keyName));
    }
}
