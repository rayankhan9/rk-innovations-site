import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

import '../config.dart';
import '../services/location_service.dart';
import '../state/app_state.dart';
import '../state/home_controller.dart';
import '../widgets/common.dart';

class TodayTab extends StatefulWidget {
  const TodayTab({super.key});
  @override
  State<TodayTab> createState() => _TodayTabState();
}

class _TodayTabState extends State<TodayTab> {
  final _map = MapController();
  bool _mapReady = false, _followed = false;

  Future<void> _punch(HomeController h, bool checkIn) async {
    if (!checkIn) {
      final ok = await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: const Text('Sign out now?'),
          content: Text('Current session: ${fmtDuration(h.elapsed)}.'),
          actions: [TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Cancel')), FilledButton(style: FilledButton.styleFrom(minimumSize: const Size(100, 40)), onPressed: () => Navigator.pop(c, true), child: const Text('Sign out'))],
        ),
      );
      if (ok != true || !mounted) return;
    }
    HapticFeedback.mediumImpact();
    final r = await h.punch(checkIn: checkIn);
    if (!mounted) return;
    if (r.ok) HapticFeedback.heavyImpact();
    toast(context, r.message, error: !r.ok);
    if (r.code == 'DEVICE_MISMATCH' || r.code == 'ACCOUNT_INACTIVE') context.read<AppState>().refreshUser();
  }

  LatLng? _focus(HomeController h) {
    if (h.pos != null) return LatLng(h.pos!.latitude, h.pos!.longitude);
    return h.data?.sites.isNotEmpty == true ? h.data!.sites.first.center : null;
  }

  @override
  Widget build(BuildContext context) {
    final h = context.watch<HomeController>();
    final app = context.watch<AppState>();
    final cs = Theme.of(context).colorScheme;

    if (h.loading && h.data == null) return const Center(child: CircularProgressIndicator());
    if (h.data == null) return ErrorBox(h.error ?? 'Could not load.', onRetry: h.load);
    final d = h.data!;

    // Follow the first GPS fix once the map is ready.
    if (_mapReady && !_followed && h.pos != null) {
      _followed = true;
      WidgetsBinding.instance.addPostFrameCallback((_) => _map.move(LatLng(h.pos!.latitude, h.pos!.longitude), 17));
    }

    return RefreshIndicator(
      onRefresh: () async {
        await h.load(silent: true);
        await h.syncPending();
        await app.refreshUser();
      },
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Row(children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('Hi, ${d.user.fullName.split(' ').first}', style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700)),
                Text('${d.user.org.name} · ${fmtDate.format(h.now)}', style: TextStyle(color: cs.onSurfaceVariant)),
              ]),
            ),
          ]),
          const SizedBox(height: 12),
          if (app.user?.isPending == true) _Banner(icon: Icons.hourglass_top, color: Colors.orange, text: 'Your account is waiting for administrator approval. Pull down to refresh once approved.'),
          if (h.offlineData) _Banner(icon: Icons.cloud_off, color: Colors.blueGrey, text: 'Offline — showing saved locations. Punches are saved and synced later.'),
          if (h.pending > 0)
            _Banner(
                icon: Icons.sync,
                color: Colors.indigo,
                text: '${h.pending} punch(es) waiting to sync.',
                action: TextButton(
                    onPressed: () async {
                      final msg = await h.syncPending();
                      if (context.mounted && msg != null) toast(context, msg, error: true);
                    },
                    child: const Text('Sync now'))),
          if (d.sites.isEmpty) _Banner(icon: Icons.location_off, color: Colors.red, text: 'No attendance location is set up for you yet. Contact your administrator.'),
          // ---- map
          ClipRRect(
            borderRadius: BorderRadius.circular(14),
            child: SizedBox(
              height: 250,
              child: Stack(children: [
                FlutterMap(
                  mapController: _map,
                  options: MapOptions(
                    initialCenter: _focus(h) ?? const LatLng(20.5937, 78.9629),
                    initialZoom: _focus(h) == null ? 4 : 16,
                    onMapReady: () => setState(() => _mapReady = true),
                  ),
                  children: [
                    TileLayer(urlTemplate: AppConfig.tileUrl, userAgentPackageName: AppConfig.userAgentPackage, maxZoom: 19),
                    PolygonLayer(polygons: [
                      for (final s in d.sites.where((s) => s.isPolygon && s.polygon.length >= 3))
                        Polygon(points: s.polygon, color: cs.primary.withValues(alpha: .18), borderColor: cs.primary, borderStrokeWidth: 2),
                    ]),
                    CircleLayer(circles: [
                      for (final s in d.sites.where((s) => !s.isPolygon))
                        CircleMarker(point: s.center, radius: s.radius, useRadiusInMeter: true, color: cs.primary.withValues(alpha: .18), borderColor: cs.primary, borderStrokeWidth: 2),
                    ]),
                    if (h.pos != null)
                      CircleLayer(circles: [
                        CircleMarker(point: LatLng(h.pos!.latitude, h.pos!.longitude), radius: h.pos!.accuracy, useRadiusInMeter: true, color: Colors.blue.withValues(alpha: .12), borderColor: Colors.blue.withValues(alpha: .4), borderStrokeWidth: 1),
                      ]),
                    if (h.pos != null)
                      MarkerLayer(markers: [
                        Marker(
                            point: LatLng(h.pos!.latitude, h.pos!.longitude),
                            width: 22,
                            height: 22,
                            child: Container(decoration: BoxDecoration(color: Colors.blue, shape: BoxShape.circle, border: Border.all(color: Colors.white, width: 3), boxShadow: const [BoxShadow(blurRadius: 4, color: Colors.black38)]))),
                      ]),
                    const RichAttributionWidget(attributions: [TextSourceAttribution('© OpenStreetMap contributors')]),
                  ],
                ),
                Positioned(
                  right: 8,
                  top: 8,
                  child: FloatingActionButton.small(
                    heroTag: 'recenter',
                    onPressed: () {
                      final f = _focus(h);
                      if (f != null) _map.move(f, 17);
                    },
                    child: const Icon(Icons.my_location),
                  ),
                ),
              ]),
            ),
          ),
          const SizedBox(height: 12),
          _StatusCard(h: h),
          const SizedBox(height: 12),
          _SessionCard(h: h),
          const SizedBox(height: 16),
          _PunchButton(h: h, onPunch: (inn) => _punch(h, inn), blocked: app.user?.isPending == true),
          const SizedBox(height: 20),
          if (d.sessions.isNotEmpty) ...[
            Text('Today', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600)),
            const SizedBox(height: 6),
            for (final s in d.sessions)
              Card(
                child: ListTile(
                  dense: true,
                  leading: Icon(s.checkOut == null ? Icons.timelapse : Icons.check_circle, color: s.checkOut == null ? Colors.orange : const Color(0xFF059669)),
                  title: Text('${fmtTime.format(s.checkIn)} → ${s.checkOut == null ? 'now' : fmtTime.format(s.checkOut!)}'),
                  subtitle: Text(fmtDuration((s.checkOut ?? DateTime.now()).difference(s.checkIn))),
                  trailing: s.late ? const StatusPill('Late') : null,
                ),
              ),
          ],
          const SizedBox(height: 8),
          Text('Shift: ${d.shift.name} · ${d.shift.start}–${d.shift.end} · ${d.shift.graceMinutes} min grace', textAlign: TextAlign.center, style: TextStyle(color: cs.onSurfaceVariant, fontSize: 12)),
        ],
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  final IconData icon;
  final Color color;
  final String text;
  final Widget? action;
  const _Banner({required this.icon, required this.color, required this.text, this.action});
  @override
  Widget build(BuildContext context) => Container(
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        decoration: BoxDecoration(color: color.withValues(alpha: .12), borderRadius: BorderRadius.circular(10)),
        child: Row(children: [
          Icon(icon, color: color, size: 20),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: const TextStyle(fontSize: 13))),
          ?action,
        ]),
      );
}

