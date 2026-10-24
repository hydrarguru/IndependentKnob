using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace IndependentKnob.Windows;

// Per-app volume through the Core Audio session API on the default output device.
// The device is looked up on every call, so switching speakers/headphones just works.
[SupportedOSPlatform("windows")]
public sealed class WindowsAudio : IAudio
{
    public IReadOnlyList<(string ProcessName, string DisplayName)> Sessions() =>
        With(null, all => all
            .Select(x => (x.Name, string.IsNullOrEmpty(x.Session.DisplayName) ? x.Name : x.Session.DisplayName))
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public bool HasSession(string app) => With(app, s => s.Count > 0);

    public AudioState? TryGet(string app) => With(app, s =>
        s.Count == 0 ? null : new AudioState(s[0].Session.SimpleAudioVolume.Volume, s[0].Session.SimpleAudioVolume.Mute));

    public AudioState? TryAdjust(string app, float step) => SetVolume(app, current => current + step);

    public AudioState? TrySet(string app, float volume) => SetVolume(app, _ => volume);

    // Like the Windows volume keys, changing the volume also unmutes.
    static AudioState? SetVolume(string app, Func<float, float> target) => With(app, s =>
    {
        if (s.Count == 0) return null;
        float volume = Math.Clamp(target(s[0].Session.SimpleAudioVolume.Volume), 0f, 1f);
        foreach (var (session, _) in s)
        {
            session.SimpleAudioVolume.Volume = volume;
            session.SimpleAudioVolume.Mute = false;
        }
        return new AudioState(volume, false);
    });

    public AudioState? TryToggleMute(string app) => With(app, s =>
    {
        if (s.Count == 0) return null;
        bool mute = !s[0].Session.SimpleAudioVolume.Mute;
        foreach (var (session, _) in s) session.SimpleAudioVolume.Mute = mute;
        return new AudioState(s[0].Session.SimpleAudioVolume.Volume, mute);
    });

    // Runs f over the live sessions of the default output device, filtered to one process name when app is set.
    static T With<T>(string? app, Func<List<(AudioSessionControl Session, string Name)>, T> f)
    {
        var list = new List<(AudioSessionControl Session, string Name)>();
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device)) return f(list);
        using (device)
        {
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            try
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    string? name = session.IsSystemSoundsSession || session.State == AudioSessionState.AudioSessionStateExpired
                        ? null
                        : ProcessName(session.GetProcessID);
                    if (name != null && (app == null || string.Equals(name, app, StringComparison.OrdinalIgnoreCase)))
                        list.Add((session, name));
                    else
                        session.Dispose();
                }
                return f(list);
            }
            finally
            {
                foreach (var (session, _) in list) session.Dispose();
            }
        }
    }

    internal static string? ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // process already exited
        }
    }
}
