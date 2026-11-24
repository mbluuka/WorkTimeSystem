import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';
import 'package:uuid/uuid.dart';

import '../models/work_event.dart';

/// Локальное хранилище рабочих событий (FR9).
///
/// Схема повторяет `WorkTime.Client.Data.LocalDbContext`, поэтому запись
/// выглядит одинаково на десктопе, консоли и мобильном клиенте, а серверный
/// bulk-upsert принимает её без изменений.
///
/// Хранение:
///  - Guid → TEXT
///  - enum → INTEGER
///  - DateTime → TEXT в UTC (ISO-8601)
class LocalEventStore {
  static const _dbName = 'worktime-local.db';
  static const _dbVersion = 2;

  Database? _db;

  Future<Database> get database async {
    if (_db != null) return _db!;
    final dir = await getDatabasesPath();
    final path = p.join(dir, _dbName);

    _db = await openDatabase(
      path,
      version: _dbVersion,
      onCreate: _onCreate,
      onUpgrade: _onUpgrade,
    );
    return _db!;
  }

  Future<void> _onCreate(Database db, int version) async {
    await db.execute('''
      CREATE TABLE WorkEvents (
        Id TEXT PRIMARY KEY,
        UserId TEXT NOT NULL,
        DeviceId TEXT NOT NULL,
        Type INTEGER NOT NULL,
        Timestamp TEXT NOT NULL,
        CreatedAt TEXT NOT NULL,
        SyncedAt TEXT,
        SyncStatus INTEGER NOT NULL DEFAULT 0,
        DeviceSeq INTEGER NOT NULL DEFAULT 0,
        ProjectId TEXT
      )
    ''');

    // Очередь выбирается по статусу в хронологическом порядке.
    await db.execute(
        'CREATE INDEX IX_WorkEvents_SyncStatus ON WorkEvents (SyncStatus)');
    await db.execute(
        'CREATE INDEX IX_WorkEvents_Timestamp ON WorkEvents (Timestamp)');
  }

  /// Миграция версии 1 (без DeviceSeq/ProjectId) → 2.
  ///
  /// Соответствует LocalEventStore.EnsureColumnAsync в C#: колонки добавляются,
  /// если их нет, а существующие данные не трогаются.
  Future<void> _onUpgrade(Database db, int oldVersion, int newVersion) async {
    if (oldVersion < 2) {
      final cols = await db.rawQuery('PRAGMA table_info(WorkEvents)');
      final names = cols.map((c) => c['name'] as String).toSet();
      if (!names.contains('DeviceSeq')) {
        await db.execute(
            'ALTER TABLE WorkEvents ADD COLUMN DeviceSeq INTEGER NOT NULL DEFAULT 0');
      }
      if (!names.contains('ProjectId')) {
        await db.execute('ALTER TABLE WorkEvents ADD COLUMN ProjectId TEXT');
      }
    }
  }

  /// Следующий монотонный номер события на устройстве.
  ///
  /// Нужен, чтобы сервер применил события в том же порядке, в котором они
  /// были созданы офлайн (см. ServerOrder на сервере).
  Future<int> nextDeviceSeq() async {
    final db = await database;
    final rows = await db.rawQuery('SELECT MAX(DeviceSeq) AS m FROM WorkEvents');
    final max = (rows.first['m'] as int?) ?? 0;
    return max + 1;
  }

  /// Добавляет событие. Повторная вставка того же Id молча игнорируется —
  /// это клиентская часть идемпотентности.
  Future<void> add(WorkEvent event) async {
    final db = await database;
    await db.insert('WorkEvents', event.toDbMap(),
        conflictAlgorithm: ConflictAlgorithm.ignore);
  }

  /// Вставляет запись, возвращая true, если строка реально добавлена.
  ///
  /// Нужно для pull-синхронизации: чтобы посчитать количество новых
  /// записей и не перезаписать локальную версию с её deviceSeq.
  Future<bool> insertIfAbsent(WorkEvent event) async {
    final db = await database;
    final rows = await db.insert('WorkEvents', event.toDbMap(),
        conflictAlgorithm: ConflictAlgorithm.ignore);
    return rows > 0;
  }

  /// Создаёт событие указанного типа «сейчас» и возвращает его.
  Future<WorkEvent> record(
      {required String userId,
      required String deviceId,
      required WorkEventType type,
      String? projectId}) async {
    final now = DateTime.now().toUtc();
    final seq = await nextDeviceSeq();
    final event = WorkEvent(
      id: _newGuid(),
      userId: userId,
      deviceId: deviceId,
      type: type,
      timestamp: now,
      createdAt: now,
      deviceSeq: seq,
      projectId: projectId,
    );
    await add(event);
    return event;
  }

