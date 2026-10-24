import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../models.dart';
import '../services/api.dart';
import '../state/app_state.dart';
import '../widgets/common.dart';

/// Leave requests and attendance-correction requests.
class RequestsTab extends StatelessWidget {
  const RequestsTab({super.key});
  @override
  Widget build(BuildContext context) {
    return const DefaultTabController(
      length: 2,
      child: Column(children: [
        TabBar(tabs: [Tab(text: 'Leave'), Tab(text: 'Corrections')]),
        Expanded(child: TabBarView(children: [_LeaveList(), _RegList()])),
      ]),
    );
  }
}

String _d(DateTime d) => DateFormat('yyyy-MM-dd').format(d);

class _LeaveList extends StatefulWidget {
  const _LeaveList();
  @override
  State<_LeaveList> createState() => _LeaveListState();
}

class _LeaveListState extends State<_LeaveList> {
  late Future<List<LeaveItem>> _f;
  @override
  void initState() {
    super.initState();
    _f = context.read<AppState>().api.leaves();
  }

  void _reload() => setState(() {
        _f = context.read<AppState>().api.leaves();
      });

  Future<void> _apply() async {
    final api = context.read<AppState>().api;
    String type = 'Casual';
    DateTime from = DateTime.now(), to = DateTime.now();
    final reason = TextEditingController();
    final ok = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (c) => StatefulBuilder(
        builder: (c, set) => Padding(
          padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.of(c).viewInsets.bottom + 20),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Text('Apply for leave', style: Theme.of(c).textTheme.titleLarge),
            const SizedBox(height: 14),
            DropdownButtonFormField<String>(
              initialValue: type,
              decoration: const InputDecoration(labelText: 'Type'),
              items: const ['Casual', 'Sick', 'Annual', 'Unpaid', 'Other'].map((e) => DropdownMenuItem(value: e, child: Text(e))).toList(),
              onChanged: (v) => set(() => type = v!),
            ),
            const SizedBox(height: 12),
            Row(children: [
              Expanded(child: OutlinedButton.icon(icon: const Icon(Icons.calendar_today, size: 16), label: Text('From ${DateFormat('d MMM').format(from)}'), onPressed: () async {
                final p = await showDatePicker(context: c, initialDate: from, firstDate: DateTime.now().subtract(const Duration(days: 30)), lastDate: DateTime.now().add(const Duration(days: 365)));
                if (p != null) set(() { from = p; if (to.isBefore(from)) to = from; });
              })),
              const SizedBox(width: 10),
              Expanded(child: OutlinedButton.icon(icon: const Icon(Icons.event, size: 16), label: Text('To ${DateFormat('d MMM').format(to)}'), onPressed: () async {
                final p = await showDatePicker(context: c, initialDate: to, firstDate: from, lastDate: DateTime.now().add(const Duration(days: 365)));
                if (p != null) set(() => to = p);
              })),
            ]),
            const SizedBox(height: 12),
            TextField(controller: reason, maxLines: 2, decoration: const InputDecoration(labelText: 'Reason (optional)')),
            const SizedBox(height: 16),
            FilledButton(onPressed: () => Navigator.pop(c, true), child: const Text('Submit')),
          ]),
        ),
      ),
    );
    if (ok != true) return;
    try {
      await api.applyLeave(type, _d(from), _d(to), reason.text.trim().isEmpty ? null : reason.text.trim());
      if (mounted) toast(context, 'Leave request submitted');
      _reload();
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message, error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(onPressed: _apply, icon: const Icon(Icons.add), label: const Text('Apply leave')),
      body: FutureBuilder<List<LeaveItem>>(
        future: _f,
        builder: (c, s) {
          if (s.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (s.hasError) return ErrorBox(s.error is ApiException ? (s.error as ApiException).message : 'Could not load.', onRetry: _reload);
          final l = s.data!;
          if (l.isEmpty) return const Center(child: Text('No leave requests yet.'));
          return RefreshIndicator(
            onRefresh: () async => _reload(),
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 90),
              itemCount: l.length,
              itemBuilder: (c, i) {
                final x = l[i];
                return Card(
                  child: ListTile(
                    title: Text('${x.type} · ${x.days} day${x.days == 1 ? '' : 's'}'),
                    subtitle: Text('${DateFormat('d MMM').format(DateTime.parse(x.from))} – ${DateFormat('d MMM yyyy').format(DateTime.parse(x.to))}${x.reviewNote != null ? '\n“${x.reviewNote}”' : ''}'),
                    isThreeLine: x.reviewNote != null,
                    trailing: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                      StatusPill(x.status),
                      if (x.status == 'Pending' || x.status == 'Approved')
                        GestureDetector(
                          onTap: () async {
                            try {
                              await context.read<AppState>().api.cancelLeave(x.id);
                              _reload();
                            } on ApiException catch (e) {
                              if (context.mounted) toast(context, e.message, error: true);
                            }
                          },
                          child: const Padding(padding: EdgeInsets.only(top: 4), child: Text('Cancel', style: TextStyle(fontSize: 12, color: Colors.red))),
                        ),
                    ]),
                  ),
                );
              },
            ),
          );
        },
      ),
    );
  }
}

