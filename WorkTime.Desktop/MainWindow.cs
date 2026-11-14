using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WorkTime.Client.Data;
using WorkTime.Client.Models;
using WorkTime.Client.Services;
using WorkTime.Client.Storage;
using WorkTime.Desktop.Services;

namespace WorkTime.Desktop;

/// <summary>
/// Десктоп-клиент (FR20): те же сценарии, что веб и консоль — крупные кнопки
/// «Начать день / Перерыв / Вернуться / Закончить», локальная SQLite-БД и
/// индикатор очереди синхронизации. Работает офлайн, синхронизируется в фоне.
/// UI собран кодом (без XAML), поэтому проект одинаково собирается на
/// Windows и Linux, включая RED OS.
/// </summary>
public class MainWindow : Window
{
    private const string DefaultServerUrl = "http://localhost:8080";

    // Внимательно с статическими полями здесь: InvariantGlobalization=true
    // запрещает new CultureInfo("ru-RU") — это уронило бы весь класс типа
    // TypeInitializationException при первом обращении к любому статическому члену.
    private static readonly Dictionary<WorkEventType, string> TypeLabels = new()
    {
        { WorkEventType.StartWork, "Начало работы" },
        { WorkEventType.StartBreak, "Перерыв" },
        { WorkEventType.EndBreak, "Возврат с перерыва" },
        { WorkEventType.EndWork, "Конец работы" }
    };

    private readonly LocalDbContext _db = new(DesktopPaths.DatabasePath);
    private readonly LocalEventStore _store;
    private readonly DesktopApiClient _api;
    private readonly DispatcherTimer _ticker;

    private Guid _userId;
    private readonly Guid _deviceId = DesktopPaths.GetOrCreateDeviceId();
    private string _role = "Employee";
    private List<ProjectInfo> _projects = new();

    private TextBox _serverBox = null!;
    private TextBox _emailBox = null!;
    private TextBox _passwordBox = null!;
    private Button _loginButton = null!;
    private TextBlock _loginStatus = null!;

    private Panel _loginPanel = null!;
    private Panel _workPanel = null!;

    private TextBlock _statusText = null!;
    private TextBlock _timerText = null!;
    private TextBlock _userText = null!;
    private TextBlock _syncText = null!;
    private Button _startButton = null!;
    private Button _breakButton = null!;
    private Button _resumeButton = null!;
    private Button _endButton = null!;
    private Button _syncButton = null!;
    private ComboBox _projectBox = null!;
    private ListBox _eventsList = null!;

    public MainWindow()
    {
        _store = new LocalEventStore(_db);
        _api = new DesktopApiClient(DefaultServerUrl);

        Title = "WorkTime — учёт рабочего времени";
        Width = 520;
        Height = 680;
        MinWidth = 420;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Spacing = 0 };
        root.Children.Add(BuildLoginPanel());
        root.Children.Add(BuildWorkPanel());

        Content = new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => RefreshClock();

