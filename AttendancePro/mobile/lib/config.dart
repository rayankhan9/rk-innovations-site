/// Build-time defaults. A company white-labelling the app sets its own server:
///   flutter build apk --dart-define=API_BASE_URL=https://attendance.example.com
/// Users can also change the server from the sign-in screen (Advanced).
class AppConfig {
  static const defaultBaseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://10.0.2.2:5080');
  static const appName = String.fromEnvironment('APP_NAME', defaultValue: 'AttendancePro');
  /// Screenshot/demo builds only: pretend the phone is at DEMO_LAT,DEMO_LNG. Never set for real releases.
  static final double? _demoLat = double.tryParse(const String.fromEnvironment('DEMO_LAT'));
  static final double? _demoLng = double.tryParse(const String.fromEnvironment('DEMO_LNG'));
  static double get demoLat => _demoLat ?? 0;
  static double get demoLng => _demoLng ?? 0;
  static bool get demoLocation => _demoLat != null && _demoLng != null;
  /// Screenshot builds: label shown for the linked phone instead of the real device model.
  static const demoDevice = String.fromEnvironment('DEMO_DEVICE');
  static const tileUrl = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
  static const userAgentPackage = 'com.rkinnovations.attendance_pro';
}
