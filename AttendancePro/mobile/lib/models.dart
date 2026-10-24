import 'dart:math' as math;

import 'package:latlong2/latlong.dart';

/// Times are shown in the organization's time zone (not the phone's), corrected by the server's clock.
class OrgClock {
  static Duration offset = Duration.zero; // org UTC offset
  static Duration skew = Duration.zero; // server time minus device time
  static DateTime toOrg(DateTime utc) => utc.toUtc().add(offset);
  static DateTime now() => DateTime.now().toUtc().add(skew).add(offset);
}

DateTime? _dt(dynamic v) => v == null ? null : OrgClock.toOrg(DateTime.parse(v as String));
double _d(dynamic v) => (v as num?)?.toDouble() ?? 0;
int _i(dynamic v) => (v as num?)?.toInt() ?? 0;

class OrgInfo {
  final String name, type, code, memberLabel, timeZone;
  OrgInfo({required this.name, required this.type, required this.code, required this.memberLabel, required this.timeZone});
  factory OrgInfo.fromJson(Map<String, dynamic> j) {
    OrgClock.offset = Duration(minutes: _i(j['utcOffsetMinutes']));
    return _orgFrom(j);
  }

  static OrgInfo _orgFrom(Map<String, dynamic> j) => OrgInfo(
      name: j['name'] ?? '', type: j['type'] ?? '', code: j['code'] ?? '', memberLabel: j['memberLabel'] ?? 'Member', timeZone: j['timeZone'] ?? 'UTC');
}

class UserProfile {
  final String id, fullName, email, status;
  final String? employeeCode, department, designation;
  final OrgInfo org;
  UserProfile({required this.id, required this.fullName, required this.email, required this.status, this.employeeCode, this.department, this.designation, required this.org});
  bool get isPending => status == 'Pending';
  factory UserProfile.fromJson(Map<String, dynamic> j) => UserProfile(
      id: j['id'], fullName: j['fullName'] ?? '', email: j['email'] ?? '', status: j['status'] ?? 'Active',
      employeeCode: j['employeeCode'], department: j['department'], designation: j['designation'], org: OrgInfo.fromJson(j['org'] as Map<String, dynamic>));
}

class Policy {
  final int maxAccuracyMeters, geofenceToleranceMeters;
  final bool requireSelfie, blockMockLocation;
  Policy({this.maxAccuracyMeters = 50, this.geofenceToleranceMeters = 0, this.requireSelfie = false, this.blockMockLocation = true});
  factory Policy.fromJson(Map<String, dynamic> j) => Policy(
      maxAccuracyMeters: _i(j['maxAccuracyMeters']), geofenceToleranceMeters: _i(j['geofenceToleranceMeters']),
      requireSelfie: j['requireSelfie'] == true, blockMockLocation: j['blockMockLocation'] != false);
}

class ShiftInfo {
  final String name, start, end;
  final int graceMinutes;
  final List<int> workDays;
  ShiftInfo({required this.name, required this.start, required this.end, required this.graceMinutes, required this.workDays});
  factory ShiftInfo.fromJson(Map<String, dynamic> j) => ShiftInfo(
      name: j['name'] ?? '', start: j['start'] ?? '', end: j['end'] ?? '', graceMinutes: _i(j['graceMinutes']),
      workDays: (j['workDays'] as List? ?? []).map((e) => _i(e)).toList());
}

/// A permitted attendance area. Distances mirror the server's maths so the UI can
/// show "inside / 42 m away" instantly — the server still makes the final decision.
class GeoSite {
  final int id;
  final String name;
  final String? address;
  final bool isPolygon;
  final LatLng center;
  final double radius;
  final List<LatLng> polygon;
  GeoSite({required this.id, required this.name, this.address, required this.isPolygon, required this.center, required this.radius, required this.polygon});

  factory GeoSite.fromJson(Map<String, dynamic> j) => GeoSite(
        id: _i(j['id']), name: j['name'] ?? '', address: j['address'], isPolygon: j['shape'] == 'Polygon',
        center: LatLng(_d(j['lat']), _d(j['lng'])), radius: _d(j['radius']),
        polygon: (j['polygon'] as List? ?? []).map((p) => LatLng(_d(p[0]), _d(p[1]))).toList(),
      );

  /// Metres from the boundary: negative = inside, positive = outside.
  double signedDistance(LatLng p) {
    if (!isPolygon || polygon.length < 3) return const Distance().as(LengthUnit.Meter, center, p) - radius;
    final edge = _edgeDistance(p);
    return _inside(p) ? -edge : edge;
  }

  bool _inside(LatLng p) {
    var inside = false;
    for (var i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
      final a = polygon[i], b = polygon[j];
      if ((a.latitude > p.latitude) != (b.latitude > p.latitude) &&
          p.longitude < (b.longitude - a.longitude) * (p.latitude - a.latitude) / (b.latitude - a.latitude) + a.longitude) {
        inside = !inside;
      }
    }
    return inside;
  }

