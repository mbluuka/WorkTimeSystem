import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:work_time_mobile/main.dart';

void main() {
  testWidgets('без активной сессии приложение показывает экран входа',
      (WidgetTester tester) async {
    // В тестовой среде нет плагина shared_preferences, поэтому задаём
    // пустое хранилище явно.
    SharedPreferences.setMockInitialValues({});

    await tester.pumpWidget(const WorkTimeApp());

    // pumpAndSettle дожидается окончания инициализации и смены экрана
    // «загрузка → вход».
    await tester.pumpAndSettle();

    expect(find.text('Вход в WorkTime'), findsOneWidget);
    expect(find.text('Войти'), findsOneWidget);

    // Адрес сервера по умолчанию рассчитан на эмулятор Android.
    expect(find.text('http://10.0.2.2:8080'), findsOneWidget);
  });
}
