using System;
using System.Collections.Generic;

namespace IndependantKnob;

public enum Modifier { None, Ctrl, Shift, Alt }

public enum KnobKind { Up, Down, Press }

public record KnobEvent(KnobKind Kind, Modifier Modifier = Modifier.None);

public record AudioState(float Volume, bool Muted);

// Apps are identified by process name; one name may own several sessions, all adjusted together.
// A null result means the app has no audio session, so the caller lets the key through.
public interface IAudio
{
    IReadOnlyList<(string ProcessName, string DisplayName)> Sessions();
    bool HasSession(string app);
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
