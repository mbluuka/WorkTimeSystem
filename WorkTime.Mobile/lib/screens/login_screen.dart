// В Flutter 3.50 пакет material помечен deprecated в пользу независимого
// material_ui, который мы сознательно не используем (см. pubspec.yaml).
// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

import '../services/api_client.dart';
import '../services/device_identity.dart';

/// Экран входа (FR11).
///
/// Адрес сервера настраивается, потому что для эмулятора Android хост
/// доступен как 10.0.2.2, а на физическом устройстве нужен LAN-IP машины
/// с сервером.
class LoginScreen extends StatefulWidget {
  final ApiClient api;
  final DeviceIdentity identity;
  final VoidCallback onLoggedIn;

  const LoginScreen({
    super.key,
    required this.api,
    required this.identity,
    required this.onLoggedIn,
  });

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _server = TextEditingController();

  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _server.text = widget.identity.serverUrl();
    final saved = widget.identity.email();
    if (saved != null) _email.text = saved;
  }

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _server.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final email = _email.text.trim();
    final password = _password.text;

    if (email.isEmpty || password.isEmpty) {
      setState(() => _error = 'Введите email и пароль');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await widget.identity.saveServerUrl(_server.text);
      widget.api.baseUrl = widget.identity.serverUrl();

      final auth = await widget.api.login(email, password);
      final userId = ApiClient.decodeUserId(auth.token);
      if (userId.isEmpty) {
        throw ApiException(500, 'Сервер не вернул идентификатор пользователя');
      }

      await widget.identity.saveSession(auth.token, userId, email);

      // Устройство нужно зарегистрировать сразу: без него сервер отклонит
      // события ссылкой FK на DeviceId.
      try {
        await widget.api.registerDevice(
          deviceId: widget.identity.deviceId(),
          userId: userId,
          name: widget.identity.deviceName(),
          platform: widget.identity.platform(),
        );
      } on ApiException {
        // Офлайн при входе — не ошибка: регистрация произойдёт при
        // первой синхронизации (см. SyncService).
      }

      if (mounted) widget.onLoggedIn();
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (e) {
      if (mounted) {
        setState(() => _error = 'Сервер недоступен: $e');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Вход в WorkTime')),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text('Учёт рабочего времени',
                  style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600)),
              const SizedBox(height: 4),
              const Text('Мобильный клиент. Работает офлайн: отметки '
                  'сохраняются на устройстве и уходят на сервер при связи.'),
              const SizedBox(height: 24),
              TextField(
                controller: _server,
                keyboardType: TextInputType.url,
                decoration: const InputDecoration(
                  labelText: 'Адрес сервера',
                  helperText: 'Эмулятор Android: http://10.0.2.2:8080',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _email,
                keyboardType: TextInputType.emailAddress,
                autofillHints: const [AutofillHints.email],
                decoration: const InputDecoration(
                  labelText: 'Email',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _password,
                obscureText: true,
                onSubmitted: (_) => _submit(),
                decoration: const InputDecoration(
                  labelText: 'Пароль',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 16),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(_error!,
                      style: const TextStyle(color: Colors.redAccent)),
                ),
              FilledButton(
                onPressed: _busy ? null : _submit,
                child: _busy
                    ? const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('Войти'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
