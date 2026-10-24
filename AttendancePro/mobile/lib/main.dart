import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'config.dart';
import 'screens/auth/login_screen.dart';
import 'screens/home_shell.dart';
import 'state/app_state.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final app = AppState();
  runApp(ChangeNotifierProvider.value(value: app, child: const AttendanceApp()));
  await app.init();
}

class AttendanceApp extends StatelessWidget {
  const AttendanceApp({super.key});

  @override
  Widget build(BuildContext context) {
    const seed = Color(0xFF0F766E);
    ThemeData theme(Brightness b) => ThemeData(
          useMaterial3: true,
          colorScheme: ColorScheme.fromSeed(seedColor: seed, brightness: b),
          inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder(borderRadius: BorderRadius.all(Radius.circular(10)))),
          filledButtonTheme: FilledButtonThemeData(style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)))),
          cardTheme: CardThemeData(elevation: 0, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14), side: BorderSide(color: Colors.grey.withValues(alpha: .25)))),
        );
    return MaterialApp(
      title: AppConfig.appName,
      debugShowCheckedModeBanner: false,
      theme: theme(Brightness.light),
      darkTheme: theme(Brightness.dark),
      home: Consumer<AppState>(builder: (_, app, _) {
        switch (app.phase) {
          case Phase.loading:
            return const Scaffold(body: Center(child: CircularProgressIndicator()));
          case Phase.signedOut:
            return const LoginScreen();
          case Phase.ready:
            return const HomeShell();
        }
      }),
    );
  }
}
