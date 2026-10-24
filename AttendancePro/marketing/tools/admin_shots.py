import re, sqlite3, os
from playwright.sync_api import sync_playwright

OUT = r"D:\android_apps\rk-innovations-site\.claude\worktrees\android-attendance-geofencing-e58f29\AttendancePro\marketing\admin-portal"
DB = r"D:\android_apps\rk-innovations-site\.claude\worktrees\android-attendance-geofencing-e58f29\AttendancePro\server\AttendancePro.Web\App_Data\attendance.db"
os.makedirs(OUT, exist_ok=True)
c = sqlite3.connect(DB)
site_id = c.execute("select Id from Sites where Name='Sports Complex'").fetchone()[0]
main_id = c.execute("select Id from Sites where Name='Main Campus'").fetchone()[0]
flag_id = c.execute("select Id from Attendance where FlagReason like 'Impossible%'").fetchone()[0]
priya = c.execute("select Id from AspNetUsers where Email like 'priya.%'").fetchone()[0]

shots = [
    ("01-dashboard", "/", 2500),
    ("02-members", "/Users", 800),
    ("03-member-statistics", f"/Users/Details/{priya}", 1200),
    ("04-locations", "/Sites", 3500),
    ("05-draw-geofence", f"/Sites/Edit/{main_id}", 3500),
    ("06-custom-boundary", f"/Sites/Edit/{site_id}", 3500),
    ("07-attendance-records", "/Attendance?from=2026-10-19&to=2026-10-26", 800),
    ("08-flagged-punch-review", f"/Attendance/Details/{flag_id}", 3500),
    ("09-leave-requests", "/Leaves?status=Pending", 800),
    ("10-corrections", "/Regularizations", 800),
    ("11-monthly-report", "/Reports/Monthly?year=2026&month=10", 1200),
    ("12-daily-roll-call", "/Reports/Daily", 800),
    ("13-policy-settings", "/Settings", 800),
    ("14-audit-log", "/Audit", 800),
    ("15-shifts", "/Shifts", 800),
]

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True)
    ctx = b.new_context(viewport={"width": 1920, "height": 1080}, device_scale_factor=1)
    page = ctx.new_page()
    page.goto("http://localhost:5080/Account/Login")
    page.screenshot(path=os.path.join(OUT, "00-login.png"))
    page.fill("input[name=Email]", "admin@greenfield.edu")
    page.fill("input[name=Password]", "Admin#12345")
    page.click("button.btn-primary")
    page.wait_for_url("http://localhost:5080/")
    for name, path, wait in shots:
        page.goto("http://localhost:5080" + path)
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(wait)
        page.screenshot(path=os.path.join(OUT, name + ".png"))
        print("ok", name)
    b.close()
