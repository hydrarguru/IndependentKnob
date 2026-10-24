using System;

namespace IndependentKnob;

// Cycle mode's double-press and press-and-hold mute triggers: decides what a knob event means once
// presses are timed. It has no timer of its own (App runs one and calls Expired), so it is testable.
//   route(press, timedMute): acts on a press via the Router; returns true to swallow the key.
//   anyPlaying(): whether any listed app has audio, i.e. whether a press is worth holding back.
public sealed class PressTiming(Settings settings, Func<KnobEvent, bool, bool> route, Func<bool> anyPlaying,
    Action<TimeSpan> startTimer, Action stopTimer)
{
    public static readonly TimeSpan DoublePressWindow = TimeSpan.FromMilliseconds(300), HoldTime = TimeSpan.FromMilliseconds(500);

    KnobEvent? pending; // the press being timed

    // Every knob event from the keyboard hook; returns true to swallow the key.
    public bool Knob(KnobEvent e)
    {
        if (e.Kind == KnobKind.Release)
        {
            // Press and hold: let go before the hold time, so it was a short press: switch app.
            if (settings.MuteTrigger == MuteTrigger.LongPress && Take() is { } press) route(press, false);
            return false; // key-ups are swallowed or not to match their key-down; the hook decides
        }

        if (settings.Mode == TargetMode.Cycle && e.Kind == KnobKind.Press && settings.MuteTrigger != MuteTrigger.ModifierPress)
        {
            // Second press of a double-press (the hook folds a held button's repeats into one press, so only
            // double-press gets here). Routes the first press, so letting go of the hold key in between still mutes.
            if (Take() is { } first) return route(first, true);
            if (!Router.Active(settings, e) || !anyPlaying()) return route(e, false);
            // The hook must answer now, before we know what kind of press this is.
            pending = e;
            startTimer(settings.MuteTrigger == MuteTrigger.LongPress ? HoldTime : DoublePressWindow);
            return true;
        }
        return route(e, false);
    }

    // The timer App started ran out. Double-press: no second press came, so switch app. Press and hold: still held, so mute.
    public void Expired()
    {
        if (Take() is { } press) route(press, settings.MuteTrigger == MuteTrigger.LongPress);
    }

    KnobEvent? Take()
    {
        var press = pending;
        pending = null;
        if (press != null) stopTimer();
        return press;
    }
}
