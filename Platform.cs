using System;
using System.Collections.Generic;

namespace IndependantKnob;

// Flags: a KnobEvent carries every modifier held, so e.g. hold-Ctrl plus Shift+press can be told apart.
[Flags]
public enum Modifier { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

// Release: the knob button was let go (for press-and-hold); it never reaches the Router.
public enum KnobKind { Up, Down, Press, Release }

public record KnobEvent(KnobKind Kind, Modifier Modifier = Modifier.None);

public record AudioState(float Volume, bool Muted);

// Apps are identified by process name; one name may own several sessions, all adjusted together.
// A null result means the app has no audio session, so the caller lets the key through.
public interface IAudio
{
    IReadOnlyList<(string ProcessName, string DisplayName)> Sessions();
    bool HasSession(string app);
    AudioState? TryGet(string app);
    AudioState? TryAdjust(string app, float step);
    AudioState? TryToggleMute(string app);
}

public interface IForeground
{
    string? ProcessName();
}

public interface IKnobInput
{
    // Return true to swallow the key, false to pass it on to Windows.
    event Func<KnobEvent, bool>? Knob;
}
