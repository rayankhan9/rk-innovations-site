import 'dart:async';
import 'dart:convert';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/foundation.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';
import 'package:latlong2/latlong.dart';
import 'package:uuid/uuid.dart';

import '../models.dart';
import '../services/api.dart';
import '../services/location_service.dart';
import 'app_state.dart';

/// Drives the Today tab: live position, geofence status, punching, offline queue + sync.
class HomeController extends ChangeNotifier {
  final AppState app;
  HomeController(this.app);

  HomeData? data;
  bool offlineData = false;
  bool loading = true;
  bool busy = false;
  String? error;
  LocationIssue issue = LocationIssue.none;
  Position? pos;
  GeoSite? nearest;
  double? nearestDist; // signed metres from boundary of the closest site
  bool inside = false;
  int pending = 0;

  StreamSubscription<Position>? _posSub;
  StreamSubscription<List<ConnectivityResult>>? _netSub;
  Timer? _tick;
  DateTime now = DateTime.now();

  Future<void> start() async {
    pending = app.queue.length;
    await load();
    await startLocation();
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      now = DateTime.now();
      if (app.queue.length > 0) syncPending(); // retry queued punches even if the network never "changed"
      notifyListeners();
    });
    _netSub = Connectivity().onConnectivityChanged.listen((r) {
      if (r.any((c) => c != ConnectivityResult.none)) {
        syncPending();
        load(silent: true);
      }
    });
    syncPending();
  }

  Future<void> load({bool silent = false}) async {
    if (!silent) {
      loading = true;
      notifyListeners();
    }
    try {
      final raw = await app.api.homeRaw();
      await app.prefs.setString('home_cache', jsonEncode(raw));
      data = HomeData.fromJson(raw);
      offlineData = false;
      error = null;
      if (data!.user.status != app.user?.status) app.refreshUser();
    } on ApiException catch (e) {
      final cached = app.prefs.getString('home_cache');
      if (e.isNetwork && cached != null) {
        data = HomeData.fromJson(Map<String, dynamic>.from(jsonDecode(cached)));
        offlineData = true;
      } else {
        error = e.message;
      }
    }
    loading = false;
    _evaluate();
    notifyListeners();
  }

  Future<void> startLocation() async {
    issue = await app.location.ensure();
    notifyListeners();
    if (issue != LocationIssue.none) return;
    await _posSub?.cancel();
    _posSub = app.location.watch().listen((p) {
      pos = p;
      _evaluate();
      notifyListeners();
    }, onError: (_) {});
    try {
      pos = await Geolocator.getLastKnownPosition();
      _evaluate();
      notifyListeners();
    } catch (_) {}
  }

  void _evaluate() {
    final d = data, p = pos;
    if (d == null || p == null || d.sites.isEmpty) {
      nearest = null;
      nearestDist = null;
      inside = false;
      return;
    }
    final here = LatLng(p.latitude, p.longitude);
    GeoSite? best;
    double bestD = double.infinity;
    for (final s in d.sites) {
      final dist = s.signedDistance(here);
      if (dist < bestD) {
        bestD = dist;
        best = s;
      }
    }
    nearest = best;
    nearestDist = bestD;
    inside = bestD <= d.policy.geofenceToleranceMeters;
  }

  bool get accuracyOk => pos != null && data != null && pos!.accuracy <= data!.policy.maxAccuracyMeters;
  bool get mocked => pos?.isMocked == true;

  /// Signed in according to the server, adjusted by anything queued offline.
  bool get signedIn {
    final q = app.queue.lastKind;
    if (q != null) return q == 'in';
    return data?.open != null;
  }

  DateTime? get since => app.queue.lastKind == 'in' ? app.queue.lastTime : data?.open?.checkIn;

  Duration get elapsed => since == null ? Duration.zero : now.difference(since!);

  Future<PunchOutcome> punch({required bool checkIn}) async {
    if (busy) return PunchOutcome(ok: false, code: 'BUSY', message: 'Please wait…');
    busy = true;
    error = null;
    notifyListeners();
    try {
      final issueNow = await app.location.ensure();
      if (issueNow != LocationIssue.none) {
        issue = issueNow;
        return PunchOutcome(ok: false, code: 'LOCATION', message: 'Location access is required to sign ${checkIn ? 'in' : 'out'}.');
      }
      final Position p;
      try {
        p = await app.location.fresh();
      } catch (_) {
        return PunchOutcome(ok: false, code: 'NO_FIX', message: 'Could not get a GPS fix. Move to an open area and try again.');
      }
      pos = p;
      _evaluate();

      String? selfie;
      if (data?.policy.requireSelfie == true) {
        final shot = await ImagePicker().pickImage(source: ImageSource.camera, preferredCameraDevice: CameraDevice.front, maxWidth: 640, imageQuality: 60);
        if (shot == null) return PunchOutcome(ok: false, code: 'SELFIE', message: 'A selfie is required to continue.');
        selfie = base64Encode(await shot.readAsBytes());
      }

      final body = <String, dynamic>{
        'lat': p.latitude,
        'lng': p.longitude,
        'accuracy': p.accuracy,
        'isMock': p.isMocked,
        'clientTimeUtc': DateTime.now().toUtc().toIso8601String(),
        'eventId': const Uuid().v4(),
        'deviceId': app.deviceId,
        'deviceModel': app.deviceModel,
        'selfieBase64': selfie,
        'kind': checkIn ? 'in' : 'out',
      };

      try {
        final r = await app.api.punch(checkIn, body);
        if (r.ok) await load(silent: true);
        return r;
      } on ApiException catch (e) {
        if (!e.isNetwork) rethrow;
        // Offline: only queue if our own check says we're inside, to avoid piling up doomed punches.
        if (!inside || !accuracyOk) {
          return PunchOutcome(ok: false, code: 'OFFLINE', message: 'No connection, and you don\'t appear to be inside the allowed area.');
        }
        await app.queue.add(body);
        pending = app.queue.length;
        return PunchOutcome(ok: true, queued: true, code: 'QUEUED', message: 'No internet. Saved on this phone — it will sync automatically.');
      }
    } on ApiException catch (e) {
      return PunchOutcome(ok: false, code: e.code, message: e.message);
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  bool _syncing = false;

  /// Replays queued punches. Every result is final: accepted *and* rejected events leave the queue.
  Future<String?> syncPending() async {
    if (_syncing || app.queue.length == 0) return null;
    _syncing = true;
    try {
      final batch = app.queue.all();
      final res = await app.api.sync(batch);
      await app.queue.removeWhere(res.keys.toSet());
      pending = app.queue.length;
      final rejected = res.values.where((r) => !r.ok).toList();
      await load(silent: true);
      if (rejected.isNotEmpty) return '${rejected.length} offline punch(es) were rejected: ${rejected.first.message}';
      return null;
    } on ApiException {
      return null; // still offline; try again later
    } finally {
      _syncing = false;
      notifyListeners();
    }
  }

  @override
  void dispose() {
    _posSub?.cancel();
    _netSub?.cancel();
    _tick?.cancel();
    super.dispose();
  }
}
