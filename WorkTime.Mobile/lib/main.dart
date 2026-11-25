// В Flutter 3.50 пакет material помечен deprecated в пользу независимого
// material_ui, который мы сознательно не используем (см. pubspec.yaml).
// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

import 'services/api_client.dart';
import 'services/connectivity_watcher.dart';
import 'services/device_identity.dart';
import 'services/sync_service.dart';
import 'storage/local_event_store.dart';
import 'screens/home_screen.dart';
import 'screens/login_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const WorkTimeApp());
}

/// Корневой виджет мобильного клиента (FR19).
///
/// Здесь один раз создаются все сервисы и определяется, какой экран
/// показывать: если токен сохранён и ещё валиден — сразу главный экран,
/// иначе вход. Локальная база при этом не чистится: неотправленные
/// переживают выход из аккаунта и уходят на сервер при следующем входе.
class WorkTimeApp extends StatefulWidget {
  const WorkTimeApp({super.key});

  @override
  State<WorkTimeApp> createState() => _WorkTimeAppState();
}

class _WorkTimeAppState extends State<WorkTimeApp> {
  final _identity = DeviceIdentity();
  final _store = LocalEventStore();
  late final ApiClient _api;
  late final SyncService _sync;
  late final ConnectivityWatcher _watcher;

  bool _ready = false;
  bool _loggedIn = false;

  @override
  void initState() {
    super.initState();
    _bootstrap();
  }

  Future<void> _bootstrap() async {
    await _identity.init();
    _api = ApiClient(_identity.serverUrl());
    _sync = SyncService(_api, _store, _identity);
    _watcher = ConnectivityWatcher(_sync);

    final restored = await _identity.restoreSession(_api);

    // Сессию не «проверяем» локально: если токен просрочен, первый же
    // запрос вернёт 401 и UI отправит пользователя на вход.
    if (restored) {
      await _watcher.start();
    }

    if (mounted) {
      setState(() {
        _loggedIn = restored;
        _ready = true;
      });
    }
  }

  void _onLoggedIn() {
    setState(() => _loggedIn = true);
    // FR10: после входа очередь уходит сама при появлении связи.
    _watcher.start();
  }

  void _logout() {
    _watcher.stop();
    _api.setToken(null);
    _identity.clearSession();
    setState(() => _loggedIn = false);
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'WorkTime',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF2E5AAC)),
        useMaterial3: true,
      ),
      home: !_ready
          ? const Scaffold(
              body: Center(child: CircularProgressIndicator()),
            )
          : _loggedIn
              ? HomeScreen(
                  api: _api,
                  store: _store,
                  identity: _identity,
                  sync: _sync,
                  watcher: _watcher,
                  onLogout: _logout,
                )
              : LoginScreen(
                  api: _api,
                  identity: _identity,
                  onLoggedIn: _onLoggedIn,
                ),
    );
  }

  @override
  void dispose() {
    _watcher.stop();
    super.dispose();
  }
}