  /// Неотправленные записи в порядке их создания на устройстве.
  Future<List<WorkEvent>> pending() async {
    final db = await database;
    final rows = await db.query('WorkEvents',
        where: 'SyncStatus = ?',
        whereArgs: [SyncStatus.pending.dbValue],
        orderBy: 'Timestamp ASC');
    return rows.map(WorkEvent.fromDbMap).toList();
  }

  /// Все записи: история своих временных записей (FR6).
  Future<List<WorkEvent>> all({int limit = 200}) async {
    final db = await database;
    final rows = await db.query('WorkEvents',
        orderBy: 'Timestamp DESC', limit: limit);
    return rows.map(WorkEvent.fromDbMap).toList();
  }

  Future<int> countPending() async {
    final db = await database;
    final rows = await db.rawQuery(
        'SELECT COUNT(*) AS c FROM WorkEvents WHERE SyncStatus = ?',
        [SyncStatus.pending.dbValue]);
    return (rows.first['c'] as int?) ?? 0;
  }

  Future<void> markSynced(String id, DateTime syncedAt) async {
    final db = await database;
    await db.update('WorkEvents', {
      'SyncStatus': SyncStatus.synced.dbValue,
      'SyncedAt': syncedAt.toUtc().toIso8601String(),
    }, where: 'Id = ?', whereArgs: [id]);
  }

  /// Удаляет локальную запись (FR7: удаление записи).
  Future<void> delete(String id) async {
    final db = await database;
    await db.delete('WorkEvents', where: 'Id = ?', whereArgs: [id]);
  }

  /// Меняет метку времени события (FR7: ручная корректировка).
  ///
  /// Запись снова помечается как Pending: сервер должен принять правку,
  /// а ограничение по прошлому периоду (окно 7 дней) проверит сервер,
  /// а не клиент.
  Future<void> updateTimestamp(String id, DateTime newTimestamp) async {
    final db = await database;
    await db.update('WorkEvents', {
      'Timestamp': newTimestamp.toUtc().toIso8601String(),
      'SyncStatus': SyncStatus.pending.dbValue,
      'SyncedAt': null,
    }, where: 'Id = ?', whereArgs: [id]);
  }

  /// Сводка за период: рабочие минуты, минуты перерыва, количество дней (FR8).
  ///
  /// Считается по парам StartWork→EndWork и StartBreak→EndBreak в
  /// хронологическом порядке. Непарные события (например, начало дня без
  /// конца) в сумму не входят, но день при этом засчитывается.
  Future<WorkSummary> summarize(DateTime from, DateTime to) async {
    final db = await database;
    final rows = await db.query('WorkEvents',
        where: 'Timestamp >= ? AND Timestamp <= ?',
        whereArgs: [
          from.toUtc().toIso8601String(),
          to.toUtc().toIso8601String(),
        ],
        orderBy: 'Timestamp ASC');

    final events = rows.map(WorkEvent.fromDbMap).toList();

    int workMinutes = 0;
    int breakMinutes = 0;
    DateTime? workStart;
    DateTime? breakStart;
    final days = <String>{};

    for (final e in events) {
      days.add(e.timestamp.toLocal().toIso8601String().substring(0, 10));
      switch (e.type) {
        case WorkEventType.startWork:
          workStart = e.timestamp;
        case WorkEventType.endWork:
          if (workStart != null) {
            workMinutes += e.timestamp.difference(workStart).inMinutes;
            workStart = null;
          }
        case WorkEventType.startBreak:
          breakStart = e.timestamp;
        case WorkEventType.endBreak:
          if (breakStart != null) {
            breakMinutes += e.timestamp.difference(breakStart).inMinutes;
            breakStart = null;
          }
      }
    }

    return WorkSummary(
      workMinutes: workMinutes,
      breakMinutes: breakMinutes,
      days: days.length,
      remainingMinutes: (8 * 60 - workMinutes).clamp(0, 8 * 60),
    );
  }

  Future<void> close() async {
    await _db?.close();
    _db = null;
  }

  /// 8-4-4-4-12 через пакет uuid (криптостойкий CSPRNG). Формат принимает
  /// `Guid.TryParse` на сервере и та же схема, что в SQLite-клиентах на C#.
  static String _newGuid() => const Uuid().v4();
}

/// Результат сводки за период (FR8).
class WorkSummary {
  final int workMinutes;
  final int breakMinutes;
  final int days;
  final int remainingMinutes;

  const WorkSummary({
    required this.workMinutes,
    required this.breakMinutes,
    required this.days,
    required this.remainingMinutes,
  });

  String get workLabel {
    final h = workMinutes ~/ 60;
    final m = workMinutes % 60;
    return h > 0 ? '$h ч $m мин' : '$m мин';
  }

  String get breakLabel => '$breakMinutes мин';
  String get remainingLabel => '$remainingMinutes мин';
}
