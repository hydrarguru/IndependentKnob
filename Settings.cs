using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndependentKnob;

public enum TargetMode { Cycle, Focused, Modifiers }

public enum MuteTrigger { DoublePress, ModifierPress, LongPress }

// Where the overlay sits: a screen edge or the middle; Left/Right show it upright.
public enum OverlayPosition { Bottom, Top, Left, Right, Center }

public record AppEntry(string ProcessName, string DisplayName, Modifier Modifier = Modifier.None);

public class Settings
{
    public TargetMode Mode { get; set; } = TargetMode.Cycle;
    public MuteTrigger MuteTrigger { get; set; } = MuteTrigger.DoublePress;
    public Modifier MuteModifier { get; set; } = Modifier.Ctrl;
    // Cycle/Focused: when set, the knob only controls apps while this key is held; otherwise it's Windows volume.
    public Modifier ActivationModifier { get; set; } = Modifier.None;
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.Bottom;
    // Overlay slider colour as "#RRGGBB"; null follows the Windows accent colour.
    public string? OverlayColor { get; set; }
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

    // The one rule for "same app": process names, ignoring case. Used everywhere apps are matched.
    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // The Add dropdown and the "add this app?" popup each work from a list built earlier,
    // so both add through here to never list an app twice. Adding also un-ignores it.
    public void AddApp(string process, string display)
    {
        if (!Apps.Exists(a => Same(a.ProcessName, process))) Apps.Add(new AppEntry(process, display));
        IgnoredApps.RemoveAll(i => Same(i, process));
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndependentKnob", "settings.json");

    // The app used to be spelled "IndependantKnob". Move that settings folder to the new name once,
    // so renaming the app doesn't lose anyone's settings. Never overwrites an existing new folder.
    public static void MoveFromOldName(string appData)
    {
        string old = Path.Combine(appData, "IndependantKnob"), current = Path.Combine(appData, "IndependentKnob");
        if (!Directory.Exists(old) || Directory.Exists(current)) return;
        try
        {
            Directory.Move(old, current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Old folder stays where it is; the app starts with defaults rather than failing.
        }
    }

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
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
            // Valid JSON can still hold nulls a hand-edit leaves behind ("Apps": null, [null], a missing name).
            settings.Apps = (settings.Apps ?? []).Where(a => a?.ProcessName != null)
                .Select(a => a with { DisplayName = a.DisplayName ?? a.ProcessName }).ToList();
            settings.IgnoredApps = (settings.IgnoredApps ?? []).Where(i => i != null).ToList();
            return settings;
        }
        catch (JsonException)
        {
            // Keep the user's broken file so the next Save doesn't destroy it.
            File.Copy(path, path + ".bak", overwrite: true);
            return new Settings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked or not readable: start with defaults instead of crashing. Can't back it up either.
            // shortcut: a later Save replaces the unreadable file; add a "don't save" flag if locked files show up.
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
