# AttendancePro – brand & store assets

Everything here was generated from the sample organization **Greenfield Institute** (fictional) using the
sample-data seeder, so the screenshots show realistic but made-up people and numbers.

| Folder | Contents | Use |
|---|---|---|
| `../branding/` | `logo-icon.svg`, `logo-foreground.svg` (Android adaptive), `icon-1024.png`, wordmark PNGs (dark/light text, transparent), `render.py` | Logo source files |
| `play-store/app-icon-512.png` | 512×512 full-bleed icon | Google Play **App icon** |
| `play-store/feature-graphic-1024x500.png` | 1024×500 | Google Play **Feature graphic** |
| `play-store/phone-screenshots/` | 10 captioned screenshots, 1080×1920 (9:16) | Google Play **Phone screenshots** (upload 4–8) |
| `play-store/clean/` | The same 10+ device screenshots with no caption/frame and no status bar | Docs, website, your own layouts |
| `promo/` | 6 browser-framed admin-portal images, 1920×1080 | Website, decks, social, Play "7/10-inch tablet" slots if you want them |
| `admin-portal/` | 16 raw 1920×1080 portal screenshots | Documentation / sales material |
| `raw/` | Untouched phone captures (include the real status bar) | Source only – do not publish |

## Suggested Google Play order
`01_geofenced_sign_in` → `02_live_session` → `04_personal_stats` → `05_calendar` → `03_out_of_range` → `07_leave_requests` → `08_join_with_code` → `09_dark_mode`

## Listing copy (edit freely)
**Title (≤30):** AttendancePro: Geofence Time Clock
**Short description (≤80):** Sign in and out only on site. Track attendance, hours and leave.
**Full description:**
AttendancePro is geofenced attendance for companies, schools, colleges, clinics, shops and any organization.
Your administrator draws the allowed area on a map; you can sign in and out only while you are inside it, and the server verifies every punch.

• One-tap sign in / sign out with a live map of your allowed area
• Know how far you are from the boundary
• Personal statistics: attendance %, punctuality, hours worked, late days, streaks, calendar view
• Complete punch history
• Leave requests and attendance-correction requests with approvals
• Works through poor signal: punches are saved and synced automatically
• Monthly attendance report emailed to you automatically
• Privacy-first: location is read only while the app is open and when you sign in/out (never in the background), and is shared only with your organization

Join with the organization code from your administrator. (Administrators manage everything from the AttendancePro web portal.)

## Play Console reminders
* Declare **Location (precise, foreground)** under Data safety and add a privacy-policy URL.
* Category: Business (or Education for school deployments). Content rating: Everyone.
* Map tiles are © OpenStreetMap contributors – the attribution is visible in-app; keep it in any screenshot that shows the map.
* The screenshots show a real street map of a public area (Chandigarh, India) because the demo location is only a placeholder. For your launch you may prefer to re-shoot at your own campus (see below).

## Re-creating the screenshots
1. `cd server/AttendancePro.Web`, run with `Seed__SampleData=true Demo__ClockOffsetHours=<h>` (stages the clock to a weekday morning; **never use in production**).
2. Build a screenshot APK: `flutter build apk --profile --target-platform android-arm64 --dart-define=API_BASE_URL=... --dart-define=DEMO_LAT=<lat> --dart-define=DEMO_LNG=<lng> --dart-define=DEMO_DEVICE="Pixel 8"` (`DEMO_*` fake the GPS position for staging only).
3. Sample logins (password `Demo#12345`): `priya.sharma@greenfield.edu` (member). Admin: `admin@greenfield.edu` / `Admin#12345`. Join code `GREEN1`.
4. Capture at 1080×1920 (`adb shell wm size 1080x1920`, reset with `wm size reset`), then run `marketing/tools/admin_shots.py` (portal) and `marketing/tools/compose.py` (captions/frames; needs `pip install pillow playwright` and Chrome). `start_demo.py HH:MM [reset]` starts the staged demo server. The scripts contain absolute paths from the author's machine – adjust them first.
