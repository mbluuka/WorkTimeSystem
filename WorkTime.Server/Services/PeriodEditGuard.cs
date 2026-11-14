namespace WorkTime.Server.Services;

/// <summary>
/// FR7: ручная корректировка записей с ограничением на прошлые периоды.
/// Employee и Manager могут править только записи в пределах окна
/// редактирования; Admin правит любые периоды.
/// Ограничение действует на явные ручные операции (PUT/DELETE),
/// но не на синхронизацию офлайн-данных, иначе потерялись бы
/// события, созданные без сети.
/// </summary>
public class PeriodEditGuard
{
    private const int DefaultWindowDays = 7;

    private readonly int _windowDays;

    public PeriodEditGuard(IConfiguration configuration)
    {
        _windowDays = configuration.GetValue<int?>("TimeEditing:EmployeeEditWindowDays")
                      ?? DefaultWindowDays;

        if (_windowDays <= 0)
        {
            _windowDays = DefaultWindowDays;
        }
    }

    public int WindowDays => _windowDays;

    /// <summary>
    /// Возвращает текст ошибки, если роль не может изменить запись
    /// с указанной меткой времени, либо null, когда правка разрешена.
    /// </summary>
    public string? CheckEdit(string role, DateTime timestampUtc)
    {
        if (role == "Admin")
        {
            return null;
        }

        var cutoff = DateTime.UtcNow.AddDays(-_windowDays);

        if (EnsureUtc(timestampUtc) >= cutoff)
        {
            return null;
        }

        return
            $"Запись старше {_windowDays} дн. нельзя изменить или удалить. " +
            "Обратитесь к администратору.";
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
