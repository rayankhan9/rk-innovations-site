import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models.dart';
import '../services/api.dart';
import '../state/app_state.dart';
import '../widgets/common.dart';
import '../widgets/month_bar.dart';

/// The member's own statistics for a chosen month.
class StatsTab extends StatefulWidget {
  const StatsTab({super.key});
  @override
  State<StatsTab> createState() => _StatsTabState();
}

class _StatsTabState extends State<StatsTab> {
  DateTime _month = DateTime(OrgClock.now().year, OrgClock.now().month);
  late Future<MonthStats> _f;

  @override
  void initState() {
    super.initState();
    _f = _load();
  }

  Future<MonthStats> _load() => context.read<AppState>().api.stats(_month.year, _month.month);
  void _go(DateTime m) => setState(() {
        _month = m;
        _f = _load();
      });

  @override
  Widget build(BuildContext context) {
    return Column(children: [
      MonthBar(month: _month, onChanged: _go),
      Expanded(
        child: FutureBuilder<MonthStats>(
          future: _f,
          builder: (c, s) {
            if (s.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
            if (s.hasError) return ErrorBox(s.error is ApiException ? (s.error as ApiException).message : 'Could not load stats.', onRetry: () => _go(_month));
            return RefreshIndicator(onRefresh: () async => _go(_month), child: _Body(s: s.data!));
          },
        ),
      ),
    ]);
  }
}

class _Body extends StatelessWidget {
  final MonthStats s;
  const _Body({required this.s});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final pctColor = s.attendancePct >= 90 ? const Color(0xFF059669) : s.attendancePct >= 75 ? const Color(0xFFD97706) : const Color(0xFFDC2626);
    return ListView(padding: const EdgeInsets.all(16), children: [
      Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(children: [
            SizedBox(
              width: 96,
              height: 96,
              child: Stack(alignment: Alignment.center, children: [
                SizedBox.expand(child: CircularProgressIndicator(value: (s.attendancePct / 100).clamp(0, 1), strokeWidth: 9, color: pctColor, backgroundColor: pctColor.withValues(alpha: .15))),
                Text('${s.attendancePct.toStringAsFixed(s.attendancePct % 1 == 0 ? 0 : 1)}%', style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800, color: pctColor)),
              ]),
            ),
            const SizedBox(width: 18),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('Attendance', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600)),
                const SizedBox(height: 4),
                Text('${s.presentDays} present of ${s.workingDays} working days', style: TextStyle(color: cs.onSurfaceVariant)),
                const SizedBox(height: 4),
                Text('Punctuality ${s.punctualityPct.toStringAsFixed(0)}%', style: TextStyle(color: cs.onSurfaceVariant)),
                if (s.streak > 1) Padding(padding: const EdgeInsets.only(top: 6), child: Text('🔥 ${s.streak}-day streak', style: const TextStyle(fontWeight: FontWeight.w600))),
              ]),
            ),
          ]),
        ),
      ),
      const SizedBox(height: 8),
      GridView.count(
        crossAxisCount: 2,
        shrinkWrap: true,
        physics: const NeverScrollableScrollPhysics(),
        childAspectRatio: 1.25,
        mainAxisSpacing: 8,
        crossAxisSpacing: 8,
        children: [
          KpiTile(value: fmtHours(s.totalHours), label: 'Hours worked', icon: Icons.schedule),
          KpiTile(value: fmtHours(s.avgHours), label: 'Average per day', icon: Icons.timelapse),
          KpiTile(value: '${s.lateDays}', label: s.lateMinutes > 0 ? 'Late (${s.lateMinutes} min total)' : 'Late arrivals', icon: Icons.alarm, color: const Color(0xFFD97706)),
          KpiTile(value: '${s.absentDays}', label: 'Absent days', icon: Icons.event_busy, color: const Color(0xFFDC2626)),
          KpiTile(value: '${s.leaveDays}', label: 'Leave days', icon: Icons.beach_access, color: const Color(0xFF2563EB)),
          KpiTile(value: '${s.halfDays}', label: 'Half days', icon: Icons.contrast, color: const Color(0xFF7C3AED)),
        ],
      ),
      const SizedBox(height: 8),
      Card(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(12, 16, 16, 8),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Padding(padding: const EdgeInsets.only(left: 4, bottom: 12), child: Text('Hours per day', style: Theme.of(context).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600))),
            SizedBox(height: 160, child: _HoursChart(s: s)),
          ]),
        ),
      ),
      const SizedBox(height: 8),
      _CalendarCard(s: s),
    ]);
  }
}

