import os, pathlib, shutil
from PIL import Image
from playwright.sync_api import sync_playwright

M = pathlib.Path(r"D:\android_apps\rk-innovations-site\.claude\worktrees\android-attendance-geofencing-e58f29\AttendancePro\marketing")
BR = pathlib.Path(r"D:\android_apps\rk-innovations-site\.claude\worktrees\android-attendance-geofencing-e58f29\AttendancePro\branding")
RAW, ADM = M / "raw", M / "admin-portal"
CLEAN, PLAY, PROMO, TMP = M / "play-store" / "clean", M / "play-store" / "phone-screenshots", M / "promo", M / "_tmp"
for d in (CLEAN, PLAY, PROMO, TMP):
    d.mkdir(parents=True, exist_ok=True)

# 1) clean device screenshots: strip status bar (real clock/battery) and navigation bar
for f in sorted(RAW.glob("*.png")):
    Image.open(f).convert("RGB").crop((0, 80, 1080, 1812)).save(CLEAN / f.name)

FONT = "'Segoe UI', 'Inter', Arial, sans-serif"
BASE_CSS = f"""
*{{box-sizing:border-box;margin:0;padding:0}}
html,body{{width:100%;height:100%;font-family:{FONT};-webkit-font-smoothing:antialiased}}
.bg{{position:absolute;inset:0;background:linear-gradient(160deg,#14B8A6 0%,#0F766E 55%,#0B4F4A 100%)}}
.ring{{position:absolute;border-radius:50%;border:3px dashed rgba(255,255,255,.18)}}
.phone{{position:absolute;background:#0b1211;border-radius:70px;padding:16px;box-shadow:0 40px 90px rgba(0,0,0,.45),0 0 0 3px #26332f inset}}
.phone img{{display:block;width:100%;border-radius:56px}}
"""

def page_phone(img, title, sub, tag=None):
    return f"""<!doctype html><meta charset=utf-8><style>{BASE_CSS}
    .t{{position:absolute;left:70px;right:70px;top:78px;color:#fff}}
    .t h1{{font-size:78px;line-height:1.04;font-weight:800;letter-spacing:-1.5px}}
    .t p{{font-size:38px;margin-top:22px;opacity:.88;line-height:1.25}}
    .tag{{display:inline-block;font-size:26px;font-weight:700;letter-spacing:3px;text-transform:uppercase;background:rgba(255,255,255,.16);padding:8px 18px;border-radius:99px;margin-bottom:22px}}
    </style><div class=bg></div>
    <div class=ring style="width:1100px;height:1100px;right:-480px;top:760px"></div><div class=ring style="width:760px;height:760px;right:-300px;top:930px"></div>
    <div class=t>{f'<span class=tag>{tag}</span><br>' if tag else ''}<h1>{title}</h1><p>{sub}</p></div>
    <div class=phone style="left:120px;top:478px;width:840px"><img src="{img.as_uri()}"></div>"""

PHONE_SHOTS = [
    ("01_geofenced_sign_in", "02_today_inside.png", "Sign in where<br>you belong", "Attendance only counts inside the area your organization sets", "Geofenced"),
    ("02_live_session", "03_today_signed_in.png", "One tap in.<br>One tap out.", "Live session timer and hours worked today", "Simple"),
    ("03_out_of_range", "14_today_outside.png", "Know exactly<br>where the line is", "See how far you are from the allowed area", "Clear"),
    ("04_personal_stats", "05_stats.png", "Your attendance,<br>at a glance", "Attendance %, punctuality, hours and streaks", "Insights"),
    ("05_calendar", "06_stats_chart.png", "Every day on<br>one calendar", "Hours per day, late arrivals, leave and holidays", "Insights"),
    ("06_history", "04_history.png", "A complete record<br>of every punch", "Sessions, locations and durations — always at hand", "History"),
    ("07_leave_requests", "07_requests.png", "Leave and fixes,<br>handled in-app", "Request, track and get approvals without paperwork", "Requests"),
    ("08_join_with_code", "15_register.png", "Join with your<br>organization code", "Register in under a minute — no IT help needed", "Onboarding"),
    ("09_dark_mode", "10_today_dark.png", "Light or dark,<br>always clear", "Follows your phone's theme", "Comfort"),
    ("10_profile", "09_profile.png", "Your details,<br>your shift, your site", "Everything about your assignment in one place", "Profile"),
]

