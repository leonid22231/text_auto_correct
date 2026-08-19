namespace TextAutoCorrect.Core.Interfaces;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    bool Register();
    void Unregister();
}
