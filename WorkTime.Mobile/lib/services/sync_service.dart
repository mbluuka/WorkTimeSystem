import '../models/work_event.dart';
import '../storage/local_event_store.dart';
import 'api_client.dart';
import 'device_identity.dart';

/// Результат одной попытки синхронизации.
class SyncOutcome {
  final int pushed;
  final int pulled;
  final int failed;
  final String? error;

  const SyncOutcome({
    this.pushed = 0,
    this.pulled = 0,
    this.failed = 0,
    this.error,
  });

  bool get ok => error == null;

  String get summary {
    if (error != null) return error!;
    if (pushed == 0 && pulled == 0) return 'Синхронизировано, изменений нет';
    final parts = <String>[
      if (pushed > 0) 'отправлено: $pushed',
      if (pulled > 0) 'получено: $pulled',
      if (failed > 0) 'ошибок: $failed',
    ];
    return parts.join(', ');
  }
}

/// Движок синхронизации (FR10).
///
/// Логика повторяет `DesktopApiClient.SyncPendingEventsAsync`:
///  1. выгружаем локальную очередь Pending в порядке создания на устройстве;
///  2. при ответе «Device does not exist» регистрируем устройство и
///     повторяем отправку один раз — это самолечение после входа в офлайне;
///  3. после успешной выгрузки подтягиваем события других устройств того
///     же пользователя, чтобы история была полной.
///
/// Очередь остаётся Pending, если отправка не удалась: данные не теряются
/// и уйдут при следующей попытке.
class SyncService {
  final ApiClient api;
  final LocalEventStore store;
  final DeviceIdentity identity;

  const SyncService(this.api, this.store, this.identity);

  Future<SyncOutcome> syncNow() async {
    if (!api.hasToken) {
      return const SyncOutcome(error: 'Нет активной сессии');
    }

    var pushed = 0;
    var failed = 0;
    String? firstError;

    final pending = await store.pending();
    for (final event in pending) {
      try {
        await api.postWorkEvent(event);
        await store.markSynced(event.id, DateTime.now().toUtc());
        pushed++;
      } on ApiException catch (e) {
        // Самолечение: вход был выполнен без сети, поэтому устройство не
        // зарегистрировалось, а сервер отклоняет события по FK на DeviceId.
        if (e.statusCode == 400 &&
            e.message.toLowerCase().contains('device does not exist')) {
          try {
            await api.registerDevice(
              deviceId: event.deviceId,
              userId: event.userId,
              name: identity.deviceName(),
              platform: identity.platform(),
            );
            await api.postWorkEvent(event);
            await store.markSynced(event.id, DateTime.now().toUtc());
            pushed++;
            continue;
          } on ApiException catch (e2) {
            firstError ??= e2.message;
            failed++;
            continue;
          }
        }
        // Прочие ошибки (401, 403, 5xx): запись остаётся Pending.
        firstError ??= e.message;
        failed++;
      }
    }

    var pulled = 0;
    try {
      pulled = await _pull();
    } on ApiException {
      // Pull не критичен: локальные данные уже сохранены и отправлены.
    }

    return SyncOutcome(
      pushed: pushed,
      pulled: pulled,
      failed: failed,
      error: firstError,
    );
  }

  /// Забирает с сервера события, созданные после последней выгрузки.
  ///
  /// Это закрывает разрыв local-first между устройствами: событие,
  /// поставленное с десктопа, появляется в мобильной истории.
  /// Собственные неотправленные записи не перезаписываются — вставка
  /// идемпотентна по Id.
  Future<int> _pull() async {
    final userId = identity.userId();
    if (userId == null || userId.isEmpty) return 0;

    final since = identity.lastSyncAt();
    final remote = await api.syncEvents(userId: userId, since: since);

    var added = 0;
    for (final json in remote) {
      final id = json['id'] as String?;
      if (id == null) continue;

      final event = WorkEvent(
        id: id,
        userId: json['userId'] as String,
        deviceId: json['deviceId'] as String,
        type: WorkEventType.fromWireName(json['type'] as String?),
        timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
        createdAt: DateTime.parse(json['createdAt'] as String).toUtc(),
        syncedAt: json['syncedAt'] == null
            ? null
            : DateTime.parse(json['syncedAt'] as String).toUtc(),
        syncStatus: SyncStatus.synced,
        deviceSeq: 0, // принадлежит другому устройству, наш счётчик не трогаем
        projectId: json['projectId'] as String?,
      );

      // Вставка игнорирует дубликат по Id, поэтому считаем только реально
      // добавленные строки.
      if (await store.insertIfAbsent(event)) added++;
    }

    await identity.saveLastSync(DateTime.now().toUtc());
    return added;
  }

  /// Регистрация устройства при первом входе онлайн.
  Future<void> ensureDeviceRegistered() async {
    final userId = identity.userId();
    if (userId == null || userId.isEmpty) return;

    await api.registerDevice(
      deviceId: identity.deviceId(),
      userId: userId,
      name: identity.deviceName(),
      platform: identity.platform(),
    );
  }
}
