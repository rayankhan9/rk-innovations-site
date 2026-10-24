import 'dart:async';

import 'package:geolocator/geolocator.dart';

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
  Stream<Position> watch() => Geolocator.getPositionStream(
      locationSettings: AndroidSettings(accuracy: LocationAccuracy.best, distanceFilter: 3, intervalDuration: const Duration(seconds: 3)));

  /// A fresh, high-accuracy fix taken at the moment of the punch.
  Future<Position> fresh() => Geolocator.getCurrentPosition(
      locationSettings: AndroidSettings(accuracy: LocationAccuracy.best, timeLimit: const Duration(seconds: 20)));

  Future<void> openAppSettings() => Geolocator.openAppSettings();
  Future<void> openLocationSettings() => Geolocator.openLocationSettings();
}
