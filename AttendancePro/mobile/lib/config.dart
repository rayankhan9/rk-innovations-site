/// Build-time defaults. A company white-labelling the app sets its own server:
///   flutter build apk --dart-define=API_BASE_URL=https://attendance.example.com
/// Users can also change the server from the sign-in screen (Advanced).
class AppConfig {
  static const defaultBaseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://10.0.2.2:5080');
  static const appName = String.fromEnvironment('APP_NAME', defaultValue: 'AttendancePro');
  static const tileUrl = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
  static const userAgentPackage = 'com.rkinnovations.attendance_pro';
}
