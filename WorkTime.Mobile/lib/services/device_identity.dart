import 'dart:io';

import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

import 'api_client.dart';

/// Стабильная идентичность устройства и сохранённые параметры входа.
///
/// Соответствует `WorkTime.Desktop.Services.DesktopPaths`: device id
/// переживает перезапуск и обновление приложения, потому что на сервере
/// события привязаны FK к конкретному DeviceId. Теряем Id — получаем
/// «Device does not exist» на каждой отправке.
class DeviceIdentity {
  static const _deviceKey = 'worktime_device_id';
  static const _serverKey = 'worktime_server_url';
  static const _tokenKey = 'worktime_token';
  static const _userKey = 'worktime_user_id';
  static const _emailKey = 'worktime_email';
  static const _lastSyncKey = 'worktime_last_sync';

  /// Значение по умолчанию для эмулятора Android: хост-машина видна ему
  /// как 10.0.2.2. localhost внутри эмулятора — сам эмулятор.
  static const defaultServer = 'http://10.0.2.2:8080';

  late final SharedPreferences _prefs;

  Future<void> init() async {
    _prefs = await SharedPreferences.getInstance();
  }

  /// Возвращает существующий Id устройства или создаёт и запоминает новый.
  String deviceId() {
    var id = _prefs.getString(_deviceKey);
    if (id == null || id.isEmpty) {
      id = const Uuid().v4();
      _prefs.setString(_deviceKey, id);
    }
    return id;
  }

  String serverUrl() => _prefs.getString(_serverKey) ?? defaultServer;

  Future<void> saveServerUrl(String url) =>
      _prefs.setString(_serverKey, url.trim());

  String? token() => _prefs.getString(_tokenKey);

  Future<void> saveSession(String token, String userId, String email) async {
    await _prefs.setString(_tokenKey, token);
    await _prefs.setString(_userKey, userId);
    await _prefs.setString(_emailKey, email);
  }

  Future<void> clearSession() async {
    await _prefs.remove(_tokenKey);
    await _prefs.remove(_userKey);
  }

  String? userId() => _prefs.getString(_userKey);

  String? email() => _prefs.getString(_emailKey);

  Future<void> saveUserId(String id) => _prefs.setString(_userKey, id);

  /// Момент последней успешной выгрузки — точка отсчёта для pull-синхронизации
  /// (`GET /api/WorkEvents/sync?since=`).
  DateTime? lastSyncAt() {
    final raw = _prefs.getString(_lastSyncKey);
    return raw == null ? null : DateTime.tryParse(raw)?.toUtc();
  }

  Future<void> saveLastSync(DateTime when) =>
      _prefs.setString(_lastSyncKey, when.toUtc().toIso8601String());

  /// Имя устройства для реестра сервера: модель телефона и версия ОС,
  /// чтобы администратор видел, с чего именно синхронизируются данные.
  String deviceName() {
    final model = Platform.operatingSystemVersion;
    return 'Android ($model)';
  }

  String platform() => 'Android';

  /// Восстанавливает сессию из сохранённого токена.
  ///
  /// Токен не проверяется локально по сроку действия: если он истёк,
  /// сервер ответит 401, и UI вернёт пользователя на экран входа.
  Future<bool> restoreSession(ApiClient api) async {
    final savedToken = token();
    final uid = userId();
    if (savedToken == null || savedToken.isEmpty || uid == null || uid.isEmpty) {
      return false;
    }
    api.baseUrl = serverUrl();
    api.setToken(savedToken);
    return true;
  }
}
