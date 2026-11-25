"""Compile the installable source ZIP in a fresh native dedicated server.
No connected client, seating or renderer is simulated. Requires user-owned game
files and the official Rough Seas 5.0.1 ZIP; neither is redistributed.
"""
import hashlib
import os
from pathlib import Path
import selectors
import shutil
import socket
import subprocess
import tempfile
import time
from support import dependency, dotnet_root

repo = Path(__file__).resolve().parent.parent
game = dependency('VINTAGE_STORY_DIR', 'VintagestoryServer.dll')
rough = dependency('ROUGH_SEAS_ZIP')
addon = repo / 'dist/quietroughseas_1.8.3.zip'
assert addon.is_file(), 'Run python package.py first'
root = Path(tempfile.mkdtemp(prefix='qrs-native-', dir=os.environ.get('TMPDIR')))
(root / 'Mods').mkdir()
shutil.copyfile(rough, root / 'Mods/roughseas_5.0.1.zip')
shutil.copyfile(addon, root / 'Mods/quietroughseas_1.8.3.zip')
with socket.socket() as probe:
    probe.bind(('127.0.0.1', 0))
    port = probe.getsockname()[1]
command = [str(dotnet_root() / 'dotnet'), 'VintagestoryServer.dll',
           '--dataPath', str(root), '--ip', '127.0.0.1', '--port', str(port),
           '--maxclients', '1', '--reducedthreads']
lines = []
ready = False
p = subprocess.Popen(command, cwd=game, stdin=subprocess.PIPE,
                     stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                     text=True, bufsize=1)
watch = selectors.DefaultSelector()
assert p.stdout is not None and p.stdin is not None
watch.register(p.stdout, selectors.EVENT_READ)
deadline = time.monotonic() + 150
try:
    while p.poll() is None and time.monotonic() < deadline:
        for key, event in watch.select(timeout=1):
            line = p.stdout.readline()
            lines.append(line)
            if 'Dedicated Server now running' in line and not ready:
                ready = True
                p.stdin.write('/stop\n')
                p.stdin.flush()
    if p.poll() is None:
        p.stdin.write('/stop\n')
        p.stdin.flush()
        p.wait(timeout=30)
    lines.append(p.stdout.read())
finally:
    if p.poll() is None:
        p.kill()
        p.wait()
    watch.close()
text = ''.join(lines)
(root / 'console.txt').write_text(text)
print('Fresh native dataPath:', root)
print('Addon SHA256:', hashlib.sha256(addon.read_bytes()).hexdigest())
print('Rough Seas ZIP SHA256:', hashlib.sha256(rough.read_bytes()).hexdigest())
print('VintagestoryAPI SHA256:', hashlib.sha256((game / 'VintagestoryAPI.dll').read_bytes()).hexdigest())
for line in lines:
    if '[quietroughseas]' in line or 'Dedicated Server now running' in line or 'All threads gracefully shut down' in line:
        print(line.rstrip())
assert ready and p.returncode == 0, ('Native startup/clean exit failed', p.returncode, root)
assert f'[quietroughseas] Successfully compiled {len(list((repo / "src").glob("*.cs")))} source files' in text
assert 'Guarded Rough Seas rowing animation reads' in text
assert 'Guarded Rough Seas cosmetic animation and renderer lifecycles (bounded mod-owned exception containment).' in text
assert 'Guarded Rough Seas zero-time swimming, non-finite vessel lift and player motion packets.' in text
assert '[quietroughseas] Could not' not in text
assert '[Server Error]' not in text
with socket.socket() as probe:
    assert probe.connect_ex(('127.0.0.1', port)) != 0, 'Native server port leaked'
print('PASS native Roslyn source compilation, production Harmony installation against official binary, RunGame, /stop, exit 0 and released loopback port')
print('LIMITATION: no connected player, boat render or FP/immersive-FP visuals exercised')
