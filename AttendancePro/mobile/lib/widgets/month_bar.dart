import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models.dart';

class MonthBar extends StatelessWidget {
  final DateTime month;
  final ValueChanged<DateTime> onChanged;
  const MonthBar({super.key, required this.month, required this.onChanged});

  @override
  Widget build(BuildContext context) {
    final now = OrgClock.now();
    final isCurrent = month.year == now.year && month.month == now.month;
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 8, 8, 0),
      child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
        IconButton(icon: const Icon(Icons.chevron_left), onPressed: () => onChanged(DateTime(month.year, month.month - 1))),
        SizedBox(width: 150, child: Text(DateFormat('MMMM yyyy').format(month), textAlign: TextAlign.center, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600))),
        IconButton(icon: const Icon(Icons.chevron_right), onPressed: isCurrent ? null : () => onChanged(DateTime(month.year, month.month + 1))),
      ]),
    );
  }
}
