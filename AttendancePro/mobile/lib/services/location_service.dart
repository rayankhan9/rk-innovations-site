import 'dart:async';

import 'package:geolocator/geolocator.dart';

import '../config.dart';

enum LocationIssue { none, serviceOff, denied, deniedForever }

class LocationService {
  /// Checks service + permission, requesting it if needed.
  Future<LocationIssue> ensure({bool request = true}) async {
    if (!await Geolocator.isLocationServiceEnabled()) return LocationIssue.serviceOff;
    var p = await Geolocator.checkPermission();
    if (p == LocationPermission.denied && request) p = await Geolocator.requestPermission();
    if (p == LocationPermission.deniedForever) return LocationIssue.deniedForever;
    if (p == LocationPermission.denied) return LocationIssue.denied;
    return LocationIssue.none;
  }

  /// Continuous high-accuracy updates for the live "inside / outside" indicator.
  Position _demo() => Position(
      latitude: AppConfig.demoLat, longitude: AppConfig.demoLng, timestamp: DateTime.now(), accuracy: 6, altitude: 0, altitudeAccuracy: 1,
      heading: 0, headingAccuracy: 1, speed: 0, speedAccuracy: 1);

  Stream<Position> watch() => AppConfig.demoLocation ? Stream.periodic(const Duration(seconds: 2), (_) => _demo()) : _real();

  Stream<Position> _real() => Geolocator.getPositionStream(
      locationSettings: AndroidSettings(accuracy: LocationAccuracy.best, distanceFilter: 3, intervalDuration: const Duration(seconds: 3)));

  /// A fresh, high-accuracy fix taken at the moment of the punch.
  Future<Position> fresh() async => AppConfig.demoLocation ? _demo() : await _fresh();

  Future<Position> _fresh() => Geolocator.getCurrentPosition(
      locationSettings: AndroidSettings(accuracy: LocationAccuracy.best, timeLimit: const Duration(seconds: 20)));

  Future<void> openAppSettings() => Geolocator.openAppSettings();
  Future<void> openLocationSettings() => Geolocator.openLocationSettings();
}
