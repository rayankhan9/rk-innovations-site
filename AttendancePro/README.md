# AttendancePro

Geofenced attendance for companies, schools, colleges, clinics, retail and any other organization.
Members install the **Android app** (Flutter), register with an organization code, and can **sign in / sign out only
inside the boundary the admin draws on a map**. Admins run everything from an **ASP.NET Core MVC** portal.
Everything is free / open source: OpenStreetMap + Leaflet + flutter_map for maps, SQLite, any SMTP server for email.

```
AttendancePro/
├── server/
│   ├── AttendancePro.Web/     ASP.NET Core 10 MVC admin portal + REST API (/api/v1) + monthly-report job
│   └── AttendancePro.Tests/   xUnit tests (geofence maths, punch rules, stats)
└── mobile/                    Flutter app (Android)
```

## How it works

1. An admin creates the organization at `/Account/Signup` (or the seeded demo org in Development) and gets a **join code**.
2. Admin adds **Locations** (Sites): a circle (centre + radius) or a free-form polygon drawn on an OpenStreetMap map, optional shifts, holidays.
3. The company tells users to install the app. Users enter the join code, register, and (if approval is on) wait for the admin to approve them.
4. In the app the user sees a live map with the allowed area and an "inside / 42 m outside" indicator. **Sign in / Sign out** unlock only inside the boundary.
5. **The server re-checks everything** (location, accuracy, mock flag, device, clock) — the app is never trusted.
6. On the 1st of every month (organization time zone) each member automatically receives an emailed report; admins receive an organization summary.

## Feature set (and why — based on what leading attendance/geofencing products do)

| Area | What is included |
|---|---|
| **Geofencing** | Circle or polygon sites, multiple sites, per-user allowed sites, boundary tolerance, GPS-accuracy cap, server-side authoritative check |
| **Anti-fraud** | Mock-location detection (client flag, server rejects), device binding (one phone per account, admin can reset), trusted server time + clock-drift limit, impossible-travel flag, optional selfie with every punch, idempotent event IDs, flagged-punch review queue |
| **Reliability** | Offline punches queued on the phone with the device timestamp and replayed later (bounded window), auto-close of forgotten sessions (flagged), correction requests (regularization) with admin approval |
| **Member app** | Live map, one-tap punch, live session timer, history, **personal statistics** (attendance %, punctuality, hours, avg/day, late, absent, leave, streak, hours chart, calendar heat-map), leave requests, correction requests, password reset by emailed code |
| **Admin portal** | Dashboard (present / late / signed-in-now / on-leave / absent, 14-day trend, top late), people (approve, edit, disable, delete, reset device/password), locations (map editor), shifts & grace periods, holidays, attendance records with filters + CSV export + map view of each punch, leave & correction approvals, daily roll call, **monthly org report** (CSV, print, resend), organization policy settings, audit log |
| **Multi-tenant** | Any number of organizations in one deployment; global EF query filter isolates tenants; wording adapts for education (Members) vs company (Employees) |
| **Reports** | Monthly HTML email to each member + admin summary; sent exactly once (tracked in `ReportLogs`, survives restarts); CSV with formula-injection protection |
| **Security** | ASP.NET Identity (lockout, password rules), JWT access tokens + rotating hashed refresh tokens, rate limiting on auth endpoints, anti-forgery on all portal posts, CSP + security headers, selfies stored outside `wwwroot` and served only to same-org admins, enumeration-safe password reset, audit log of admin actions |

## Run the backend

Requirements: .NET 10 SDK.

```bash
cd server/AttendancePro.Web
dotnet run --urls http://localhost:5080        # Development seeds a demo organization
```

Demo portal login: `admin@demo.local` / `Admin#12345`, join code **DEMO01** (Development only — see `appsettings.Development.json`).
Open `http://localhost:5080`, go to **Locations → Main campus**, and move the circle to where you are testing.

Database: SQLite file in `App_Data/` (migrations are applied automatically at startup). Run tests: `dotnet test` in `server/`.

### Production checklist