class _StatusCard extends StatelessWidget {
  final HomeController h;
  const _StatusCard({required this.h});

  @override
  Widget build(BuildContext context) {
    IconData icon;
    Color color;
    String title;
    String sub;
    Widget? action;

    if (h.issue != LocationIssue.none) {
      icon = Icons.location_disabled;
      color = Colors.red;
      switch (h.issue) {
        case LocationIssue.serviceOff:
          title = 'Location is turned off';
          sub = 'Turn on GPS to sign in or out.';
          action = TextButton(onPressed: () => context.read<AppState>().location.openLocationSettings(), child: const Text('Turn on'));
        case LocationIssue.deniedForever:
          title = 'Location permission blocked';
          sub = 'Allow location for this app in Settings.';
          action = TextButton(onPressed: () => context.read<AppState>().location.openAppSettings(), child: const Text('Open settings'));
        default:
          title = 'Location permission needed';
          sub = 'We only read your location when you sign in or out and while this screen is open.';
          action = TextButton(onPressed: h.startLocation, child: const Text('Allow'));
      }
    } else if (h.pos == null) {
      icon = Icons.gps_not_fixed;
      color = Colors.grey;
      title = 'Finding your location…';
      sub = 'Step outside or near a window for a better signal.';
    } else if (h.mocked && h.data!.policy.blockMockLocation) {
      icon = Icons.gpp_bad;
      color = Colors.red;
      title = 'Mock location detected';
      sub = 'Turn off fake-GPS apps / developer mock location to continue.';
    } else if (!h.accuracyOk) {
      icon = Icons.gps_not_fixed;
      color = Colors.orange;
      title = 'GPS signal is weak (±${h.pos!.accuracy.round()} m)';
      sub = 'Needs ±${h.data!.policy.maxAccuracyMeters} m or better. Wait a moment or move outdoors.';
    } else if (h.inside) {
      icon = Icons.check_circle;
      color = const Color(0xFF059669);
      title = 'You\'re inside ${h.nearest!.name}';
      sub = 'GPS accuracy ±${h.pos!.accuracy.round()} m';
    } else {
      icon = Icons.wrong_location;
      color = Colors.orange;
      final m = h.nearestDist!.round();
      title = '${m >= 1000 ? '${(m / 1000).toStringAsFixed(1)} km' : '$m m'} outside ${h.nearest?.name ?? 'the area'}';
      sub = 'Move inside the highlighted area to sign in or out.';
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Row(children: [
          Icon(icon, color: color, size: 30),
          const SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
            Text(sub, style: TextStyle(fontSize: 12, color: Theme.of(context).colorScheme.onSurfaceVariant)),
          ])),
          ?action,
        ]),
      ),
    );
  }
}

