using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace IndependantKnob.Windows;

// Takes the first icon out of the app's .exe.
[SupportedOSPlatform("windows")]
public sealed class WindowsIcons : IAppIcons
{
    // Misses are cached too, so an app without a readable icon isn't retried on every knob turn.
    // shortcut: cached until restart, so an icon missed while the app was starting stays missing; add expiry if that shows up.
    readonly Dictionary<string, IImage?> cache = new(StringComparer.OrdinalIgnoreCase);

    public IImage? TryGet(string app)
    {
        if (!cache.TryGetValue(app, out var icon)) cache[app] = icon = Load(app);
        return icon;
    }

    static IImage? Load(string app)
    {
        foreach (var process in Process.GetProcessesByName(app))
        {
            using (process)
            {
                try
                {
                    // MainModule throws for processes we can't read (elevated, or exiting); try the next one.
                    if (process.MainModule?.FileName is not { } path) continue;
                    using var icon = DrawingIcon.ExtractIcon(path, 0, 64);
                    if (icon == null) continue;
                    using var bitmap = icon.ToBitmap();
                    using var png = new MemoryStream();
                    bitmap.Save(png, ImageFormat.Png);
                    png.Position = 0;
                    return new Bitmap(png);
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException or IOException)
                {
                }
            }
        }
        return null;
    }
}
