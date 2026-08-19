namespace TextAutoCorrect.Native;

public sealed class ForegroundWindowHolder
{
    public IntPtr Window { get; private set; }

    public IntPtr Capture() => Window = NativeMethods.GetForegroundWindow();

    public void Clear() => Window = IntPtr.Zero;
}
