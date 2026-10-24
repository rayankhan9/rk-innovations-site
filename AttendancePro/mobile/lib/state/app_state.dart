import 'dart:convert';

import 'package:device_info_plus/device_info_plus.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

import '../config.dart';
import '../models.dart';
import '../services/api.dart';
import '../services/location_service.dart';
import '../services/offline_queue.dart';

enum Phase { loading, signedOut, ready }

/// Session + shared services. Screens read this via Provider.
class AppState extends ChangeNotifier {
  final api = ApiClient();
  final location = LocationService();
  late OfflineQueue queue;
  late SharedPreferences prefs;
  Phase phase = Phase.loading;
  UserProfile? user;
  late String deviceId;
  String? deviceModel;

  Future<void> init() async {
    prefs = await SharedPreferences.getInstance();
    queue = OfflineQueue(prefs);
    await api.init();
    api.onSessionExpired = () {
      user = null;
      phase = Phase.signedOut;
      notifyListeners();
    };
    const secure = FlutterSecureStorage();
    var id = await secure.read(key: 'device_id');
    if (id == null) {
      id = const Uuid().v4();
      await secure.write(key: 'device_id', value: id);
    }
    deviceId = id;
    try {
      final info = await DeviceInfoPlugin().androidInfo;
      deviceModel = '${info.manufacturer} ${info.model}';
    } catch (_) {}
    if (AppConfig.demoDevice.isNotEmpty) deviceModel = AppConfig.demoDevice;

    if (api.hasSession) {
      try {
        user = await api.me();
        phase = Phase.ready;
      } on ApiException catch (e) {
        // Offline start: trust the stored session; the server re-validates every call.
        if (e.isNetwork && prefs.getString('home_cache') != null) {
          user = HomeData.fromJson(Map<String, dynamic>.from(jsonDecode(prefs.getString('home_cache')!))).user;
          phase = Phase.ready;
        } else {
          await api.clearSession();
          phase = Phase.signedOut;
        }
      }
    } else {
      phase = Phase.signedOut;
    }
    notifyListeners();
  }

  Future<void> login(String email, String password) async {
    user = await api.login(email, password, deviceId, deviceModel);
    phase = Phase.ready;
    notifyListeners();
  }

  Future<void> register(Map<String, dynamic> body) async {
    user = await api.register({...body, 'deviceId': deviceId, 'deviceModel': deviceModel});
    phase = Phase.ready;
    notifyListeners();
  }

  Future<void> refreshUser() async {
    try {
      user = await api.me();
      notifyListeners();
    } catch (_) {}
  }

  Future<void> logout() async {
    await api.logout();
    await prefs.remove('home_cache');
    user = null;
    phase = Phase.signedOut;
    notifyListeners();
  }
}