- Set secrets by environment variable: `Jwt__Key` (32+ random chars — the app refuses to start with the dev key outside Development), `ConnectionStrings__Default`, `Smtp__Host/Port/User/Password/FromAddress`.
- Serve behind HTTPS (IIS, nginx, Caddy…). The Android release build **blocks plain HTTP**.
- Set `Registration:AllowOrganizationSignup` to `false` if you host a single organization.
- Back up `App_Data/` (database + selfies). For heavy use switch to PostgreSQL/SQL Server: swap the EF provider package, `UseSqlite` in `Program.cs`, and regenerate migrations (`dotnet ef migrations add Initial`).
- SMTP: any free relay works (Gmail app password, Brevo/Sendinblue free tier, your own server). With no SMTP configured emails are written to `App_Data/outbox/` so you can inspect them.
- The monthly job runs inside the web app (checks every 30 min). Keep the site warm (IIS "Always running" / systemd service) so it is not recycled away.
- Maps use the public OpenStreetMap tile server, fine for modest use. For large deployments point `AppConfig.tileUrl` (app) and the Leaflet URLs (portal) to your own/other free-tier tile provider per the [OSM tile policy](https://operations.osmfoundation.org/policies/tiles/).

## Build the Android app

Requirements: Flutter 3.x, Android SDK.

```bash
cd mobile
flutter pub get
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080        # emulator → host machine
flutter build apk --release --dart-define=API_BASE_URL=https://attendance.yourcompany.com
```

- `API_BASE_URL` / `APP_NAME` are compile-time defaults, so a company can ship its own build. Users can also change the server on the sign-in screen.
- Before publishing to Google Play: change the application id (`android/app/build.gradle.kts`), add your own launcher icon, create a release keystore (`android/key.properties`) and a privacy policy (a draft is at `/attendance-pro/privacy.html` in the site repo) that states location is read only while the app is open and recorded when signing in/out.
- Debug builds allow cleartext HTTP (for the emulator); release builds do not.

## API overview (`/api/v1`, JSON, Bearer JWT)

`POST auth/register · auth/login · auth/refresh · auth/logout · auth/forgot-password · auth/reset-password`, `GET auth/org/{code}`
`GET me · me/home · me/stats?year&month · me/history · me/holidays`, `PUT me`, `POST me/change-password`
`GET/POST me/leaves`, `POST me/leaves/{id}/cancel`, `GET/POST me/regularizations`
`POST attendance/check-in · attendance/check-out · attendance/sync` (batch of queued offline punches)

Punch rejections come back as `200 { ok:false, code, message }` with codes such as `OUTSIDE_GEOFENCE`, `MOCK_LOCATION`, `LOW_ACCURACY`, `DEVICE_MISMATCH`, `CLOCK_DRIFT`, `ALREADY_CHECKED_IN`.

## Honest limits

- Mock-location detection relies on Android's `isMocked` flag. A rooted device or sophisticated spoofing framework can defeat client-side checks; the server adds device binding, accuracy/clock checks, impossible-travel flags and optional selfies, and flags suspicious punches for review — it cannot make GPS tamper-proof. There is no face *recognition*: selfies are stored for human review only (deliberate: biometric matching has legal/privacy implications).
- Push notifications (Firebase) and iOS are not included. The Flutter code is portable to iOS; add the location usage strings and Apple signing.
- The Android app has been run on a Samsung Galaxy A32 (register, live map, sign in/out, outside-area state, offline queue + sync, stats, history, leave and correction requests). The selfie/camera flow, mock-location detection, device-binding rejection and the emailed monthly report on a real SMTP server have not been exercised on hardware yet.
- Selfie capture / storing photos and location history are personal data in many jurisdictions (GDPR, DPDP, etc.): make sure your organization has a lawful basis, retention policy and employee/student/guardian notice.

## Sample data for demos & screenshots
`Seed__SampleData=true` creates "Greenfield Institute" (join code `GREEN1`): 24 members, 45 days of history, leave, corrections, flagged punches, holidays and an audit trail. Admin `admin@greenfield.edu` / `Admin#12345`, member `priya.sharma@greenfield.edu` / `Demo#12345`. Logos and store assets live in `branding/` and `marketing/` (see `marketing/README.md`).
