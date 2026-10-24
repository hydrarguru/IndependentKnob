using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndependantKnob;

public enum TargetMode { Cycle, Focused, Modifiers }

public enum MuteTrigger { DoublePress, ModifierPress, LongPress }

public record AppEntry(string ProcessName, string DisplayName, Modifier Modifier = Modifier.None);

public class Settings
{
    public TargetMode Mode { get; set; } = TargetMode.Cycle;
    public MuteTrigger MuteTrigger { get; set; } = MuteTrigger.DoublePress;
    public Modifier MuteModifier { get; set; } = Modifier.Ctrl;
    // Cycle/Focused: when set, the knob only controls apps while this key is held; otherwise it's Windows volume.
    public Modifier ActivationModifier { get; set; } = Modifier.None;
    public List<AppEntry> Apps { get; set; } = [];
    public string? DefaultApp { get; set; }
    // Process names the user said "Never" to (or removed); not suggested again.
    public List<string> IgnoredApps { get; set; } = [];

    // The first audio app worth suggesting: not listed, not ignored, not put off with "Not now".
    public (string ProcessName, string DisplayName)? NextSuggestion(
        IEnumerable<(string ProcessName, string DisplayName)> sessions, IReadOnlySet<string> notNow)
    {
        foreach (var s in sessions)
            if (!Apps.Exists(a => Same(a.ProcessName, s.ProcessName))
                && !IgnoredApps.Exists(i => Same(i, s.ProcessName))
                && !notNow.Contains(s.ProcessName))
                return s;
        return null;
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndependantKnob", "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return new Settings();
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
        }
        catch (JsonException)
        {
            // Keep the user's broken file so the next Save doesn't destroy it.
            File.Copy(path, path + ".bak", overwrite: true);
            return new Settings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write then swap, so a crash mid-write can't leave a half-written file.
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    // Save for UI callers: returns the error message instead of throwing on disk/permission problems.
    public string? TrySave(string path)
    {
        try
        {
            Save(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
