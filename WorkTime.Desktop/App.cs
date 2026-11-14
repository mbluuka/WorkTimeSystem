using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace WorkTime.Desktop;

public class App : Application
{
    public override void Initialize()
    {
        // Тема подключается кодом (без XAML), чтобы проект одинаково
        // собирался на Windows и Linux/RED OS без генераторов имён.
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
