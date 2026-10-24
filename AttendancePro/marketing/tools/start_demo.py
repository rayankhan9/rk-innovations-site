"""Start the demo server with the clock staged to Mon 26 Oct 2026 at HH:MM India time. Usage: start_demo.py HH:MM [reset]"""
import datetime, os, subprocess, sys, shutil, time, urllib.request

hh, mm = map(int, sys.argv[1].split(':'))
reset = len(sys.argv) > 2 and sys.argv[2] == 'reset'
root = r"D:\android_apps\rk-innovations-site\.claude\worktrees\android-attendance-geofencing-e58f29\AttendancePro\server\AttendancePro.Web"
subprocess.run(["powershell", "-c", "Get-Process AttendancePro.Web -ErrorAction SilentlyContinue | Stop-Process -Force"])
time.sleep(1.5)
if reset:
    shutil.rmtree(os.path.join(root, "App_Data"), ignore_errors=True)
target_utc = datetime.datetime(2026, 10, 26, hh, mm) - datetime.timedelta(hours=5, minutes=30)
off = (target_utc - datetime.datetime.utcnow()).total_seconds() / 3600
env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", Seed__DemoOrganization="false", Seed__SampleData="true", Demo__ClockOffsetHours=f"{off:.4f}")
log = open(r"C:\Users\USER\AppData\Local\Temp\ap.log", "w")
subprocess.Popen(["dotnet", "run", "--no-build", "--urls", "http://localhost:5080"], cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT,
                 creationflags=0x00000008 | 0x00000200)
for _ in range(60):
    time.sleep(1)
    try:
        if urllib.request.urlopen("http://localhost:5080/healthz", timeout=2).status == 200:
            print("up, offset hours", f"{off:.3f}")
            break
    except Exception:
        pass
