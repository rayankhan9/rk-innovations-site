import 'package:attendance_pro/models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:latlong2/latlong.dart';

void main() {
  final circle = GeoSite(id: 1, name: 'HQ', isPolygon: false, center: const LatLng(28.6139, 77.2090), radius: 100, polygon: const []);
  final poly = GeoSite(id: 2, name: 'Campus', isPolygon: true, center: const LatLng(28.614, 77.209), radius: 0, polygon: const [
    LatLng(28.6130, 77.2080),
    LatLng(28.6130, 77.2100),
    LatLng(28.6150, 77.2100),
    LatLng(28.6150, 77.2080),
  ]);

  test('circle: inside is negative, outside positive and roughly right', () {
    expect(circle.signedDistance(const LatLng(28.6139, 77.2090)), lessThan(0));
    final d = circle.signedDistance(const LatLng(28.6200, 77.2090)); // ~680 m north, 100 m radius
    expect(d, inInclusiveRange(550, 650));
  });

  test('polygon: inside / outside / distance to the nearest edge', () {
    expect(poly.signedDistance(const LatLng(28.6140, 77.2090)), lessThan(0));
    final out = poly.signedDistance(const LatLng(28.6160, 77.2090)); // ~110 m above the top edge
    expect(out, inInclusiveRange(90, 130));
  });

  test('HomeData parses the server payload', () {
    final h = HomeData.fromJson({
      'serverTimeUtc': '2026-10-23T05:00:00Z',
      'user': {'id': 'u', 'fullName': 'A B', 'email': 'a@b.c', 'status': 'Active', 'org': {'name': 'Org', 'type': 'Company', 'code': 'X', 'memberLabel': 'Employee', 'timeZone': 'UTC'}},
      'policy': {'maxAccuracyMeters': 50, 'requireSelfie': false, 'blockMockLocation': true, 'geofenceToleranceMeters': 0},
      'shift': {'name': 'G', 'start': '09:00', 'end': '17:00', 'graceMinutes': 10, 'workDays': [1, 2, 3, 4, 5]},
      'sites': [
        {'id': 1, 'name': 'HQ', 'shape': 'Circle', 'lat': 1.0, 'lng': 2.0, 'radius': 100.0, 'polygon': null}
      ],
      'openSession': null,
      'today': {'date': '2026-10-23', 'hours': 1.5, 'sessions': []},
    });
    expect(h.sites.single.name, 'HQ');
    expect(h.open, isNull);
    expect(h.todayHours, 1.5);
  });
}
