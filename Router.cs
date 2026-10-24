using System;
using System.Linq;

namespace IndependantKnob;

public enum ActionKind { PassThrough, VolumeUp, VolumeDown, ToggleMute, CycleNext }

public record KnobAction(ActionKind Kind, string? App = null);

// Selected = index into Settings.Apps of the app Cycle mode is controlling.
public record RouterState(int Selected = 0);

// Decides what a knob event does. No OS calls, so every mode is unit-testable.
public static class Router
{
    static readonly KnobAction Pass = new(ActionKind.PassThrough);

    // False when an activation modifier is set (Cycle/Focused) and not held: the key is plain Windows volume.
    public static bool Active(Settings s, KnobEvent e) =>
        s.Mode == TargetMode.Modifiers || (e.Modifier & s.ActivationModifier) == s.ActivationModifier;

    // secondPressInWindow: Cycle + DoublePress only. The caller holds the first press for a short
    // window and calls Route once: true if a second press arrived in time, false when the window ran out.
    public static (KnobAction Action, RouterState State) Route(
        Settings s, RouterState st, KnobEvent e, Func<string, bool> hasSession,
        string? foreground, bool secondPressInWindow = false)
    {
        if (!Active(s, e)) return (Pass, st);
        // The activation key is consumed; what's left is what Modifier + press mute compares against.
        if (s.Mode != TargetMode.Modifiers) e = e with { Modifier = e.Modifier & ~s.ActivationModifier };

        if (s.Mode == TargetMode.Cycle && e.Kind == KnobKind.Press)
        {
            bool mute = s.MuteTrigger == MuteTrigger.DoublePress
                ? secondPressInWindow
                : e.Modifier == s.MuteModifier;
            if (mute) return (Act(ActionKind.ToggleMute, Selected(s, st), hasSession), st);

            // Skip apps that aren't playing; wraps back to the current app if it's the only one.
            for (int i = 1; i <= s.Apps.Count; i++)
            {
                int next = (st.Selected + i) % s.Apps.Count;
                if (hasSession(s.Apps[next].ProcessName))
                    return (new KnobAction(ActionKind.CycleNext, s.Apps[next].ProcessName), new RouterState(next));
            }
            return (Pass, st);
        }

        string? app = s.Mode switch
        {
            TargetMode.Cycle => Selected(s, st),
            TargetMode.Focused => s.Apps.FirstOrDefault(a => Same(a.ProcessName, foreground))?.ProcessName ?? s.DefaultApp,
            _ => s.Apps.FirstOrDefault(a => a.Modifier == e.Modifier)?.ProcessName,
        };
        var kind = e.Kind switch
        {
            KnobKind.Up => ActionKind.VolumeUp,
            KnobKind.Down => ActionKind.VolumeDown,
            _ => ActionKind.ToggleMute,
        };
        return (Act(kind, app, hasSession), st);
    }

    static string? Selected(Settings s, RouterState st) =>
        s.Apps.Count == 0 ? null : s.Apps[st.Selected % s.Apps.Count].ProcessName;

    static KnobAction Act(ActionKind kind, string? app, Func<string, bool> hasSession) =>
        app != null && hasSession(app) ? new KnobAction(kind, app) : Pass;

    static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
