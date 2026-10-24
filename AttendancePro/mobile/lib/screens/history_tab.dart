import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../models.dart';
import '../services/api.dart';
import '../state/app_state.dart';
import '../widgets/common.dart';
import '../widgets/month_bar.dart';

class HistoryTab extends StatefulWidget {
  const HistoryTab({super.key});
  @override
  State<HistoryTab> createState() => _HistoryTabState();
}

class _HistoryTabState extends State<HistoryTab> {
  DateTime _month = DateTime(OrgClock.now().year, OrgClock.now().month);
  late Future<List<HistoryItem>> _f;

  @override
  void initState() {
    super.initState();
    _f = _load();
  }

  Future<List<HistoryItem>> _load() => context.read<AppState>().api.history(_month.year, _month.month);

  void _go(DateTime m) => setState(() {
        _month = m;
        _f = _load();
      });

  @override
  Widget build(BuildContext context) {
    return Column(children: [
      MonthBar(month: _month, onChanged: _go),
      Expanded(
        child: FutureBuilder<List<HistoryItem>>(
          future: _f,
          builder: (c, s) {
            if (s.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
            if (s.hasError) return ErrorBox(s.error is ApiException ? (s.error as ApiException).message : 'Could not load history.', onRetry: () => _go(_month));
            final items = s.data!;
            if (items.isEmpty) return const Center(child: Text('No attendance recorded this month.'));
            final byDay = <String, List<HistoryItem>>{};
            for (final i in items) {
              byDay.putIfAbsent(DateFormat('yyyy-MM-dd').format(i.checkIn), () => []).add(i);
            }
            final keys = byDay.keys.toList()..sort((a, b) => b.compareTo(a));
            return RefreshIndicator(
              onRefresh: () async => _go(_month),
              child: ListView.builder(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
                itemCount: keys.length,
                itemBuilder: (c, idx) {
                  final recs = byDay[keys[idx]]!;
                  final total = recs.fold<double>(0, (a, r) => a + r.hours);
                  return Card(
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Row(children: [
                          Expanded(child: Text(fmtDate.format(DateTime.parse(keys[idx])), style: const TextStyle(fontWeight: FontWeight.w600))),
                          Text(fmtHours(total), style: const TextStyle(fontWeight: FontWeight.w600)),
                        ]),
                        const SizedBox(height: 6),
                        for (final r in recs.reversed)
                          Padding(
                            padding: const EdgeInsets.symmetric(vertical: 2),
                            child: Row(children: [
                              Text('${fmtTime.format(r.checkIn)} → ${r.checkOut == null ? '…' : fmtTime.format(r.checkOut!)}'),
                              const SizedBox(width: 8),
                              if (r.site != null) Expanded(child: Text(r.site!, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12, color: Theme.of(context).colorScheme.onSurfaceVariant))) else const Spacer(),
                              if (r.late) StatusPill('Late ${r.lateMinutes}m', color: statusColor(context, 'Late')),
                              if (r.source != 'Mobile') Padding(padding: const EdgeInsets.only(left: 4), child: StatusPill(r.source, color: Colors.grey)),
                              if (r.offline) const Padding(padding: EdgeInsets.only(left: 4), child: Icon(Icons.cloud_off, size: 14, color: Colors.grey)),
                            ]),
                          ),
                      ]),
                    ),
                  );
                },
              ),
            );
          },
        ),
      ),
    ]);
  }
}