class _HoursChart extends StatelessWidget {
  final MonthStats s;
  const _HoursChart({required this.s});
  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return BarChart(BarChartData(
      gridData: FlGridData(drawVerticalLine: false, horizontalInterval: 4, getDrawingHorizontalLine: (_) => FlLine(color: cs.outlineVariant.withValues(alpha: .4), strokeWidth: 1)),
      borderData: FlBorderData(show: false),
      titlesData: FlTitlesData(
        topTitles: const AxisTitles(),
        rightTitles: const AxisTitles(),
        leftTitles: AxisTitles(sideTitles: SideTitles(showTitles: true, reservedSize: 28, interval: 4, getTitlesWidget: (v, m) => Text('${v.toInt()}h', style: const TextStyle(fontSize: 10)))),
        bottomTitles: AxisTitles(sideTitles: SideTitles(showTitles: true, interval: 1, getTitlesWidget: (v, m) => (v.toInt() == 1 || v.toInt() % 5 == 0) ? Text('${v.toInt()}', style: const TextStyle(fontSize: 10)) : const SizedBox.shrink())),
      ),
      barGroups: [
        for (final d in s.days)
          BarChartGroupData(x: d.date.day, barRods: [
            BarChartRodData(
              toY: d.hours.clamp(0, 14).toDouble(),
              width: 5,
              borderRadius: BorderRadius.circular(2),
              color: d.status == 'Late' ? const Color(0xFFD97706) : d.status == 'HalfDay' ? const Color(0xFF7C3AED) : cs.primary,
            ),
          ]),
      ],
    ));
  }
}

class _CalendarCard extends StatelessWidget {
  final MonthStats s;
  const _CalendarCard({required this.s});

  static const _colors = {
    'Present': Color(0xFF34D399),
    'Late': Color(0xFFFBBF24),
    'HalfDay': Color(0xFFA78BFA),
    'Absent': Color(0xFFF87171),
    'Leave': Color(0xFF60A5FA),
    'Holiday': Color(0xFFC4B5FD),
  };

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final first = DateTime(s.year, s.month, 1);
    final offset = (first.weekday + 6) % 7; // Monday first
    final cells = <Widget>[
      for (final d in ['M', 'T', 'W', 'T', 'F', 'S', 'S']) Center(child: Text(d, style: TextStyle(fontSize: 11, color: cs.onSurfaceVariant))),
      for (var i = 0; i < offset; i++) const SizedBox(),
      for (final d in s.days)
        Container(
          margin: const EdgeInsets.all(2),
          decoration: BoxDecoration(color: _colors[d.status] ?? cs.surfaceContainerHighest.withValues(alpha: .6), borderRadius: BorderRadius.circular(8)),
          alignment: Alignment.center,
          child: Text('${d.date.day}', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: _colors.containsKey(d.status) ? Colors.black87 : cs.onSurfaceVariant)),
        ),
    ];
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('Calendar', style: Theme.of(context).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600)),
          const SizedBox(height: 8),
          GridView.count(crossAxisCount: 7, shrinkWrap: true, physics: const NeverScrollableScrollPhysics(), childAspectRatio: 1.15, children: cells),
          const SizedBox(height: 8),
          Wrap(spacing: 12, runSpacing: 4, children: [
            for (final e in {'Present': 'Present', 'Late': 'Late', 'HalfDay': 'Half day', 'Absent': 'Absent', 'Leave': 'Leave', 'Holiday': 'Holiday'}.entries)
              Row(mainAxisSize: MainAxisSize.min, children: [
                Container(width: 10, height: 10, decoration: BoxDecoration(color: _colors[e.key], borderRadius: BorderRadius.circular(3))),
                const SizedBox(width: 4),
                Text(e.value, style: const TextStyle(fontSize: 11)),
              ]),
          ]),
        ]),
      ),
    );
  }
}