  double _edgeDistance(LatLng p) {
    const r = 6371000.0;
    final cosLat = math.cos(p.latitude * math.pi / 180);
    math.Point<double> proj(LatLng q) => math.Point((q.longitude - p.longitude) * math.pi / 180 * cosLat * r, (q.latitude - p.latitude) * math.pi / 180 * r);
    var best = double.infinity;
    for (var i = 0; i < polygon.length; i++) {
      final a = proj(polygon[i]), b = proj(polygon[(i + 1) % polygon.length]);
      final dx = b.x - a.x, dy = b.y - a.y;
      final len2 = dx * dx + dy * dy;
      final t = len2 == 0 ? 0.0 : (-(a.x * dx + a.y * dy) / len2).clamp(0.0, 1.0);
      best = math.min(best, math.sqrt(math.pow(a.x + t * dx, 2) + math.pow(a.y + t * dy, 2)));
    }
    return best;
  }
}

class OpenSession {
  final DateTime checkIn;
  final String? siteName;
  OpenSession(this.checkIn, this.siteName);
}

class TodaySession {
  final DateTime checkIn;
  final DateTime? checkOut;
  final bool late;
  TodaySession(this.checkIn, this.checkOut, this.late);
}

class HomeData {
  final UserProfile user;
  final Policy policy;
  final ShiftInfo shift;
  final List<GeoSite> sites;
  final OpenSession? open;
  final double todayHours;
  final List<TodaySession> sessions;
  final Duration clockSkew; // server time − device time
  HomeData({required this.user, required this.policy, required this.shift, required this.sites, this.open, required this.todayHours, required this.sessions, required this.clockSkew});

  factory HomeData.fromJson(Map<String, dynamic> j) {
    final server = DateTime.parse(j['serverTimeUtc']).toUtc();
    final user = UserProfile.fromJson(j['user']); // sets the org time offset before any time is parsed
    OrgClock.skew = server.difference(DateTime.now().toUtc());
    final o = j['openSession'] as Map<String, dynamic>?;
    final t = j['today'] as Map<String, dynamic>;
    return HomeData(
      user: user,
      policy: Policy.fromJson(j['policy']),
      shift: ShiftInfo.fromJson(j['shift']),
      sites: (j['sites'] as List).map((s) => GeoSite.fromJson(s)).toList(),
      open: o == null ? null : OpenSession(_dt(o['checkInUtc'])!, o['siteName']),
      todayHours: _d(t['hours']),
      sessions: (t['sessions'] as List).map((s) => TodaySession(_dt(s['checkInUtc'])!, _dt(s['checkOutUtc']), s['isLate'] == true)).toList(),
      clockSkew: server.difference(DateTime.now().toUtc()),
    );
  }
}

class PunchOutcome {
  final bool ok;
  final String code, message;
  final bool queued;
  PunchOutcome({required this.ok, required this.code, required this.message, this.queued = false});
  factory PunchOutcome.fromJson(Map<String, dynamic> j) => PunchOutcome(ok: j['ok'] == true, code: j['code'] ?? '', message: j['message'] ?? '');
}

class DayStat {
  final DateTime date;
  final String status;
  final double hours;
  final int lateMinutes;
  final String? note;
  DayStat(this.date, this.status, this.hours, this.lateMinutes, this.note);
  factory DayStat.fromJson(Map<String, dynamic> j) => DayStat(DateTime.parse(j['date']), j['status'], _d(j['hours']), _i(j['lateMinutes']), j['note']);
}

class MonthStats {
  final int year, month, workingDays, presentDays, lateDays, halfDays, absentDays, leaveDays, holidays, streak, lateMinutes;
  final double totalHours, avgHours, attendancePct, punctualityPct;
  final List<DayStat> days;
  MonthStats.fromJson(Map<String, dynamic> j)
      : year = _i(j['year']), month = _i(j['month']), workingDays = _i(j['workingDays']), presentDays = _i(j['presentDays']),
        lateDays = _i(j['lateDays']), halfDays = _i(j['halfDays']), absentDays = _i(j['absentDays']), leaveDays = _i(j['leaveDays']),
        holidays = _i(j['holidays']), streak = _i(j['currentStreak']), lateMinutes = _i(j['totalLateMinutes']),
        totalHours = _d(j['totalHours']), avgHours = _d(j['avgHoursPerDay']), attendancePct = _d(j['attendancePercent']),
        punctualityPct = _d(j['punctualityPercent']), days = (j['days'] as List).map((d) => DayStat.fromJson(d)).toList();
}

class HistoryItem {
  final DateTime checkIn;
  final DateTime? checkOut;
  final String? site, note;
  final double hours;
  final bool late, offline;
  final int lateMinutes;
  final String source;
  HistoryItem.fromJson(Map<String, dynamic> j)
      : checkIn = _dt(j['checkInUtc'])!, checkOut = _dt(j['checkOutUtc']), site = j['site'], note = j['note'], hours = _d(j['hours']),
        late = j['isLate'] == true, offline = j['isOfflineSync'] == true, lateMinutes = _i(j['lateMinutes']), source = j['source'] ?? 'Mobile';
}

class LeaveItem {
  final int id, days;
  final String type, from, to, status;
  final String? reason, reviewNote;
  LeaveItem.fromJson(Map<String, dynamic> j)
      : id = _i(j['id']), days = _i(j['days']), type = j['type'], from = j['from'], to = j['to'], status = j['status'], reason = j['reason'], reviewNote = j['reviewNote'];
}

class RegItem {
  final int id;
  final String date, status;
  final String? inTime, outTime, reason, reviewNote;
  RegItem.fromJson(Map<String, dynamic> j)
      : id = _i(j['id']), date = j['date'], status = j['status'], inTime = j['inTime'], outTime = j['outTime'], reason = j['reason'], reviewNote = j['reviewNote'];
}
