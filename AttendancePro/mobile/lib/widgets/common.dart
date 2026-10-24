import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

void toast(BuildContext c, String msg, {bool error = false}) {
  final m = ScaffoldMessenger.of(c);
  m.hideCurrentSnackBar();
  m.showSnackBar(SnackBar(
    content: Text(msg),
    behavior: SnackBarBehavior.floating,
    backgroundColor: error ? Theme.of(c).colorScheme.error : null,
    duration: Duration(seconds: error ? 5 : 3),
  ));
}

final fmtTime = DateFormat('HH:mm');
final fmtDate = DateFormat('EEE, d MMM');
String fmtDuration(Duration d) {
  final h = d.inHours, m = d.inMinutes % 60;
  return h > 0 ? '${h}h ${m.toString().padLeft(2, '0')}m' : '${m}m';
}

String fmtHours(double h) => fmtDuration(Duration(minutes: (h * 60).round()));

Color statusColor(BuildContext c, String status) {
  switch (status) {
    case 'Present':
    case 'Approved':
    case 'Active':
      return const Color(0xFF059669);
    case 'Late':
    case 'HalfDay':
    case 'Pending':
      return const Color(0xFFD97706);
    case 'Absent':
    case 'Rejected':
    case 'Disabled':
      return const Color(0xFFDC2626);
    case 'Leave':
      return const Color(0xFF2563EB);
    case 'Holiday':
      return const Color(0xFF7C3AED);
    default:
      return Colors.grey;
  }
}

class StatusPill extends StatelessWidget {
  final String text;
  final Color? color;
  const StatusPill(this.text, {super.key, this.color});
  @override
  Widget build(BuildContext context) {
    final c = color ?? statusColor(context, text);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(color: c.withValues(alpha: .14), borderRadius: BorderRadius.circular(99)),
      child: Text(text, style: TextStyle(color: c, fontSize: 12, fontWeight: FontWeight.w600)),
    );
  }
}

class KpiTile extends StatelessWidget {
  final String value, label;
  final IconData icon;
  final Color? color;
  const KpiTile({super.key, required this.value, required this.label, required this.icon, this.color});
  @override
  Widget build(BuildContext context) {
    final c = color ?? Theme.of(context).colorScheme.primary;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Icon(icon, color: c, size: 20),
          const SizedBox(height: 8),
          Text(value, style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700, color: c)),
          Text(label, style: Theme.of(context).textTheme.bodySmall),
        ]),
      ),
    );
  }
}

class ErrorBox extends StatelessWidget {
  final String message;
  final VoidCallback? onRetry;
  const ErrorBox(this.message, {super.key, this.onRetry});
  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.cloud_off, size: 40, color: Colors.grey),
            const SizedBox(height: 8),
            Text(message, textAlign: TextAlign.center),
            if (onRetry != null) TextButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh), label: const Text('Retry')),
          ]),
        ),
      );
}