class _SessionCard extends StatelessWidget {
  final HomeController h;
  const _SessionCard({required this.h});
  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final inn = h.signedIn;
    final total = h.data!.todayHours;
    return Card(
      color: inn ? cs.primaryContainer : null,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(children: [
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(inn ? 'Signed in since ${h.since == null ? '—' : fmtTime.format(h.since!)}' : 'Not signed in', style: const TextStyle(fontWeight: FontWeight.w600)),
              const SizedBox(height: 2),
              Text(inn ? 'Current session ${fmtDuration(h.elapsed)}' : 'Tap the button below when you arrive', style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant)),
            ]),
          ),
          Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
            Text(fmtHours(total), style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700)),
            Text('worked today', style: TextStyle(fontSize: 11, color: cs.onSurfaceVariant)),
          ]),
        ]),
      ),
    );
  }
}

class _PunchButton extends StatelessWidget {
  final HomeController h;
  final void Function(bool checkIn) onPunch;
  final bool blocked;
  const _PunchButton({required this.h, required this.onPunch, required this.blocked});
  @override
  Widget build(BuildContext context) {
    final inn = h.signedIn;
    final ready = !blocked && !h.busy && h.issue == LocationIssue.none && h.pos != null && h.accuracyOk && h.inside && !(h.mocked && h.data!.policy.blockMockLocation);
    final color = inn ? const Color(0xFFDC2626) : Theme.of(context).colorScheme.primary;
    return SizedBox(
      height: 64,
      child: FilledButton.icon(
        style: FilledButton.styleFrom(backgroundColor: color, minimumSize: const Size.fromHeight(64), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
        onPressed: ready ? () => onPunch(!inn) : null,
        icon: h.busy ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white)) : Icon(inn ? Icons.logout : Icons.login, size: 26),
        label: Text(h.busy ? 'Verifying location…' : (inn ? 'Sign out' : 'Sign in'), style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
      ),
    );
  }
}
