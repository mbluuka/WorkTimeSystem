using System;
using Avalonia;

namespace WorkTime.Desktop;

internal static class Program
{
    // Avalonia-приложение: на Windows — WinExe, на Linux (в т.ч. RED OS) — обычный GUI-процесс.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
