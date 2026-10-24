using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndependantKnob;

public enum TargetMode { Cycle, Focused, Modifiers }

public enum MuteTrigger { DoublePress, ModifierPress }

public record AppEntry(string ProcessName, string DisplayName, Modifier Modifier = Modifier.None);

public class Settings
{
    public TargetMode Mode { get; set; } = TargetMode.Cycle;
    public MuteTrigger MuteTrigger { get; set; } = MuteTrigger.DoublePress;
    public Modifier MuteModifier { get; set; } = Modifier.Ctrl;
    public List<AppEntry> Apps { get; set; } = [];
    public string? DefaultApp { get; set; }

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
}
