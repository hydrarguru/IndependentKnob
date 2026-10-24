using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static IndependentKnob.Windows.Native;

namespace IndependentKnob.Windows;

// Low-level keyboard hook for the volume media keys. Must be created on a thread with a message
// loop (the Avalonia UI thread). Windows drops hooks whose callback takes over ~1 s, so handlers stay fast.
[SupportedOSPlatform("windows")]
public sealed unsafe class KeyboardHook : IKnobInput
{
    // The callback is a static function pointer (nothing for the GC to collect), so it finds the hook here.
    static KeyboardHook? current;

    readonly nint handle;
    // Key-downs we swallowed; their key-ups are swallowed too so Windows never sees half a keypress.
    readonly HashSet<uint> swallowed = [];
    bool pressHeld;
    long lastPressDown;

    public event Func<KnobEvent, bool>? Knob;

    public KeyboardHook()
    {
        if (current != null) throw new InvalidOperationException("Only one keyboard hook may be installed.");
        handle = SetWindowsHookExW(WH_KEYBOARD_LL, &Callback, GetModuleHandleW(null), 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        current = this;
    }

    public void Dispose()
    {
        if (current != this) return;
        UnhookWindowsHookEx(handle);
        current = null;
    }

    [UnmanagedCallersOnly]
    static nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && current is { } hook)
        {
            // vkCode is the first field of KBDLLHOOKSTRUCT.
            uint vk = *(uint*)lParam;
            if (hook.Handle(vk, wParam)) return 1;
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    bool Handle(uint vk, nint message)
    {
        KnobKind? kind = vk switch
        {
            VK_VOLUME_UP => KnobKind.Up,
            VK_VOLUME_DOWN => KnobKind.Down,
            VK_VOLUME_MUTE => KnobKind.Press,
            _ => null,
        };
        if (kind == null) return false;
        if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            if (kind == KnobKind.Press && pressHeld)
            {
                pressHeld = false;
                Raise(new KnobEvent(KnobKind.Release, HeldModifier()));
            }
            return swallowed.Remove(vk);
        }
        if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return false;

        if (kind == KnobKind.Press)
        {
            // A held button auto-repeats key-downs; that's still one press. Turning keeps its repeats, like Windows.
            // Repeats come every ~30 ms, so the time check stops a lost key-up from blocking the button for good.
            long now = Environment.TickCount64;
            bool repeat = pressHeld && now - lastPressDown < 1000;
            lastPressDown = now;
            if (repeat) return swallowed.Contains(vk);
            pressHeld = true;
        }

        bool swallow = Raise(new KnobEvent(kind.Value, HeldModifier()));
        if (swallow) swallowed.Add(vk);
        return swallow;
    }

    bool Raise(KnobEvent e)
    {
        try
        {
            return Knob?.Invoke(e) ?? false;
        }
        catch (Exception ex)
        {
            // An exception escaping an UnmanagedCallersOnly method kills the process; let the key through instead.
            System.Diagnostics.Debug.WriteLine($"Knob handler failed: {ex}");
            return false;
        }
    }

    // GetAsyncKeyState, not GetKeyState: our thread doesn't get the keyboard messages, so its key state is stale.
    static Modifier HeldModifier() =>
        (Down(VK_CONTROL) ? Modifier.Ctrl : 0) | (Down(VK_SHIFT) ? Modifier.Shift : 0) | (Down(VK_MENU) ? Modifier.Alt : 0);

    static bool Down(int vk) => GetAsyncKeyState(vk) < 0;
}
