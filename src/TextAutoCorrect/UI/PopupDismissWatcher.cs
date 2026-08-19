using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using TextAutoCorrect.Native;

namespace TextAutoCorrect.UI;

internal sealed class PopupDismissWatcher : IDisposable
{
    private const int LlkhfInjected = 0x10;

    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly ILogger<PopupDismissWatcher>? _logger;
    private GCHandle _selfHandle;
    private Window? _popup;
    private Action? _dismiss;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _disposed;
    private int _dismissPending;
    private NativeMethods.RECT _popupBounds;
    private volatile bool _hasBounds;

    public PopupDismissWatcher(ILogger<PopupDismissWatcher>? logger = null)
    {
        _logger = logger;
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public void Attach(Window popup, Action dismiss)
    {
        Detach();

        _popup = popup;
        _dismiss = dismiss;
        _selfHandle = GCHandle.Alloc(this);

        popup.Loaded += OnPopupLayoutChanged;
        popup.LocationChanged += OnPopupLayoutChanged;
        popup.SizeChanged += OnPopupLayoutChanged;
        UpdatePopupBounds();

        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardProc, module, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            _logger?.LogWarning("Failed to install popup dismiss hooks. Mouse={MouseHook}, Keyboard={KeyboardHook}", _mouseHook, _keyboardHook);
    }

    private void OnPopupLayoutChanged(object? sender, EventArgs e) => UpdatePopupBounds();

    private void UpdatePopupBounds()
    {
        if (_popup is null || !_popup.IsLoaded)
            return;

        try
        {
            var topLeft = _popup.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = _popup.PointToScreen(new System.Windows.Point(_popup.ActualWidth, _popup.ActualHeight));
            _popupBounds = new NativeMethods.RECT
            {
                Left = (int)topLeft.X,
                Top = (int)topLeft.Y,
                Right = (int)bottomRight.X,
                Bottom = (int)bottomRight.Y
            };
            _hasBounds = _popupBounds.Right > _popupBounds.Left && _popupBounds.Bottom > _popupBounds.Top;
        }
        catch (Exception ex)
        {
            _hasBounds = false;
            _logger?.LogDebug(ex, "Failed to update popup bounds.");
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _popup is not null && _dismiss is not null && IsMouseDown(wParam))
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (!IsPointOverPopup(info.Pt))
                    DispatchDismiss();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Mouse hook callback failed.");
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _popup is not null && _dismiss is not null)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var isKeyUp = (info.Flags & 0x80) != 0;
                var isInjected = (info.Flags & LlkhfInjected) != 0;
                if (!isKeyUp && !isInjected)
                    DispatchDismiss();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Keyboard hook callback failed.");
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsMouseDown(IntPtr wParam)
    {
        var code = wParam.ToInt32();
        return code is NativeMethods.WmLButtonDown
            or NativeMethods.WmRButtonDown
            or NativeMethods.WmMButtonDown
            or NativeMethods.WmXButtonDown;
    }

    private bool IsPointOverPopup(NativeMethods.POINT screenPoint)
    {
        if (!_hasBounds)
            return false;

        var bounds = _popupBounds;
        return screenPoint.X >= bounds.Left &&
               screenPoint.X <= bounds.Right &&
               screenPoint.Y >= bounds.Top &&
               screenPoint.Y <= bounds.Bottom;
    }

    private void DispatchDismiss()
    {
        if (Interlocked.CompareExchange(ref _dismissPending, 1, 0) != 0)
            return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_disposed)
                    return;

                _dismiss?.Invoke();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Popup dismiss handler failed.");
            }
            finally
            {
                Interlocked.Exchange(ref _dismissPending, 0);
            }
        });
    }

    public void Detach()
    {
        if (_popup is not null)
        {
            _popup.Loaded -= OnPopupLayoutChanged;
            _popup.LocationChanged -= OnPopupLayoutChanged;
            _popup.SizeChanged -= OnPopupLayoutChanged;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();

        _popup = null;
        _dismiss = null;
        _hasBounds = false;
        Interlocked.Exchange(ref _dismissPending, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Detach();
    }
}
