"""Render an SVG/HTML snippet to PNG with headless Chrome. Usage: python render.py in.svg out.png WIDTH HEIGHT [transparent]"""
import subprocess, sys, pathlib, tempfile
src, out, w, h = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
transparent = len(sys.argv) > 5 and sys.argv[5] == "transparent"
svg = pathlib.Path(src).read_text(encoding="utf-8")
html = f"<!doctype html><meta charset=utf-8><style>html,body{{margin:0;padding:0;background:{'transparent' if transparent else '#fff'}}}svg{{display:block;width:{w}px;height:{h}px}}</style>{svg}"
with tempfile.TemporaryDirectory() as d:
    f = pathlib.Path(d) / "r.html"; f.write_text(html, encoding="utf-8")
    cmd = ["C:/Program Files/Google/Chrome/Application/chrome.exe", "--headless=new", "--disable-gpu", "--hide-scrollbars",
           f"--window-size={w},{h}", f"--screenshot={pathlib.Path(out).resolve()}", "--force-device-scale-factor=1"]
    if transparent: cmd.append("--default-background-color=00000000")
    cmd.append(f.as_uri())
    subprocess.run(cmd, check=True, capture_output=True, timeout=60)
print("ok", out)
