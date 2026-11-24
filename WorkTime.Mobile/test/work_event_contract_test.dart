import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:work_time_mobile/models/work_event.dart';
import 'package:work_time_mobile/services/api_client.dart';
import 'package:work_time_mobile/services/sync_service.dart';

/// Контрактные тесты мобильного клиента.
///
/// Проверяют то, из-за чего клиент обычно ломается на живом сервере:
/// регистр enum в JSON, формат дат, имя claim со userId и поведение
/// очереди при частичных ошибках синхронизации. SQLite и SharedPreferences
/// в этих тестах не нужны.
void main() {
  group('WorkEventType', () {
    test('wireName совпадает с серверным JsonStringEnumConverter', () {
      expect(WorkEventType.startWork.wireName, 'StartWork');
      expect(WorkEventType.startBreak.wireName, 'StartBreak');
      expect(WorkEventType.endBreak.wireName, 'EndBreak');
      expect(WorkEventType.endWork.wireName, 'EndWork');
    });

    test('fromWireName терпим к регистру и пробелам', () {
      expect(WorkEventType.fromWireName('EndWork'), WorkEventType.endWork);
      expect(WorkEventType.fromWireName('endwork'), WorkEventType.endWork);
      expect(WorkEventType.fromWireName(' EndBreak '), WorkEventType.endBreak);
    });

    test('неизвестное имя не роняет клиента, а сворачивается в StartWork', () {
      expect(WorkEventType.fromWireName(null), WorkEventType.startWork);
      expect(WorkEventType.fromWireName('Lunch'), WorkEventType.startWork);
    });

    test('dbValue соответствует порядку серверного enum', () {
      expect(WorkEventType.values.map((e) => e.dbValue).toList(), [0, 1, 2, 3]);
      expect(WorkEventType.fromDbValue(2), WorkEventType.endBreak);
      // Значение вне диапазона (например, после серверного расширения)
      // не должно выбрасывать исключение.
      expect(WorkEventType.fromDbValue(99), WorkEventType.endWork);
    });
  });

  group('WorkEvent', () {
    final event = WorkEvent(
      id: '11111111-2222-3333-4444-555555555555',
      userId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      deviceId: '99999999-8888-7777-6666-555555555555',
      type: WorkEventType.endBreak,
      timestamp: DateTime.utc(2026, 11, 24, 9, 24, 36),
      createdAt: DateTime.utc(2026, 11, 24, 9, 24, 37),
      deviceSeq: 7,
      projectId: 'cccccccc-1111-2222-3333-444444444444',
    );

    test('toJson отдаёт enum строкой и даты в UTC с суффиксом Z', () {
      final json = event.toJson();

      expect(json['type'], 'EndBreak');
      expect(json['timestamp'], '2026-11-24T09:24:36.000Z');
      expect(json['createdAt'], '2026-11-24T09:24:37.000Z');
      expect(json['id'], event.id);
      expect(json['deviceSeq'], 7);
      expect(json['projectId'], event.projectId);

      // Сервер принимает только Guid; проверка формата, а не парсинга .NET.
      expect(
        RegExp(r'^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$')
            .hasMatch(json['id'] as String),
        isTrue,
      );
    });

    test('toJson не отправляет поле syncStatus — сервер его не знает', () {
      expect(event.toJson().containsKey('syncStatus'), isFalse);
    });

    test('roundtrip через SQLite-представление сохраняет все поля', () {
      final restored = WorkEvent.fromDbMap(event.toDbMap());

      expect(restored.id, event.id);
      expect(restored.userId, event.userId);
      expect(restored.deviceId, event.deviceId);
      expect(restored.type, WorkEventType.endBreak);
      expect(restored.timestamp, event.timestamp);
      expect(restored.createdAt, event.createdAt);
      expect(restored.deviceSeq, 7);
      expect(restored.projectId, event.projectId);
      expect(restored.syncStatus, SyncStatus.pending);
    });

    test('enum в БД хранится числом, Guid — строкой', () {
      final map = event.toDbMap();
      expect(map['Type'], 2);
      expect(map['SyncStatus'], 0);
      expect(map['Id'], isA<String>());
      expect(map['SyncedAt'], isNull);
    });

    test('локальное время сохраняется в UTC', () {
      // 12:00 при смещении +03:00 — это 09:00Z.
      final local = DateTime(2026, 11, 24, 12, 0);
      final e = WorkEvent(
        id: event.id,
        userId: event.userId,
        deviceId: event.deviceId,
        type: WorkEventType.startWork,
        timestamp: local,
        createdAt: local,
        deviceSeq: 1,
      );
      expect(e.toJson()['timestamp'],
          e.timestamp.toUtc().toIso8601String());
      expect(DateTime.parse(e.toJson()['timestamp'] as String).isUtc, isTrue);
    });
  });

  group('ApiClient.decodeUserId', () {
    // Сервер пишет claim с полным URI-именем (JwtSecurityTokenHandler),
    // именно такой токен получен живым POST /api/auth/login.
    String buildJwt(Map<String, Object?> claims) {
      String b64(Map<String, Object?> m) => base64Url
          .encode(utf8.encode(jsonEncode(m)))
          .replaceAll('=', '');
      return '${b64({'alg': 'HS256', 'typ': 'JWT'})}'
          '.${b64(claims)}.signature';
    }

    test('читает userId из полного URI-имени claim', () {
      final token = buildJwt({
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier':
            '3f2504e0-4f89-11d3-9a0c-0305e82c3301',
        'role': 'Employee',
      });
      expect(ApiClient.decodeUserId(token),
          '3f2504e0-4f89-11d3-9a0c-0305e82c3301');
    });

    test('читает userId из короткого nameid и sub', () {
      expect(
          ApiClient.decodeUserId(
              buildJwt({'nameid': '11111111-1111-1111-1111-111111111111'})),
          '11111111-1111-1111-1111-111111111111');
      expect(
          ApiClient.decodeUserId(
              buildJwt({'sub': '22222222-2222-2222-2222-222222222222'})),
          '22222222-2222-2222-2222-222222222222');
    });

    test('пустая строка вместо падения на битом или чужом токене', () {
      expect(ApiClient.decodeUserId(''), '');
      expect(ApiClient.decodeUserId('abc'), '');
      expect(ApiClient.decodeUserId(buildJwt({'role': 'Admin'})), '');
      // sub есть, но это не Guid (например, email) — не принимаем.
      expect(ApiClient.decodeUserId(buildJwt({'sub': 'user@example.com'})), '');
    });
  });

  group('SyncOutcome', () {
    test('ошибка важнее счётчиков', () {
      const o = SyncOutcome(pushed: 2, error: 'Сервер недоступен');
      expect(o.ok, isFalse);
      expect(o.summary, 'Сервер недоступен');
    });

    test('без изменений — короткий ответ', () {
      const o = SyncOutcome();
      expect(o.ok, isTrue);
      expect(o.summary, 'Синхронизировано, изменений нет');
    });

    test('частичная отправка показывает все ненулевые счётчики', () {
      const o = SyncOutcome(pushed: 3, pulled: 1, failed: 2);
      expect(o.summary, 'отправлено: 3, получено: 1, ошибок: 2');
    });
  });
}
