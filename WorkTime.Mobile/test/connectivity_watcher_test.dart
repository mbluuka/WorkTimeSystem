import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:work_time_mobile/services/api_client.dart';
import 'package:work_time_mobile/services/connectivity_watcher.dart';
import 'package:work_time_mobile/services/device_identity.dart';
import 'package:work_time_mobile/services/sync_service.dart';
import 'package:work_time_mobile/storage/local_event_store.dart';

/// Тесты фоновой синхронизации (FR10).
///
/// Плагин connectivity_plus в тестовой среде недоступен, поэтому
/// используется подставной [ConnectivityProbe]; сам SyncService тоже
/// подменён — проверяется именно логика «когда запускать синхронизацию»,
/// а не сетевой обмен.
class _FakeProbe implements ConnectivityProbe {
  final _controller = StreamController<List<ConnectivityResult>>.broadcast();

  List<ConnectivityResult> current = [ConnectivityResult.wifi];

  @override
  Future<List<ConnectivityResult>> check() async => current;

  @override
  Stream<List<ConnectivityResult>> get changes => _controller.stream;

  void emit(List<ConnectivityResult> results) {
    current = results;
    _controller.add(results);
  }

  Future<void> dispose() => _controller.close();
}

class _FakeSync extends SyncService {
  _FakeSync()
      : super(
          ApiClient('http://localhost:1'),
          LocalEventStore(),
          DeviceIdentity(),
        );

  int calls = 0;

  @override
  Future<SyncOutcome> syncNow() async {
    calls++;
    return const SyncOutcome(pushed: 1);
  }
}

void main() {
  group('ConnectivityWatcher (FR10)', () {
    late _FakeProbe probe;
    late _FakeSync sync;
    late ConnectivityWatcher watcher;

    setUp(() {
      probe = _FakeProbe();
      sync = _FakeSync();
      watcher = ConnectivityWatcher(sync, probe: probe);
    });

    tearDown(() async {
      await watcher.stop();
      await probe.dispose();
    });

    test('при старте онлайн синхронизация не запускается сама по себе', () async {
      await watcher.start();

      // Даём отработать возможным микрозадачам потока.
      await Future<void>.delayed(Duration.zero);

      expect(watcher.isRunning, isTrue);
      expect(sync.calls, 0);
    });

    test('переход офлайн → онлайн запускает синхронизацию', () async {
      await watcher.start();

      probe.emit([ConnectivityResult.none]);
      await Future<void>.delayed(Duration.zero);
      expect(sync.calls, 0, reason: 'пока сети нет, отправлять нечего');

      probe.emit([ConnectivityResult.wifi]);
      await Future<void>.delayed(Duration.zero);

      expect(sync.calls, 1, reason: 'связь вернулась — очередь должна уйти');
    });

    test('повторные события «онлайн» не дают лишних синхронизаций', () async {
      await watcher.start();

      probe.emit([ConnectivityResult.none]);
      await Future<void>.delayed(Duration.zero);

      probe.emit([ConnectivityResult.wifi]);
      probe.emit([ConnectivityResult.wifi]);
      probe.emit([ConnectivityResult.mobile]);
      await Future<void>.delayed(Duration.zero);

      // Синхронизация нужна только на переходе из офлайна.
      expect(sync.calls, 1);
    });

    test('если стартовать офлайн, первое появление сети синхронизирует', () async {
      probe.current = [ConnectivityResult.none];
      await watcher.start();

      probe.emit([ConnectivityResult.wifi]);
      await Future<void>.delayed(Duration.zero);

      expect(sync.calls, 1);
    });

    test('результат передаётся в onSynced для обновления UI', () async {
      SyncOutcome? received;
      watcher.onSynced = (outcome) => received = outcome;

      await watcher.start();
      probe.emit([ConnectivityResult.none]);
      probe.emit([ConnectivityResult.wifi]);
      await Future<void>.delayed(Duration.zero);

      expect(received, isNotNull);
      expect(received!.pushed, 1);
      expect(received!.ok, isTrue);
    });

    test('stop() снимает подписку и останавливает таймер', () async {
      await watcher.start();
      expect(watcher.isRunning, isTrue);

      await watcher.stop();
      expect(watcher.isRunning, isFalse);

      probe.emit([ConnectivityResult.none]);
      probe.emit([ConnectivityResult.wifi]);
      await Future<void>.delayed(Duration.zero);

      expect(sync.calls, 0, reason: 'после stop() сигналы игнорируются');
    });

    test('пустой список результатов считается офлайном', () async {
      await watcher.start();

      probe.emit([]);
      probe.emit([ConnectivityResult.wifi]);
      await Future<void>.delayed(Duration.zero);

      expect(sync.calls, 1);
    });
  });

  group('SyncService: защита от параллельных запусков', () {
    test('одновременные вызовы дают один проход', () async {
      final api = ApiClient('http://localhost:1');
      final service = SyncService(api, LocalEventStore(), DeviceIdentity());

      // Без токена синхронизация сразу возвращает ошибку — важно, что
      // второй вызов получает тот же Future, а не запускает новый проход.
      final first = service.syncNow();
      final second = service.syncNow();

      expect(identical(first, second), isTrue);

      final results = await Future.wait([first, second]);
      expect(results[0].error, 'Нет активной сессии');
      expect(results[1].error, 'Нет активной сессии');
    });

    test('после завершения можно запускать снова', () async {
      final api = ApiClient('http://localhost:1');
      final service = SyncService(api, LocalEventStore(), DeviceIdentity());

      await service.syncNow();
      final next = service.syncNow();

      // Новый проход — новый объект Future.
      expect(next, isA<Future<SyncOutcome>>());
      expect((await next).error, 'Нет активной сессии');
    });
  });
}
