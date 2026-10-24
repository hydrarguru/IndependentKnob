using System.Runtime.Versioning;

namespace IndependentKnob.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsForeground : IForeground
{
    public string? ProcessName()
    {
        nint window = Native.GetForegroundWindow();
        if (window == 0) return null;
        Native.GetWindowThreadProcessId(window, out uint pid);
        return WindowsAudio.ProcessName(pid);
    }
}