def page_feature():
    a = (CLEAN / "02_today_inside.png").as_uri(); b = (CLEAN / "05_stats.png").as_uri()
    icon = (BR / "icon-1024.png").as_uri()
    return f"""<!doctype html><meta charset=utf-8><style>{BASE_CSS}
    .ph{{position:absolute;background:#0b1211;border-radius:44px;padding:10px;width:290px;box-shadow:0 24px 60px rgba(0,0,0,.45)}}
    .ph img{{border-radius:34px;width:100%;display:block}}
    h1{{font-size:64px;font-weight:800;letter-spacing:-1px;color:#fff}}
    p{{font-size:30px;color:rgba(255,255,255,.9);line-height:1.3;margin-top:14px}}
    </style><div class=bg></div>
    <div class=ring style="width:700px;height:700px;left:-250px;top:-300px"></div>
    <img src="{icon}" style="position:absolute;left:64px;top:62px;width:120px;height:120px;border-radius:28px;box-shadow:0 12px 30px rgba(0,0,0,.3)">
    <div style="position:absolute;left:64px;top:214px;width:520px"><h1>AttendancePro</h1><p>Geofenced attendance for every organization</p>
      <p style="font-size:23px;opacity:.75;margin-top:22px">Verified sign-in &amp; sign-out · Personal stats · Automatic monthly reports</p></div>
    <div class=ph style="left:610px;top:70px;transform:rotate(-4deg)"><img src="{a}"></div>
    <div class=ph style="left:790px;top:150px;transform:rotate(5deg)"><img src="{b}"></div>"""

def page_browser(img, title, sub):
    return f"""<!doctype html><meta charset=utf-8><style>{BASE_CSS}
    .t{{position:absolute;left:90px;top:62px;color:#fff}}
    .t h1{{font-size:62px;font-weight:800;letter-spacing:-1px}} .t p{{font-size:30px;margin-top:10px;opacity:.88}}
    .win{{position:absolute;left:90px;right:90px;top:250px;bottom:-4px;background:#fff;border-radius:18px 18px 0 0;overflow:hidden;box-shadow:0 30px 80px rgba(0,0,0,.45)}}
    .bar{{height:46px;background:#e8ecea;display:flex;align-items:center;gap:9px;padding:0 18px}}
    .bar i{{width:14px;height:14px;border-radius:50%;display:block}}
    .url{{margin-left:18px;flex:1;max-width:620px;height:26px;border-radius:13px;background:#fff;color:#667;font-size:15px;line-height:26px;padding:0 14px}}
    .win img{{display:block;width:100%}}
    </style><div class=bg></div><div class=t><h1>{title}</h1><p>{sub}</p></div>
    <div class=win><div class=bar><i style="background:#ff5f57"></i><i style="background:#febc2e"></i><i style="background:#28c840"></i><span class=url>attendance.greenfield.edu</span></div><img src="{img.as_uri()}"></div>"""

BROWSER_SHOTS = [
    ("01_dashboard", "01-dashboard.png", "See attendance the moment it happens", "Live dashboard: present, late, signed in now, on leave and trends"),
    ("02_draw_geofence", "05-draw-geofence.png", "Draw the boundary on a map", "Circles or custom shapes, any number of locations — powered by OpenStreetMap"),
    ("03_monthly_report", "11-monthly-report.png", "Monthly reports, sent automatically", "Per-member summaries by email and a full organization report with CSV export"),
    ("04_flagged_review", "08-flagged-punch-review.png", "Catch suspicious punches", "Impossible-travel, mock-GPS and offline flags with every punch on the map"),
    ("05_member_statistics", "03-member-statistics.png", "Deep statistics for every member", "Calendar, hours, punctuality and full history in one view"),
    ("06_approvals", "09-leave-requests.png", "Approve leave in one click", "Leave and attendance corrections with an audit trail"),
]

LOGO_SVG = (BR / "logo-icon.svg").read_text(encoding="utf-8")

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True)

    def render(html, out, w, h, transparent=False):
        f = TMP / "page.html"; f.write_text(html, encoding="utf-8")
        pg = b.new_page(viewport={"width": w, "height": h}, device_scale_factor=1)
        pg.goto(f.as_uri()); pg.wait_for_load_state("networkidle"); pg.wait_for_timeout(300)
        pg.screenshot(path=str(out), omit_background=transparent)
        pg.close()

    for name, src, t, s, tag in PHONE_SHOTS:
        render(page_phone(CLEAN / src, t, s, tag), PLAY / f"{name}.png", 1080, 1920)
        print("phone", name)
    render(page_feature(), M / "play-store" / "feature-graphic-1024x500.png", 1024, 500)
    for name, src, t, s in BROWSER_SHOTS:
        render(page_browser(ADM / src, t, s), PROMO / f"admin_{name}.png", 1920, 1080)
        print("promo", name)

    # wordmark logos
    wm = lambda color, bg: f"""<!doctype html><meta charset=utf-8><style>*{{margin:0}}body{{background:{bg};display:flex;align-items:center;gap:36px;padding:56px 70px;font-family:{FONT}}}
        svg{{width:180px;height:180px}} span{{font-size:128px;font-weight:800;letter-spacing:-3px;color:{color}}} b{{color:#14B8A6;font-weight:800}}</style>{LOGO_SVG}<span>Attendance<b>Pro</b></span>"""
    render(wm("#0B3F3B", "transparent"), BR / "wordmark-dark-text.png", 1220, 300, transparent=True)
    render(wm("#ffffff", "transparent"), BR / "wordmark-light-text.png", 1220, 300, transparent=True)
    b.close()

shutil.rmtree(TMP, ignore_errors=True)
shutil.copy(BR / "play-icon-512.png", M / "play-store" / "app-icon-512.png")
shutil.copy(BR / "icon-1024.png", M / "play-store" / "app-icon-1024-rounded.png")
print("done")
