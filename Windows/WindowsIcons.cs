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

namespace IndependentKnob.Windows;

// Takes the first icon out of the app's .exe.
[SupportedOSPlatform("windows")]
public sealed class WindowsIcons : IAppIcons
{
    // Cached until restart. A miss is cached only when the app is running but has no readable icon, so an app
    // that isn't running yet (e.g. listed in settings) gets its icon once it starts.
    // shortcut: an icon missed while the app was still starting stays missing; add expiry if that shows up.
    readonly Dictionary<string, IImage?> cache = new(StringComparer.OrdinalIgnoreCase);

    public IImage? TryGet(string app)
    {
        if (cache.TryGetValue(app, out var icon)) return icon;
        var processes = Process.GetProcessesByName(app);
        try
        {
            icon = Load(processes);
            if (icon != null || processes.Length > 0) cache[app] = icon;
            return icon;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    static IImage? Load(Process[] processes)
    {
        foreach (var process in processes)
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
        return null;
    }
}
