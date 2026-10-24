using Avalonia;
using System;
using System.Threading;

namespace IndependantKnob;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    // Set in the running copy; App opens the settings window whenever it is signalled.
    internal static EventWaitHandle? OpenRequest;

    [STAThread]
    public static void Main(string[] args)
    {
        // Created (or opened, if it exists) before the mutex check, so a second copy can't signal before it exists.
        var openRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\IndependantKnob.Open");
        // One copy per user session: a second keyboard hook would take the knob from the first.
        using var mutex = new Mutex(true, @"Local\IndependantKnob", out bool first);
        if (!first)
        {
            openRequest.Set(); // ask the running copy to show its window instead
            return;
        }

        OpenRequest = openRequest;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
