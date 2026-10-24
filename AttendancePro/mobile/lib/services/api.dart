import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../config.dart';
import '../models.dart';

class ApiException implements Exception {
  final int status;
  final String code, message;
  ApiException(this.status, this.code, this.message);
  bool get isNetwork => status == 0;
  @override
  String toString() => message;
}

/// Thin typed wrapper over the AttendancePro REST API. Handles JWT storage and silent refresh.
class ApiClient {
  static const _kAccess = 'access', _kRefresh = 'refresh', _kBase = 'base_url';
  final _secure = const FlutterSecureStorage();
  late final Dio _dio;
  late SharedPreferences _prefs;
  String? _access, _refresh;
  Future<bool>? _refreshing;

  /// Called when the refresh token is rejected, so the UI can return to sign-in.
  void Function()? onSessionExpired;

  String get baseUrl => _prefs.getString(_kBase) ?? AppConfig.defaultBaseUrl;
  bool get hasSession => _refresh != null;

  Future<void> init() async {
    _prefs = await SharedPreferences.getInstance();
    _access = await _secure.read(key: _kAccess);
    _refresh = await _secure.read(key: _kRefresh);
    _dio = Dio(BaseOptions(baseUrl: baseUrl, connectTimeout: const Duration(seconds: 12), receiveTimeout: const Duration(seconds: 20)));
    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (o, h) {
        o.baseUrl = baseUrl;
        if (_access != null && !o.path.contains('/auth/')) o.headers['Authorization'] = 'Bearer $_access';
        h.next(o);
      },
      onError: (e, h) async {
        final isAuth = e.requestOptions.path.contains('/auth/');
        if (e.response?.statusCode == 401 && !isAuth && _refresh != null && e.requestOptions.extra['retried'] != true) {
          if (await (_refreshing ??= _doRefresh().whenComplete(() => _refreshing = null))) {
            final o = e.requestOptions..extra['retried'] = true;
            o.headers['Authorization'] = 'Bearer $_access';
            try {
              return h.resolve(await _dio.fetch(o));
            } on DioException catch (e2) {
              return h.next(e2);
            }
          }
          onSessionExpired?.call();
        }
        h.next(e);
      },
    ));
  }

  Future<void> setBaseUrl(String url) async {
    var u = url.trim();
    if (u.endsWith('/')) u = u.substring(0, u.length - 1);
    await _prefs.setString(_kBase, u);
  }

  Future<void> resetBaseUrl() => _prefs.remove(_kBase);

  Future<bool> _doRefresh() async {
    try {
      final r = await _dio.post('/api/v1/auth/refresh', data: {'refreshToken': _refresh});
      await _saveTokens(r.data['tokens']);
      return true;
    } catch (_) {
      await clearSession();
      return false;
    }
  }

  Future<void> _saveTokens(Map<String, dynamic> t) async {
    _access = t['accessToken'];
    _refresh = t['refreshToken'];
    await _secure.write(key: _kAccess, value: _access);
    await _secure.write(key: _kRefresh, value: _refresh);
  }

  Future<void> clearSession() async {
    _access = _refresh = null;
    await _secure.delete(key: _kAccess);
    await _secure.delete(key: _kRefresh);
  }

  Future<T> _call<T>(Future<Response> Function() fn, T Function(dynamic data) map) async {
    try {
      final r = await fn();
      return map(r.data);
    } on DioException catch (e) {
      final d = e.response?.data;
      if (e.response == null) {
        throw ApiException(0, 'NETWORK', 'Can\'t reach the server. Check your internet connection.');
      }
      if (d is Map && d['message'] != null) throw ApiException(e.response!.statusCode ?? 0, '${d['code'] ?? ''}', '${d['message']}');
      if (e.response!.statusCode == 429) throw ApiException(429, 'RATE', 'Too many attempts. Please wait a minute.');
      throw ApiException(e.response!.statusCode ?? 0, 'HTTP', 'Something went wrong (${e.response!.statusCode}).');
    }
  }

  // ---------------------------------------------------------------- auth
  Future<Map<String, dynamic>> orgLookup(String code) =>
      _call(() => _dio.get('/api/v1/auth/org/${Uri.encodeComponent(code.trim())}'), (d) => Map<String, dynamic>.from(d));

  Future<UserProfile> register(Map<String, dynamic> body) => _call(() => _dio.post('/api/v1/auth/register', data: body), (d) {
        _saveTokens(d['tokens']);
        return UserProfile.fromJson(d['user']);
      });

  Future<UserProfile> login(String email, String password, String deviceId, String? model) =>
      _call(() => _dio.post('/api/v1/auth/login', data: {'email': email.trim(), 'password': password, 'deviceId': deviceId, 'deviceModel': model}), (d) {
        _saveTokens(d['tokens']);
        return UserProfile.fromJson(d['user']);
      });

  Future<void> logout() async {
    final r = _refresh;
    await clearSession();
    if (r != null) {
      try {
        await _dio.post('/api/v1/auth/logout', data: {'refreshToken': r});
      } catch (_) {}
    }
  }

  Future<void> forgotPassword(String email) => _call(() => _dio.post('/api/v1/auth/forgot-password', data: {'email': email.trim()}), (_) {});
  Future<void> resetPassword(String email, String code, String pw) =>
      _call(() => _dio.post('/api/v1/auth/reset-password', data: {'email': email.trim(), 'code': code.trim(), 'newPassword': pw}), (_) {});

  // ---------------------------------------------------------------- me
  Future<UserProfile> me() => _call(() => _dio.get('/api/v1/me'), (d) => UserProfile.fromJson(d));
  Future<Map<String, dynamic>> homeRaw() => _call(() => _dio.get('/api/v1/me/home'), (d) => Map<String, dynamic>.from(d));
  Future<MonthStats> stats(int y, int m) => _call(() => _dio.get('/api/v1/me/stats', queryParameters: {'year': y, 'month': m}), (d) => MonthStats.fromJson(d));
  Future<List<HistoryItem>> history(int y, int m) =>
      _call(() => _dio.get('/api/v1/me/history', queryParameters: {'year': y, 'month': m}), (d) => (d as List).map((e) => HistoryItem.fromJson(e)).toList());
  Future<List<LeaveItem>> leaves() => _call(() => _dio.get('/api/v1/me/leaves'), (d) => (d as List).map((e) => LeaveItem.fromJson(e)).toList());
  Future<void> applyLeave(String type, String from, String to, String? reason) =>
      _call(() => _dio.post('/api/v1/me/leaves', data: {'type': type, 'fromDate': from, 'toDate': to, 'reason': reason}), (_) {});
  Future<void> cancelLeave(int id) => _call(() => _dio.post('/api/v1/me/leaves/$id/cancel'), (_) {});
  Future<List<RegItem>> regularizations() => _call(() => _dio.get('/api/v1/me/regularizations'), (d) => (d as List).map((e) => RegItem.fromJson(e)).toList());
  Future<void> requestRegularization(String date, String? inT, String? outT, String reason) =>
      _call(() => _dio.post('/api/v1/me/regularizations', data: {'date': date, 'inTime': inT, 'outTime': outT, 'reason': reason}), (_) {});
  Future<void> changePassword(String cur, String next) =>
      _call(() => _dio.post('/api/v1/me/change-password', data: {'currentPassword': cur, 'newPassword': next}), (_) {});
  Future<List<Map<String, dynamic>>> holidays(int year) =>
      _call(() => _dio.get('/api/v1/me/holidays', queryParameters: {'year': year}), (d) => List<Map<String, dynamic>>.from(d));

  // ---------------------------------------------------------------- punches
  Future<PunchOutcome> punch(bool checkIn, Map<String, dynamic> body) =>
      _call(() => _dio.post(checkIn ? '/api/v1/attendance/check-in' : '/api/v1/attendance/check-out', data: body), (d) => PunchOutcome.fromJson(d));

  /// Returns eventId → outcome.
  Future<Map<String, PunchOutcome>> sync(List<Map<String, dynamic>> batch) => _call(() => _dio.post('/api/v1/attendance/sync', data: batch), (d) {
        final m = <String, PunchOutcome>{};
        for (final r in d as List) {
          m[r['eventId']] = PunchOutcome.fromJson(r['result']);
        }
        return m;
      });
}
