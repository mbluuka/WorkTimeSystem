import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/work_event.dart';

/// Ответ `POST /api/auth/login`.
class AuthResult {
  final String token;
  final String role;

  const AuthResult(this.token, this.role);
}

/// Проект из `GET /api/projects` (FR5).
class ProjectInfo {
  final String id;
  final String name;

  const ProjectInfo(this.id, this.name);

  factory ProjectInfo.fromJson(Map<String, dynamic> j) =>
      ProjectInfo(j['id'] as String, j['name'] as String);
}

/// Ошибка уровня API: код ответа + текст, который вернул сервер.
class ApiException implements Exception {
  final int statusCode;
  final String message;

  ApiException(this.statusCode, this.message);

  @override
  String toString() => 'HTTP $statusCode: $message';
}

/// HTTP-клиент к серверу WorkTime.
///
/// Повторяет контракт `WorkTime.Desktop.Services.DesktopApiClient`, поэтому
/// мобильный клиент работает с тем же API без серверных изменений:
///  - авторизация JWT (FR11);
///  - регистрация устройства по стабильному Id (нужен FK на сервере);
///  - отправка событий `POST /api/WorkEvents` с Id клиента → идемпотентность;
///  - enum `type` — строкой, так как сервер добавил JsonStringEnumConverter.
class ApiClient {
  String baseUrl;
  final http.Client _http;

  String? _token;

  ApiClient(this.baseUrl, {http.Client? client}) : _http = client ?? http.Client();

  void setToken(String? token) => _token = token;

