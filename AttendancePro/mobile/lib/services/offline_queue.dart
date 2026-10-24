import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';
import '../models.dart';

/// Punches captured with no connectivity. They keep the device timestamp and are replayed (idempotently,
/// via eventId) when the network returns. The server re-validates location, time window and device.
class OfflineQueue {
  static const _key = 'offline_punches';
  final SharedPreferences _prefs;
  OfflineQueue(this._prefs);

  List<Map<String, dynamic>> all() {
    final raw = _prefs.getString(_key);
    if (raw == null) return [];
    return List<Map<String, dynamic>>.from(jsonDecode(raw));
  }

  int get length => all().length;

  Future<void> add(Map<String, dynamic> punch) async {
    final l = all()..add(punch);
    await _prefs.setString(_key, jsonEncode(l));
  }

  Future<void> removeWhere(Set<String> eventIds) async {
    final l = all()..removeWhere((p) => eventIds.contains(p['eventId']));
    await _prefs.setString(_key, jsonEncode(l));
  }

  /// The most recent queued punch decides what the UI should show before sync ("in" → currently signed in).
  String? get lastKind {
    final l = all();
    return l.isEmpty ? null : l.last['kind'] as String;
  }

  DateTime? get lastTime {
    final l = all();
    return l.isEmpty ? null : OrgClock.toOrg(DateTime.parse(l.last['clientTimeUtc']));
  }
}
