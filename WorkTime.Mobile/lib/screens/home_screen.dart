// В Flutter 3.50 пакет material помечен deprecated в пользу независимого
// material_ui, который мы сознательно не используем (см. pubspec.yaml).
// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

import '../models/work_event.dart';
import '../services/api_client.dart';
import '../services/connectivity_watcher.dart';
import '../services/device_identity.dart';
import '../services/sync_service.dart';
import '../storage/local_event_store.dart';

/// Главный экран сотрудника.
///
/// Структура взята из раздела «Требования к прототипам» документа:
/// крупные кнопки действий, текущий статус, индикатор синхронизации,
/// простой список истории.
class HomeScreen extends StatefulWidget {
  final ApiClient api;
  final LocalEventStore store;
  final DeviceIdentity identity;
  final SyncService sync;

  /// Фоновый наблюдатель за сетью (FR10). Экран подписывается на его
  /// события, чтобы история и индикатор очереди обновлялись сами, когда
  /// связь вернулась и очередь ушла на сервер.
  final ConnectivityWatcher watcher;

  final VoidCallback onLogout;

  const HomeScreen({
    super.key,
    required this.api,
    required this.store,
    required this.identity,
    required this.sync,
    required this.watcher,
    required this.onLogout,
  });

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

enum _DayState {
  idle, // день ещё не начат или уже закрыт
  working,
  onBreak;

  String get label => switch (this) {
        _DayState.idle => 'День не начат',
        _DayState.working => 'Работаю',
        _DayState.onBreak => 'Перерыв',
      };

  IconData get icon => switch (this) {
        _DayState.idle => Icons.brightness_2_outlined,
        _DayState.working => Icons.work_outline,
        _DayState.onBreak => Icons.local_cafe_outlined,
      };
}

class _HomeScreenState extends State<HomeScreen> {
  _DayState _state = _DayState.idle;
  WorkSummary _summary =
      const WorkSummary(workMinutes: 0, breakMinutes: 0, days: 0, remainingMinutes: 480);
  List<WorkEvent> _events = [];
  List<ProjectInfo> _projects = [];
  String? _selectedProjectId;
  int _pending = 0;
  bool _busy = false;
  String? _message;
  bool _messageIsError = false;

  @override
  void initState() {
    super.initState();

    // FR10: когда фоновая синхронизация отработала (вернулась сеть или
    // сработал таймер), обновляем историю и индикатор очереди — иначе
    // экран показывал бы устаревшие «ждут отправки».
    widget.watcher.onSynced = (outcome) {
      if (!mounted) return;
      _refresh();
      _show(outcome.summary, isError: !outcome.ok);
    };

    _refresh();
    _loadProjects();
  }

  @override
  void dispose() {
    // Экран — единственный подписчик; после его закрытия колбэк не нужен
    // и мог бы дёргать setState на уничтоженном State.
    widget.watcher.onSynced = null;
    super.dispose();
  }

  Future<void> _refresh() async {
    final now = DateTime.now();
    final startOfDay = DateTime(now.year, now.month, now.day);
    final endOfDay = startOfDay.add(const Duration(days: 1));

    final today = (await widget.store.all(limit: 500))
        .where((e) =>
            e.timestamp.toLocal().isAfter(startOfDay) &&
            e.timestamp.toLocal().isBefore(endOfDay))
        .toList()
      ..sort((a, b) => a.timestamp.compareTo(b.timestamp));

    var state = _DayState.idle;
    if (today.isNotEmpty) {
      state = switch (today.last.type) {
        WorkEventType.startWork => _DayState.working,
        WorkEventType.endBreak => _DayState.working,
        WorkEventType.startBreak => _DayState.onBreak,
        WorkEventType.endWork => _DayState.idle,
      };
    }

    final summary = await widget.store.summarize(
        startOfDay.toUtc(), endOfDay.toUtc());
    final recent = await widget.store.all(limit: 30);
    final pending = await widget.store.countPending();

    if (!mounted) return;
    setState(() {
      _state = state;
      _summary = summary;
      _events = recent;
      _pending = pending;
    });
  }

  Future<void> _loadProjects() async {
    try {
      final projects = await widget.api.projects();
      if (mounted) setState(() => _projects = projects);
    } on ApiException {
      // Офлайн: выбор проекта просто будет недоступен, отметки работают.
    }
  }