  bool get hasToken => _token != null && _token!.isNotEmpty;

  Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        if (_token != null) 'Authorization': 'Bearer $_token',
      };

  /// Сервер может быть указан без схемы и без завершающего слэша.
  Uri _u(String path) {
    var base = baseUrl.trim();
    if (!base.startsWith('http://') && !base.startsWith('https://')) {
      base = 'http://$base';
    }
    if (base.endsWith('/')) base = base.substring(0, base.length - 1);
    return Uri.parse('$base$path');
  }

  Future<AuthResult> login(String email, String password) async {
    final resp = await _http
        .post(_u('/api/auth/login'),
            headers: _headers,
            body: jsonEncode({'email': email, 'password': password}))
        .timeout(_timeout);

    if (resp.statusCode == 401 || resp.statusCode == 400) {
      throw ApiException(resp.statusCode, _errorOf(resp, 'Неверный логин или пароль'));
    }
    _ensureOk(resp);

    final json = jsonDecode(resp.body) as Map<String, dynamic>;
    final token = json['token'] as String;
    _token = token;
    return AuthResult(token, json['role'] as String? ?? 'Employee');
  }

  /// Регистрация устройства. Идемпотентна: сервер принимает повторный
  /// POST с тем же Id.
  Future<void> registerDevice({
    required String deviceId,
    required String userId,
    required String name,
    required String platform,
  }) async {
    final resp = await _http
        .post(_u('/api/devices'),
            headers: _headers,
            body: jsonEncode({
              'id': deviceId,
              'userId': userId,
              'name': name,
              'platform': platform,
            }))
        .timeout(_timeout);

    // 409 — устройство уже зарегистрировано ранее, это нормальный случай.
    if (resp.statusCode == 409) return;
    _ensureOk(resp);
  }

  /// Список проектов (FR5).
  Future<List<ProjectInfo>> projects() async {
    final resp =
        await _http.get(_u('/api/projects'), headers: _headers).timeout(_timeout);
    _ensureOk(resp);

    final list = jsonDecode(resp.body) as List<dynamic>;
    return list
        .map((e) => ProjectInfo.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Отправляет одно событие. Бросает [ApiException] с кодом и текстом —
  /// вызывающий код решает, повторять ли позже.
  Future<void> postWorkEvent(WorkEvent event) async {
    final resp = await _http
        .post(_u('/api/WorkEvents'),
            headers: _headers, body: jsonEncode(event.toJson()))
        .timeout(_timeout);
    _ensureOk(resp);
  }

  /// Обновление существующего события (FR7).
  ///
  /// Отправляются ВСЕ три поля — type, timestamp, projectId. Серверный
  /// `UpdateWorkEventRequest.Type` не является nullable и присваивается
  /// безусловно (`workEvent.Type = req.Type`), поэтому запрос без type
  /// молча превратил бы, например, «Конец перерыва» в «Начало дня».
  /// То же поле `projectId` без значения обнулило бы привязку к проекту.
  ///
  /// Именно здесь сервер применяет PeriodEditGuard: правка за пределами
  /// окна 7 дней для Employee/Manager возвращает 400, и клиент обязан
  /// показать сообщение сервера, а не «глотнуть» его.
  Future<void> putWorkEvent(WorkEvent event, {DateTime? newTimestamp}) async {
    final ts = (newTimestamp ?? event.timestamp).toUtc().toIso8601String();
    final resp = await _http
        .put(_u('/api/WorkEvents/${event.id}'),
            headers: _headers,
            body: jsonEncode({
              'type': event.type.wireName,
              'timestamp': ts,
              'projectId': event.projectId,
            }))
        .timeout(_timeout);
    _ensureOk(resp);
  }

  /// Удаляет событие на сервере (FR7).
  ///
  /// Окно корректировки здесь же: `PeriodEditGuard.CheckEdit` вызывается и
  /// при удалении, поэтому для записи старше 7 дней сервер вернёт 400.
  Future<void> deleteWorkEvent(String id) async {
    final resp =
        await _http.delete(_u('/api/WorkEvents/$id'), headers: _headers).timeout(_timeout);
    _ensureOk(resp);
  }

  /// История своих событий с сервера за период (FR6).
  Future<List<Map<String, dynamic>>> workEvents({
    DateTime? from,
    DateTime? to,
  }) async {
    final query = <String, String>{
      if (from != null) 'from': from.toUtc().toIso8601String(),
      if (to != null) 'to': to.toUtc().toIso8601String(),
    };
    final uri = _u('/api/WorkEvents').replace(queryParameters: query.isEmpty ? null : query);
    final resp = await _http.get(uri, headers: _headers).timeout(_timeout);
    _ensureOk(resp);
    return (jsonDecode(resp.body) as List<dynamic>)
        .cast<Map<String, dynamic>>();
  }

  /// Выгрузка событий пользователя с сервера (pull-часть FR10).
  ///
  /// Эндпоинт `GET /api/WorkEvents/sync?userId=&since=` фильтрует по
  /// `CreatedAt > since` и упорядочивает по CreatedAt, поэтому повторная
  /// выборка даёт только новые на сервере записи.
  Future<List<Map<String, dynamic>>> syncEvents({
    required String userId,
    DateTime? since,
  }) async {
    final query = <String, String>{
      'userId': userId,
      if (since != null) 'since': since.toUtc().toIso8601String(),
    };
    final uri = _u('/api/WorkEvents/sync').replace(queryParameters: query);
    final resp = await _http.get(uri, headers: _headers).timeout(_timeout);
    _ensureOk(resp);
    return (jsonDecode(resp.body) as List<dynamic>)
        .cast<Map<String, dynamic>>();
  }

  static const _timeout = Duration(seconds: 15);

  void _ensureOk(http.Response resp) {
    if (resp.statusCode >= 200 && resp.statusCode < 300) return;
    if (resp.statusCode == 401) {
      throw ApiException(401, 'Сессия истекла — войдите заново');
    }
    throw ApiException(resp.statusCode, _errorOf(resp, 'Запрос отклонён'));
  }

  /// Сервер возвращает ошибки в виде `{ "message": "..." }`.
  static String _errorOf(http.Response resp, String fallback) {
    try {
      final body = resp.body;
      if (body.isEmpty) return fallback;
      final json = jsonDecode(body);
      if (json is Map && json['message'] is String) return json['message'] as String;
    } catch (_) {
      // не JSON — вернём исходный текст целиком или заглушку
    }
    return resp.body.trim().isEmpty ? fallback : resp.body.trim();
  }

  /// Извлекает userId из JWT без проверки подписи.
  ///
  /// Токен выдан сервером в ответ на наш собственный логин, поэтому
  /// локальное доверие к нему безопасно. Перебираем и короткие ("nameid",
  /// "sub"), и полные URI-имена claim'ов: JwtSecurityTokenHandler пишет
  /// вторые, а JsonWebTokenHandler — первые.
  static String decodeUserId(String token) {
    const claimNames = [
      'nameid',
      'sub',
      'NameIdentifier',
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier',
    ];

    try {
      final parts = token.split('.');
      if (parts.length < 2) return '';

      var payload = parts[1].replaceAll('-', '+').replaceAll('_', '/');
      switch (payload.length % 4) {
        case 2:
          payload += '==';
        case 3:
          payload += '=';
      }

      final json =
          jsonDecode(utf8.decode(base64.decode(payload))) as Map<String, dynamic>;

      for (final name in claimNames) {
        final value = json[name];
        if (value is String && _looksLikeGuid(value)) return value;
      }
    } catch (_) {
      // повреждённый токен — пусть пользователь войдёт заново
    }
    return '';
  }

  static final _guidRe = RegExp(
      r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$');

  static bool _looksLikeGuid(String s) => _guidRe.hasMatch(s);
}