        Opened += OnWindowOpened;
        Closed += (_, _) =>
        {
            _ticker.Stop();
            _db.Dispose();
        };
    }

    // ============================================================
    // Конструкция интерфейса
    // ============================================================

    private Panel BuildLoginPanel()
    {
        _loginPanel = new StackPanel { Spacing = 10, Margin = new Thickness(24) };

        _loginPanel.Children.Add(new TextBlock
        {
            Text = "WorkTime",
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _loginPanel.Children.Add(new TextBlock
        {
            Text = "Десктоп-клиент · Windows / RED OS",
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _loginPanel.Children.Add(Label("Сервер"));
        _serverBox = new TextBox { Text = DefaultServerUrl };
        _loginPanel.Children.Add(_serverBox);

        _loginPanel.Children.Add(Label("Email"));
        _emailBox = new TextBox { Watermark = "employee@worktime.local" };
        _loginPanel.Children.Add(_emailBox);

        _loginPanel.Children.Add(Label("Пароль"));
        // PasswordBox в Avalonia 11 нет в базовом пакете — маскируем TextBox
        _passwordBox = new TextBox { Watermark = "••••••••", PasswordChar = '•' };
        _loginPanel.Children.Add(_passwordBox);

        _loginButton = new Button
        {
            Content = "Войти",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8),
            FontSize = 15
        };
        _loginButton.Click += OnLoginClick;
        _loginPanel.Children.Add(_loginButton);

        _loginStatus = new TextBlock
        {
            Text = "Демо: employee@worktime.local / Employee@123",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        };
        _loginPanel.Children.Add(_loginStatus);

        return _loginPanel;
    }

    private Panel BuildWorkPanel()
    {
        _workPanel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(20),
            IsVisible = false
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _userText = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold };
        var logout = new Button { Content = "Выйти" };
        logout.Click += OnLogoutClick;
        Grid.SetColumn(_userText, 0);
        Grid.SetColumn(logout, 1);
        header.Children.Add(_userText);
        header.Children.Add(logout);
        _workPanel.Children.Add(header);

        _statusText = new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _workPanel.Children.Add(_statusText);

        _timerText = new TextBlock
        {
            FontSize = 34,
            FontFamily = new FontFamily("Consolas,monospace"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _workPanel.Children.Add(_timerText);

        _workPanel.Children.Add(Label("Проект (опционально)"));
        _projectBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _workPanel.Children.Add(_projectBox);

        var buttons = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,*")
        };

        _startButton = ActionButton("▶ Начать день", Brushes.SeaGreen);
        _startButton.Click += (_, _) => OnAction(WorkEventType.StartWork);

        _breakButton = ActionButton("⏸ Перерыв", Brushes.DarkOrange);
        _breakButton.Click += (_, _) => OnAction(WorkEventType.StartBreak);

        _resumeButton = ActionButton("▶ Вернуться", Brushes.SteelBlue);
        _resumeButton.Click += (_, _) => OnAction(WorkEventType.EndBreak);

        _endButton = ActionButton("■ Закончить день", Brushes.Firebrick);
        _endButton.Click += (_, _) => OnAction(WorkEventType.EndWork);

        Grid.SetRow(_startButton, 0); Grid.SetColumn(_startButton, 0);
        Grid.SetRow(_breakButton, 0); Grid.SetColumn(_breakButton, 1);
        Grid.SetRow(_resumeButton, 1); Grid.SetColumn(_resumeButton, 0);
        Grid.SetRow(_endButton, 1); Grid.SetColumn(_endButton, 1);

        foreach (var b in new[] { _startButton, _breakButton, _resumeButton, _endButton })
        {
            b.Margin = new Thickness(4);
            buttons.Children.Add(b);
        }

        _workPanel.Children.Add(buttons);

        var syncRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _syncText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _syncButton = new Button { Content = "Синхронизировать" };
        _syncButton.Click += OnSyncClick;
        Grid.SetColumn(_syncText, 0);
        Grid.SetColumn(_syncButton, 1);
        syncRow.Children.Add(_syncText);
        syncRow.Children.Add(_syncButton);
        _workPanel.Children.Add(syncRow);

        _workPanel.Children.Add(new Separator());
        _workPanel.Children.Add(Label("Локальные записи"));
        _eventsList = new ListBox { MaxHeight = 260 };
        _workPanel.Children.Add(_eventsList);

        return _workPanel;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.DimGray
    };

    private static Button ActionButton(string text, IBrush background) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(10, 14),
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.White,
        Background = background
    };

    // ============================================================
    // Жизненный цикл
    // ============================================================

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            await _store.InitializeAsync();
        }
        catch (Exception ex)
        {
            _loginStatus.Text = "Не удалось открыть локальную базу: " + ex.Message;
        }
    }

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        var url = _serverBox.Text?.Trim();
        var email = _emailBox.Text?.Trim();
        var password = _passwordBox.Text;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _loginStatus.Text = "Введите email и пароль.";
            return;
        }

        _loginButton.IsEnabled = false;
        _loginStatus.Text = "Вход...";
        _loginStatus.Foreground = Brushes.Gray;

        try
        {
            if (!string.Equals(url, _api.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                _api.Rebase(url ?? DefaultServerUrl);
            }

            var auth = await _api.LoginAsync(email, password);

            if (auth is null)
            {
                _loginStatus.Text = "Неверные учётные данные или сервер недоступен.";
                _loginStatus.Foreground = Brushes.Firebrick;
                return;
            }

            _userId = DesktopApiClient.DecodeUserId(auth.Token);
            _role = auth.Role;

            if (_userId == Guid.Empty)
            {
                _loginStatus.Text = "Сервер вернул токен без идентификатора пользователя.";
                _loginStatus.Foreground = Brushes.Firebrick;
                return;
            }

            // Устройство нужно серверу для FK-проверки событий; при офлайне
            // регистрация отложится до первой успешной синхронизации.
            await _api.RegisterDeviceAsync(
                _deviceId,
                _userId,
                $"{Environment.MachineName} (Desktop)",
                OperatingSystem.IsWindows() ? "Windows" : "Linux");

            await EnterWorkspaceAsync(email);
        }
        catch (Exception ex)
        {
            _loginStatus.Text = "Ошибка сети: " + ex.Message;
            _loginStatus.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _loginButton.IsEnabled = true;
        }
    }

    private async Task EnterWorkspaceAsync(string email)
    {
        _loginPanel.IsVisible = false;
        _workPanel.IsVisible = true;
        _userText.Text = $"{email} · {_role}";

        _projects = await _api.GetProjectsAsync();
        RebuildProjectBox();

        await RefreshAsync();
        _ticker.Start();
    }

    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        _ticker.Stop();
        _api.SetToken(null);
        _workPanel.IsVisible = false;
        _loginPanel.IsVisible = true;
        _loginStatus.Text = "Вы вышли. Локальные записи сохранены и синхронизируются при следующем входе.";
        _loginStatus.Foreground = Brushes.Gray;
    }

    // ============================================================
    // Данные и статус
    // ============================================================

    private void RebuildProjectBox()
    {
        _projectBox.Items.Clear();
        _projectBox.Items.Add(new ComboBoxItem { Content = "Без проекта", Tag = (Guid?)null });

        foreach (var p in _projects)
        {
            _projectBox.Items.Add(new ComboBoxItem { Content = p.Name, Tag = (Guid?)p.Id });
        }

        _projectBox.SelectedIndex = 0;
    }

    private Guid? SelectedProjectId()
    {
        // Tag хранит (Guid?) — проверяем через object-паттерн, чтобы не
        // требовать не-nullable Guid в шаблоне.
        if (_projectBox.SelectedItem is ComboBoxItem item &&
            item.Tag is Guid id)
        {
            return id;
        }

        return null;
    }

    private async Task RefreshAsync()
    {
        var events = await _store.GetAllAsync();
        var pending = events.Count(x => x.SyncStatus == SyncStatus.Pending);

        _eventsList.ItemsSource = events
            .OrderByDescending(x => x.Timestamp)
            .Take(50)
            .Select(x => $"{x.Timestamp.ToLocalTime():dd.MM.yyyy HH:mm}  " +
                        $"{Label(x.Type)}  " +
                        $"{ProjectName(x.ProjectId)}  " +
                        (x.SyncStatus == SyncStatus.Pending ? "○ офлайн" : "✓ сервер"))
            .ToList();

        var key = StatusKey(events);
        _startButton.IsEnabled = key is "idle" or "done";
        _breakButton.IsEnabled = key == "working";
        _resumeButton.IsEnabled = key == "break";
        _endButton.IsEnabled = key is "working" or "break";
        _projectBox.IsEnabled = key is "idle" or "done";

        _syncText.Text = pending > 0
            ? $"{pending} записей ждут синхронизации"
            : "Всё синхронизировано";
        _syncText.Foreground = pending > 0 ? Brushes.DarkOrange : Brushes.SeaGreen;

        (_statusText.Text, _statusText.Foreground, _timerText.IsVisible) = key switch
        {
            "working" => ("Работаю", Brushes.SeaGreen, true),
            "break" => ("Перерыв", Brushes.DarkOrange, false),
            "done" => ("День завершён", Brushes.SteelBlue, false),
            _ => ("День не начат", Brushes.Gray, false)
        };

        if (key == "working")
        {
            _currentStart = LastOpenStart(events)?.Timestamp;
        }
        else
        {
            _currentStart = null;
            _timerText.Text = string.Empty;
        }
    }

    private DateTime? _currentStart;

    private void RefreshClock()
    {
        if (_currentStart is not { } start)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - start;
        _timerText.Text = elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string Label(WorkEventType type) =>
        TypeLabels.TryGetValue(type, out var label) ? label : type.ToString();

    private string ProjectName(Guid? projectId)
    {
        if (projectId is not { } id)
        {
            return "—";
        }

        return _projects.Find(p => p.Id == id)?.Name ?? "—";
    }

    /// <summary>
    /// Ключ статуса по последнему событию дня. Совпадает с логикой веб-клиента,
    /// чтобы на всех платформах сотрудник видел одно и то же состояние.
    /// </summary>
    private static string StatusKey(List<LocalWorkEvent> events)
    {
        var today = DateTime.UtcNow.Date;
        var todays = events
            .Where(x => x.Timestamp.Date == today)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (todays.Count == 0)
        {
            return "idle";
        }

        return todays[^1].Type switch
        {
            WorkEventType.StartWork => "working",
            WorkEventType.EndBreak => "working",
            WorkEventType.StartBreak => "break",
            WorkEventType.EndWork => "done",
            _ => "idle"
        };
    }

    /// <summary>Старт, после которого не было окончания дня — точка отсчёта таймера.</summary>
    private static LocalWorkEvent? LastOpenStart(List<LocalWorkEvent> events)
    {
        var todays = events
            .Where(x => x.Timestamp.Date == DateTime.UtcNow.Date)
            .OrderByDescending(x => x.Timestamp)
            .ToList();

        foreach (var ev in todays)
        {
            if (ev.Type == WorkEventType.StartWork)
            {
                return ev;
            }

            if (ev.Type == WorkEventType.EndWork)
            {
                return null;
            }
        }

        return null;
    }

    // ============================================================
    // Действия сотрудника (FR1–FR5, FR9)
    // ============================================================

    private async void OnAction(WorkEventType type)
    {
        if (_userId == Guid.Empty)
        {
            return;
        }

        SetBusy(true);

        try
        {
            var now = DateTime.UtcNow;

            await _store.AddAsync(new LocalWorkEvent
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                DeviceId = _deviceId,
                Type = type,
                Timestamp = now,
                CreatedAt = now,
                SyncStatus = SyncStatus.Pending,
                DeviceSeq = await _store.NextDeviceSeqAsync(),
                ProjectId = SelectedProjectId()
            });

            // Запись уже видна пользователю локально; отправка на сервер — в фоне.
            await RefreshAsync();
            await TrySyncAsync(auto: true);
        }
        catch (Exception ex)
        {
            _syncText.Text = "Ошибка сохранения: " + ex.Message;
            _syncText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSyncClick(object? sender, RoutedEventArgs e)
    {
        SetBusy(true);

        try
        {
            await TrySyncAsync(auto: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TrySyncAsync(bool auto)
    {
        if (_userId == Guid.Empty || string.IsNullOrEmpty(_api.Token))
        {
            return;
        }

        try
        {
            // SyncService пишет прогресс в Console — вне терминала это безопасно.
            await _api.SyncPendingEventsAsync(_store);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            // Офлайн — штатное состояние: данные остаются в локальной очереди.
            _syncText.Text = auto
                ? _syncText.Text
                : "Нет связи с сервером — записи сохранены локально. " + ex.Message;
            _syncText.Foreground = Brushes.DarkOrange;
        }
    }

    private void SetBusy(bool busy)
    {
        _startButton.IsEnabled = !busy;
        _breakButton.IsEnabled = !busy;
        _resumeButton.IsEnabled = !busy;
        _endButton.IsEnabled = !busy;
        _syncButton.IsEnabled = !busy;
    }
}
