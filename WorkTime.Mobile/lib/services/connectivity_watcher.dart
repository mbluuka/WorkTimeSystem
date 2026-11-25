import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';

import 'sync_service.dart';

/// Источник сведений о сети.
///
/// Выделен отдельным интерфейсом, потому что `Connectivity` из
/// `connectivity_plus` — синглтон с приватным конструктором: подменить его
/// в тестах нельзя. Здесь же достаточно двух методов, а платформенная
/// реализация — в [PlatformConnectivityProbe].
abstract class ConnectivityProbe {
  /// Текущее состояние сети.
  Future<List<ConnectivityResult>> check();

  /// Поток изменений состояния сети.
  Stream<List<ConnectivityResult>> get changes;
}

/// Реализация через `connectivity_plus`.
class PlatformConnectivityProbe implements ConnectivityProbe {
  final Connectivity _connectivity = Connectivity();

  @override
  Future<List<ConnectivityResult>> check() => _connectivity.checkConnectivity();

  @override
  Stream<List<ConnectivityResult>> get changes =>
      _connectivity.onConnectivityChanged;
}

/// Фоновая синхронизация при восстановлении связи (FR10).
///
/// Требование: очередь должна уйти на сервер сама, без действий
/// пользователя. В мобильном клиенте для этого два независимых сигнала:
///
///  1. изменения состояния сети — система сообщает, что сеть появилась
///     (Wi-Fi, мобильные данные). Это основной сценарий: пользователь
///     вышел из метро — очередь уходит сразу;
///  2. периодический таймер — страховка на случай, когда сеть формально
///     есть, но сервер был недоступен (перезапуск контейнера, VPN,
///     captive portal). Без него запись ждала бы следующего действия.
///
/// Оба сигнала проходят через [SyncService.syncNow], а он защищён от
/// параллельных запусков, поэтому повторные вызовы безопасны.
class ConnectivityWatcher {
  final SyncService sync;
  final ConnectivityProbe _probe;

  Timer? _timer;
  StreamSubscription<List<ConnectivityResult>>? _subscription;

  /// Признак того, что на прошлой проверке сети не было. Нужен, чтобы
  /// синхронизировать именно на переходе «офлайн → онлайн», а не на каждом
  /// событии платформы (их бывает несколько подряд).
  bool _wasOffline = false;

  /// Периодическая страховочная попытка.
  static const _interval = Duration(seconds: 30);

  /// Вызывается после каждой фоновой синхронизации — UI обновляет историю
  /// и индикатор очереди.
  void Function(SyncOutcome outcome)? onSynced;

  ConnectivityWatcher(this.sync, {ConnectivityProbe? probe})
      : _probe = probe ?? PlatformConnectivityProbe();

  bool get isRunning => _timer != null;

  /// Подписывается на события сети и запускает таймер.
  ///
  /// Вызывается после входа, когда появился токен: до авторизации
  /// синхронизация всё равно невозможна.
  Future<void> start() async {
    if (isRunning) return;

    final initial = await _probe.check();
    _wasOffline = _isOffline(initial);

    _subscription = _probe.changes.listen((results) async {
      final offline = _isOffline(results);

      if (offline) {
        _wasOffline = true;
        return;
      }

      // Связь восстановилась — это и есть сигнал из требования FR10.
      if (_wasOffline) {
        _wasOffline = false;
        await _syncNow();
      }
    });

    _timer = Timer.periodic(_interval, (_) async {
      final results = await _probe.check();
      if (_isOffline(results)) {
        _wasOffline = true;
        return;
      }
      await _syncNow();
    });
  }

  Future<void> _syncNow() async {
    final outcome = await sync.syncNow();
    onSynced?.call(outcome);
  }

  Future<void> stop() async {
    _timer?.cancel();
    _timer = null;
    await _subscription?.cancel();
    _subscription = null;
  }

  /// `connectivity_plus` 7.x возвращает список: пустой список и `none`
  /// означают отсутствие сети.
  static bool _isOffline(List<ConnectivityResult> results) {
    if (results.isEmpty) return true;
    return results.every((r) => r == ConnectivityResult.none);
  }
}
