using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Native;

namespace TextAutoCorrect.Services;

public sealed class ClipboardMonitor : IClipboardMonitor
{
    private readonly object _gate = new();
    private HwndSource? _source;
    private int _suppressDepth;
    private string? _lastExternalText;
    private DateTimeOffset? _lastExternalChangeUtc;

    public void Start()
    {
        if (_source is not null)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var parameters = new HwndSourceParameters("TextAutoCorrectClipboardMonitor")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = IntPtr.Zero
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            NativeMethods.AddClipboardFormatListener(_source.Handle);
        });
    }

    public string? GetRecentText(TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_lastExternalText) || _lastExternalChangeUtc is null)
                return null;

            if (DateTimeOffset.UtcNow - _lastExternalChangeUtc.Value > maxAge)
                return null;

            return _lastExternalText;
        }
    }

    public IDisposable SuppressNotifications()
    {
        Interlocked.Increment(ref _suppressDepth);
        return new SuppressToken(() => Interlocked.Decrement(ref _suppressDepth));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmClipboardUpdate || Volatile.Read(ref _suppressDepth) > 0)
            return IntPtr.Zero;

        Application.Current?.Dispatcher.BeginInvoke(UpdateFromClipboard, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    private void UpdateFromClipboard()
    {
        if (Volatile.Read(ref _suppressDepth) > 0)
            return;

        if (!Clipboard.ContainsText())
            return;

        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_gate)
        {
            _lastExternalText = text;
            _lastExternalChangeUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Dispose()
    {
        if (_source is null)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        });
    }

    private sealed class SuppressToken(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
