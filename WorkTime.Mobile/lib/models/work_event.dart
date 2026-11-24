/// Типы рабочих событий. Порядок и имена совпадают с серверным
/// `WorkTime.Server.Models.WorkEventType` и клиентским `WorkTime.Client`.
///
/// В SQLite хранится числом (0..3), в REST API — строкой, потому что
/// сервер конфигурирует `JsonStringEnumConverter` (Program.cs).
enum WorkEventType {
  startWork,
  startBreak,
  endBreak,
  endWork;

  /// Имя, ожидаемое сервером (PascalCase).
  String get wireName => switch (this) {
        WorkEventType.startWork => 'StartWork',
        WorkEventType.startBreak => 'StartBreak',
        WorkEventType.endBreak => 'EndBreak',
        WorkEventType.endWork => 'EndWork',
      };

  /// Подпись для интерфейса.
  String get label => switch (this) {
        WorkEventType.startWork => 'Начало дня',
        WorkEventType.startBreak => 'Начало перерыва',
        WorkEventType.endBreak => 'Конец перерыва',
        WorkEventType.endWork => 'Конец дня',
      };

  int get dbValue => index;

  static WorkEventType fromDbValue(int value) =>
      WorkEventType.values[value.clamp(0, WorkEventType.values.length - 1)];

  /// Разбор строки из ответа сервера: `JsonStringEnumConverter` отдаёт
  /// PascalCase, но сравнение регистронезависимо и терпимо к пробелам,
  /// чтобы смена регистра на сервере не ломала клиента.
  static WorkEventType fromWireName(String? name) {
    final normalized = (name ?? '').trim().toLowerCase();
    return WorkEventType.values.firstWhere(
      (e) => e.wireName.toLowerCase() == normalized,
      orElse: () => WorkEventType.startWork,
    );
  }
}

/// Состояние записи в очереди синхронизации.
enum SyncStatus {
  pending,
  synced;

  int get dbValue => index;
}

/// Одна локальная запись о рабочем событии (FR1–FR5).
///
/// Структура повторяет `WorkTime.Client.Models.LocalWorkEvent`, чтобы
/// мобильный клиент участвовал в том же контракте синхронизации:
///  - `id` — клиентский GUID, обеспечивает идемпотентность (повторная
///    отправка не создаёт дубликат);
///  - `deviceSeq` — монотонный номер события на устройстве, задаёт
///    порядок применения при пакетной отправке;
///  - `projectId` — выбор проекта (FR5), необязательный.
class WorkEvent {
  final String id;
  final String userId;
  final String deviceId;
  final WorkEventType type;

  /// Момент события в UTC.
  final DateTime timestamp;

  /// Момент создания записи на устройстве в UTC.
  final DateTime createdAt;

  /// Момент успешной отправки на сервер, если она состоялась.
  final DateTime? syncedAt;

  final SyncStatus syncStatus;
  final int deviceSeq;
  final String? projectId;

  const WorkEvent({
    required this.id,
    required this.userId,
    required this.deviceId,
    required this.type,
    required this.timestamp,
    required this.createdAt,
    required this.deviceSeq,
    this.syncedAt,
    this.syncStatus = SyncStatus.pending,
    this.projectId,
  });

  WorkEvent copyWith({
    DateTime? syncedAt,
    SyncStatus? syncStatus,
  }) =>
      WorkEvent(
        id: id,
        userId: userId,
        deviceId: deviceId,
        type: type,
        timestamp: timestamp,
        createdAt: createdAt,
        deviceSeq: deviceSeq,
        syncedAt: syncedAt ?? this.syncedAt,
        syncStatus: syncStatus ?? this.syncStatus,
        projectId: projectId,
      );

  /// Тело запроса `POST /api/WorkEvents`.
  ///
  /// Даты отправляются в UTC в формате ISO-8601 с суффиксом Z — так же,
  /// как их сериализует System.Text.Json на десктопе.
  Map<String, dynamic> toJson() => {
        'id': id,
        'userId': userId,
        'deviceId': deviceId,
        'type': type.wireName,
        'timestamp': timestamp.toUtc().toIso8601String(),
        'createdAt': createdAt.toUtc().toIso8601String(),
        'deviceSeq': deviceSeq,
        'projectId': projectId,
      };

  /// Строка БД. Колонки соответствуют `LocalEventStore` в C#: Guid → TEXT,
  /// enum → INTEGER, DateTime → TEXT.
  Map<String, Object?> toDbMap() => {
        'Id': id,
        'UserId': userId,
        'DeviceId': deviceId,
        'Type': type.dbValue,
        'Timestamp': _toDbTimestamp(timestamp),
        'CreatedAt': _toDbTimestamp(createdAt),
        'SyncedAt': syncedAt == null ? null : _toDbTimestamp(syncedAt!),
        'SyncStatus': syncStatus.dbValue,
        'DeviceSeq': deviceSeq,
        'ProjectId': projectId,
      };

  static WorkEvent fromDbMap(Map<String, Object?> m) => WorkEvent(
        id: m['Id']! as String,
        userId: m['UserId']! as String,
        deviceId: m['DeviceId']! as String,
        type: WorkEventType.fromDbValue((m['Type']! as int)),
        timestamp: _parseDbTimestamp(m['Timestamp']! as String),
        createdAt: _parseDbTimestamp(m['CreatedAt']! as String),
        syncedAt: m['SyncedAt'] == null
            ? null
            : _parseDbTimestamp(m['SyncedAt']! as String),
        syncStatus: (m['SyncStatus']! as int) == 1
            ? SyncStatus.synced
            : SyncStatus.pending,
        deviceSeq: (m['DeviceSeq']! as int),
        projectId: m['ProjectId'] as String?,
      );

  // В SQLite дата хранится строкой в UTC. Явно приводим к UTC и убираем
  // локальное смещение, чтобы не зависеть от часового пояса устройства.
  static String _toDbTimestamp(DateTime value) =>
      value.toUtc().toIso8601String();

  static DateTime _parseDbTimestamp(String value) =>
      DateTime.parse(value).toUtc();
}
