import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/api.dart';
import '../state/app_state.dart';
import '../state/home_controller.dart';
import '../widgets/common.dart';

class ProfileTab extends StatelessWidget {
  const ProfileTab({super.key});

  Future<void> _changePassword(BuildContext context) async {
    final api = context.read<AppState>().api;
    final cur = TextEditingController(), next = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Change password'),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          TextField(controller: cur, obscureText: true, decoration: const InputDecoration(labelText: 'Current password')),
          const SizedBox(height: 12),
          TextField(controller: next, obscureText: true, decoration: const InputDecoration(labelText: 'New password', helperText: '8+ characters with a number')),
        ]),
        actions: [TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Cancel')), FilledButton(style: FilledButton.styleFrom(minimumSize: const Size(80, 40)), onPressed: () => Navigator.pop(c, true), child: const Text('Update'))],
      ),
    );
    if (ok != true || !context.mounted) return;
    try {
      await api.changePassword(cur.text, next.text);
      if (context.mounted) toast(context, 'Password changed');
    } on ApiException catch (e) {
      if (context.mounted) toast(context, e.message, error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final app = context.watch<AppState>();
    final h = context.watch<HomeController>();
    final u = app.user!;
    final cs = Theme.of(context).colorScheme;
    return ListView(padding: const EdgeInsets.all(16), children: [
      Center(
        child: Column(children: [
          CircleAvatar(radius: 38, backgroundColor: cs.primaryContainer, child: Text(u.fullName.isEmpty ? '?' : u.fullName.trim()[0].toUpperCase(), style: TextStyle(fontSize: 32, color: cs.onPrimaryContainer, fontWeight: FontWeight.w700))),
          const SizedBox(height: 10),
          Text(u.fullName, style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
          Text(u.email, style: TextStyle(color: cs.onSurfaceVariant)),
          const SizedBox(height: 6),
          StatusPill(u.status),
        ]),
      ),
      const SizedBox(height: 18),
      Card(
        child: Column(children: [
          ListTile(leading: const Icon(Icons.apartment), title: Text(u.org.name), subtitle: Text('Code ${u.org.code}')),
          if (u.employeeCode != null) ListTile(leading: const Icon(Icons.badge_outlined), title: Text(u.employeeCode!), subtitle: const Text('ID / roll no.')),
          if (u.department != null) ListTile(leading: const Icon(Icons.groups_outlined), title: Text(u.department!), subtitle: Text(u.designation ?? 'Department')),
          if (h.data != null) ListTile(leading: const Icon(Icons.schedule), title: Text('${h.data!.shift.name} · ${h.data!.shift.start}–${h.data!.shift.end}'), subtitle: const Text('Your shift')),
          if (h.data != null)
            ListTile(
              leading: const Icon(Icons.place_outlined),
              title: Text(h.data!.sites.map((s) => s.name).join(', ')),
              subtitle: const Text('Where you can sign in'),
            ),
          ListTile(leading: const Icon(Icons.phone_android), title: Text(app.deviceModel ?? 'This phone'), subtitle: const Text('Linked device — ask your admin to reset it if you change phones')),
        ]),
      ),
      const SizedBox(height: 12),
      Card(
        child: Column(children: [
          ListTile(leading: const Icon(Icons.lock_outline), title: const Text('Change password'), trailing: const Icon(Icons.chevron_right), onTap: () => _changePassword(context)),
          ListTile(
            leading: const Icon(Icons.privacy_tip_outlined),
            title: const Text('Privacy'),
            subtitle: const Text('Your location is read only when this screen is open or when you sign in/out. It is shared with your organization\'s administrators and never sold.'),
          ),
          ListTile(
            leading: Icon(Icons.logout, color: cs.error),
            title: Text('Sign out', style: TextStyle(color: cs.error)),
            onTap: () async {
              if (app.queue.length > 0) {
                final go = await showDialog<bool>(
                  context: context,
                  builder: (c) => AlertDialog(
                    title: const Text('Unsynced punches'),
                    content: Text('${app.queue.length} punch(es) have not synced yet. Signing out may lose them.'),
                    actions: [TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Stay')), TextButton(onPressed: () => Navigator.pop(c, true), child: const Text('Sign out anyway'))],
                  ),
                );
                if (go != true) return;
              }
              await app.logout();
            },
          ),
        ]),
      ),
      const SizedBox(height: 16),
      Center(child: Text('Server: ${app.api.baseUrl}', style: TextStyle(fontSize: 11, color: cs.onSurfaceVariant))),
    ]);
  }
}