class _RegList extends StatefulWidget {
  const _RegList();
  @override
  State<_RegList> createState() => _RegListState();
}

class _RegListState extends State<_RegList> {
  late Future<List<RegItem>> _f;
  @override
  void initState() {
    super.initState();
    _f = context.read<AppState>().api.regularizations();
  }

  void _reload() => setState(() {
        _f = context.read<AppState>().api.regularizations();
      });

  Future<void> _create() async {
    final api = context.read<AppState>().api;
    DateTime date = DateTime.now();
    TimeOfDay? tin, tout;
    final reason = TextEditingController();
    String fmt(TimeOfDay? t) => t == null ? '—' : '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
    final ok = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (c) => StatefulBuilder(
        builder: (c, set) => Padding(
          padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.of(c).viewInsets.bottom + 20),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Text('Request an attendance correction', style: Theme.of(c).textTheme.titleLarge),
            const SizedBox(height: 4),
            const Text('Forgot to sign out, or your phone died? Tell your administrator what the times should be.', style: TextStyle(fontSize: 13)),
            const SizedBox(height: 14),
            OutlinedButton.icon(icon: const Icon(Icons.calendar_today, size: 16), label: Text(DateFormat('EEE, d MMM yyyy').format(date)), onPressed: () async {
              final p = await showDatePicker(context: c, initialDate: date, firstDate: DateTime.now().subtract(const Duration(days: 31)), lastDate: DateTime.now());
              if (p != null) set(() => date = p);
            }),
            const SizedBox(height: 10),
            Row(children: [
              Expanded(child: OutlinedButton(onPressed: () async {
                final t = await showTimePicker(context: c, initialTime: tin ?? const TimeOfDay(hour: 9, minute: 0));
                if (t != null) set(() => tin = t);
              }, child: Text('Sign in: ${fmt(tin)}'))),
              const SizedBox(width: 10),
              Expanded(child: OutlinedButton(onPressed: () async {
                final t = await showTimePicker(context: c, initialTime: tout ?? const TimeOfDay(hour: 17, minute: 0));
                if (t != null) set(() => tout = t);
              }, child: Text('Sign out: ${fmt(tout)}'))),
            ]),
            const SizedBox(height: 10),
            TextField(controller: reason, maxLines: 2, decoration: const InputDecoration(labelText: 'Reason')),
            const SizedBox(height: 16),
            FilledButton(onPressed: () => Navigator.pop(c, true), child: const Text('Send request')),
          ]),
        ),
      ),
    );
    if (ok != true) return;
    if (reason.text.trim().isEmpty || (tin == null && tout == null)) {
      if (mounted) toast(context, 'Add at least one time and a reason.', error: true);
      return;
    }
    try {
      await api.requestRegularization(_d(date), tin == null ? null : fmt(tin), tout == null ? null : fmt(tout), reason.text.trim());
      if (mounted) toast(context, 'Request sent');
      _reload();
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message, error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(onPressed: _create, icon: const Icon(Icons.edit_calendar), label: const Text('New request')),
      body: FutureBuilder<List<RegItem>>(
        future: _f,
        builder: (c, s) {
          if (s.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (s.hasError) return ErrorBox(s.error is ApiException ? (s.error as ApiException).message : 'Could not load.', onRetry: _reload);
          final l = s.data!;
          if (l.isEmpty) return const Center(child: Text('No correction requests.'));
          return RefreshIndicator(
            onRefresh: () async => _reload(),
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 90),
              itemCount: l.length,
              itemBuilder: (c, i) {
                final x = l[i];
                return Card(
                  child: ListTile(
                    title: Text(DateFormat('EEE, d MMM yyyy').format(DateTime.parse(x.date))),
                    subtitle: Text('In ${x.inTime ?? '—'} · Out ${x.outTime ?? '—'}\n${x.reason ?? ''}${x.reviewNote != null ? '\n“${x.reviewNote}”' : ''}'),
                    isThreeLine: true,
                    trailing: StatusPill(x.status),
                  ),
                );
              },
            ),
          );
        },
      ),
    );
  }
}
