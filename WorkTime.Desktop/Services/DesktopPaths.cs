using System;
using System.IO;

namespace WorkTime.Desktop.Services;

/// <summary>
/// Пути профиля десктоп-клиента. Локальная БД и идентификатор устройства
/// хранятся в каталоге пользователя, а не рядом с exe: это сохраняет
/// офлайн-данные при обновлении приложения и работает одинаково на
/// Windows и Linux (RED OS).
/// </summary>
public static class DesktopPaths
{
    private const string AppFolderName = "WorkTime";

    public static string DataDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local");
            }

            var dir = Path.Combine(root, AppFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "worktime-local.db");

    public static string DeviceIdFile => Path.Combine(DataDirectory, "device.id");

    /// <summary>
    /// Идентификатор устройства постоянен между запусками: офлайн-события,
    /// созданные без сети, позже проходят FK-проверку на сервере.
    /// </summary>
    public static Guid GetOrCreateDeviceId()
    {
        try
        {
            if (File.Exists(DeviceIdFile) &&
                Guid.TryParse(File.ReadAllText(DeviceIdFile).Trim(), out var stored))
            {
                return stored;
            }
        }
        catch
        {
            // повреждённый файл — пересоздадим ниже
        }

        var created = Guid.NewGuid();

        try
        {
            File.WriteAllText(DeviceIdFile, created.ToString());
        }
        catch
        {
            // не удалось сохранить — используем временный Id
        }

        return created;
    }
}
