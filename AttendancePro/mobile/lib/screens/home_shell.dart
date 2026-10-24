import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../state/app_state.dart';
import '../state/home_controller.dart';
import 'history_tab.dart';
import 'profile_tab.dart';
import 'requests_tab.dart';
import 'stats_tab.dart';
import 'today_tab.dart';

class HomeShell extends StatefulWidget {
  const HomeShell({super.key});
  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> with WidgetsBindingObserver {
  int _i = 0;
  late final HomeController _home;

  @override
  void initState() {
    super.initState();
    _home = HomeController(context.read<AppState>())..start();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _home.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    if (s == AppLifecycleState.resumed) {
      _home.startLocation(); // user may have just granted permission / enabled GPS in Settings
      _home.syncPending();
      _home.load(silent: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final pages = [const TodayTab(), const HistoryTab(), const StatsTab(), const RequestsTab(), const ProfileTab()];
    return ChangeNotifierProvider.value(
      value: _home,
      child: Scaffold(
        body: SafeArea(child: IndexedStack(index: _i, children: pages)),
        bottomNavigationBar: NavigationBar(
          selectedIndex: _i,
          onDestinationSelected: (i) => setState(() => _i = i),
          destinations: const [
            NavigationDestination(icon: Icon(Icons.my_location_outlined), selectedIcon: Icon(Icons.my_location), label: 'Today'),
            NavigationDestination(icon: Icon(Icons.history), label: 'History'),
            NavigationDestination(icon: Icon(Icons.insights_outlined), selectedIcon: Icon(Icons.insights), label: 'My stats'),
            NavigationDestination(icon: Icon(Icons.event_note_outlined), selectedIcon: Icon(Icons.event_note), label: 'Requests'),
            NavigationDestination(icon: Icon(Icons.person_outline), selectedIcon: Icon(Icons.person), label: 'Profile'),
          ],
        ),
      ),
    );
  }
}