  Future<void> _record(WorkEventType type) async {
    setState(() => _busy = true);
    try {
      final userId = widget.identity.userId();
      if (userId == null) return;

      await widget.store.record(
        userId: userId,
        deviceId: widget.identity.deviceId(),
        type: type,
        projectId: _selectedProjectId,
      );

      // Отправка сразу: если связи нет, запись останется в Pending и
      // уйдёт при следующей синхронизации.
      final outcome = await widget.sync.syncNow();
      await _refresh();

      _show(
        '${type.label}: записано. ${outcome.ok ? outcome.summary : 'в очереди — ${outcome.error}'}',
        isError: !outcome.ok,
      );
    } catch (e) {
      _show('Не удалось записать событие: $e', isError: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _sync() async {
    setState(() => _busy = true);
    try {
      final outcome = await widget.sync.syncNow();
      await _refresh();
      _show(outcome.summary, isError: !outcome.ok);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Ручная корректировка времени записи (FR7).
  ///
  /// Климент не пытается сам решать, разрешена ли правка: сервер
  /// проверяет окно через PeriodEditGuard и возвращает 400 с текстом,
  /// который мы и показываем.
  Future<void> _editEvent(WorkEvent event) async {
    final picked = await showDatePicker(
      context: context,
      initialDate: event.timestamp.toLocal(),
      firstDate: DateTime(2020),
      lastDate: DateTime.now().add(const Duration(days: 1)),
    );
    if (!mounted || picked == null) return;

    final time = await showTimePicker(
      context: context,
      initialTime: TimeOfDay.fromDateTime(event.timestamp.toLocal()),
    );
    if (!mounted || time == null) return;

    final newTimestamp =
        DateTime(picked.year, picked.month, picked.day, time.hour, time.minute);

    setState(() => _busy = true);
    try {
      await widget.api.putWorkEvent(event, newTimestamp: newTimestamp);
      await widget.store.updateTimestamp(event.id, newTimestamp);
      await _refresh();
      _show('Время обновлено');
    } on ApiException catch (e) {
      // 400 от PeriodEditGuard: правка за пределами окна 7 дней.
      _show(e.message, isError: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _deleteEvent(WorkEvent event) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Удалить запись?'),
        content: Text('${event.type.label}\n'
            '${_fmt(event.timestamp)}'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('Отмена')),
          FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('Удалить')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busy = true);
    try {
      await widget.api.deleteWorkEvent(event.id);
      await widget.store.delete(event.id);
      await _refresh();
      _show('Запись удалена');
    } on ApiException catch (e) {
      _show(e.message, isError: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _show(String text, {bool isError = false}) {
    if (!mounted) return;
    setState(() {
      _message = text;
      _messageIsError = isError;
    });
  }

  static String _fmt(DateTime dt) {
    final l = dt.toLocal();
    String two(int v) => v.toString().padLeft(2, '0');
    return '${two(l.day)}.${two(l.month)}.${l.year} '
        '${two(l.hour)}:${two(l.minute)}';
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('WorkTime'),
        actions: [
          IconButton(
            tooltip: 'Выйти',
            icon: const Icon(Icons.logout),
            onPressed: widget.onLogout,
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          await _sync();
        },
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            _statusCard(),
            const SizedBox(height: 12),
            _summaryRow(),
            const SizedBox(height: 16),
            if (_projects.isNotEmpty) _projectPicker(),
            const SizedBox(height: 12),
            _actionButtons(),
            const SizedBox(height: 16),
            _syncBanner(),
            if (_message != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: _messageIsError
                      ? Colors.red.withValues(alpha: 0.08)
                      : Colors.green.withValues(alpha: 0.08),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(_message!,
                    style: TextStyle(
                        color: _messageIsError ? Colors.red : Colors.green)),
              ),
            ],
            const SizedBox(height: 20),
            Text('Последние записи',
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            if (_events.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 24),
                child: Center(child: Text('Записей пока нет')),
              )
            else
              ..._events.map((e) => Card(
                    child: ListTile(
                      leading: Icon(_iconFor(e.type)),
                      title: Text(e.type.label),
                      subtitle: Text(_fmt(e.timestamp)),
                      trailing: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          _statusDot(e),
                          PopupMenuButton<String>(
                            onSelected: (v) => v == 'edit'
                                ? _editEvent(e)
                                : _deleteEvent(e),
                            itemBuilder: (ctx) => const [
                              PopupMenuItem(value: 'edit', child: Text('Изменить время')),
                              PopupMenuItem(value: 'delete', child: Text('Удалить')),
                            ],
                          ),
                        ],
                      ),
                    ),
                  )),
          ],
        ),
      ),
    );
  }

  Widget _statusCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Icon(_state.icon, size: 40, color: Theme.of(context).colorScheme.primary),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(_state.label,
                      style: Theme.of(context).textTheme.titleLarge),
                  Text('Сегодня: ${_summary.workLabel}, перерыв ${_summary.breakLabel}'),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _summaryRow() {
    return Row(
      children: [
        _tile('Отработано', _summary.workLabel),
        const SizedBox(width: 8),
        _tile('До 8 часов', _summary.remainingLabel),
        const SizedBox(width: 8),
        _tile('Дней', '${_summary.days}'),
      ],
    );
  }

  Widget _tile(String label, String value) {
    return Expanded(
      child: Card(
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 8),
          child: Column(
            children: [
              Text(value, style: const TextStyle(fontWeight: FontWeight.bold)),
              const SizedBox(height: 4),
              Text(label,
                  textAlign: TextAlign.center,
                  style: const TextStyle(fontSize: 12)),
            ],
          ),
        ),
      ),
    );
  }

  Widget _projectPicker() {
    return DropdownButtonFormField<String?>(
      initialValue: _selectedProjectId,
      decoration: const InputDecoration(
        labelText: 'Проект (необязательно)',
        border: OutlineInputBorder(),
      ),
      items: [
        const DropdownMenuItem<String?>(
            value: null, child: Text('Без проекта')),
        ..._projects.map((pr) => DropdownMenuItem<String?>(
              value: pr.id,
              child: Text(pr.name),
            )),
      ],
      onChanged: (v) => setState(() => _selectedProjectId = v),
    );
  }

  Widget _actionButtons() {
    return Column(
      children: [
        if (_state == _DayState.working) ...[
          FilledButton.icon(
            icon: const Icon(Icons.local_cafe),
            label: const Text('Перерыв — начало'),
            onPressed: _busy
                ? null
                : () => _record(WorkEventType.startBreak),
          ),
          const SizedBox(height: 8),
          FilledButton.tonalIcon(
            icon: const Icon(Icons.flag),
            label: const Text('Закончить день'),
            onPressed:
                _busy ? null : () => _record(WorkEventType.endWork),
          ),
        ] else if (_state == _DayState.onBreak) ...[
          FilledButton.icon(
            icon: const Icon(Icons.play_arrow),
            label: const Text('Перерыв — конец'),
            onPressed:
                _busy ? null : () => _record(WorkEventType.endBreak),
          ),
          const SizedBox(height: 8),
          FilledButton.tonalIcon(
            icon: const Icon(Icons.flag),
            label: const Text('Закончить день'),
            onPressed:
                _busy ? null : () => _record(WorkEventType.endWork),
          ),
        ] else ...[
          FilledButton.icon(
            icon: const Icon(Icons.play_arrow),
            label: const Text('Начать день'),
            onPressed:
                _busy ? null : () => _record(WorkEventType.startWork),
          ),
        ],
      ],
    );
  }

  Widget _syncBanner() {
    return Card(
      color: _pending > 0
          ? Colors.orange.withValues(alpha: 0.12)
          : Colors.green.withValues(alpha: 0.08),
      child: ListTile(
        leading: Icon(
          _pending > 0 ? Icons.cloud_upload_outlined : Icons.cloud_done_outlined,
          color: _pending > 0 ? Colors.orange : Colors.green,
        ),
        title: Text(_pending > 0
            ? 'Ждут отправки: $_pending'
            : 'Все данные синхронизированы'),
        subtitle: const Text('Тяните вниз, чтобы отправить'),
        trailing: IconButton(
          icon: const Icon(Icons.sync),
          onPressed: _busy ? null : _sync,
        ),
      ),
    );
  }

  Widget _statusDot(WorkEvent e) {
    final synced = e.syncStatus == SyncStatus.synced;
    return Padding(
      padding: const EdgeInsets.only(right: 4),
      child: Icon(
        synced ? Icons.cloud_done : Icons.cloud_upload,
        size: 18,
        color: synced ? Colors.green : Colors.orange,
      ),
    );
  }

  IconData _iconFor(WorkEventType type) => switch (type) {
        WorkEventType.startWork => Icons.play_arrow,
        WorkEventType.endWork => Icons.stop,
        WorkEventType.startBreak => Icons.local_cafe,
        WorkEventType.endBreak => Icons.coffee_outlined,
      };
}
