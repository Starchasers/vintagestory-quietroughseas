"""Headless real-SDK motion boundary tests plus deliberate Harmony-removal controls."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
from support import dotnet_root

ROOT = Path(__file__).resolve().parent
ENV = os.environ.copy()
ENV['DOTNET_ROOT'] = str(dotnet_root())
ENV.pop('QRS_MOTION_NEGATIVE', None)
DOTNET = str(Path(ENV['DOTNET_ROOT']) / 'dotnet')

def hashes():
    return {str(p.relative_to(ROOT.parent)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted((ROOT.parent / 'src').glob('*.cs'))}

with (ROOT / 'motion-results.txt').open('w') as out:
    def record(s):
        out.write(s + '\n')
        out.flush()
        print(s, flush=True)

    def run(cmd, expected=0, control=None):
        env = ENV.copy()
        if control:
            env['QRS_MOTION_NEGATIVE'] = control
        record('$ ' + ('QRS_MOTION_NEGATIVE=' + control + ' ' if control else '') + ' '.join(cmd))
        p = subprocess.run(cmd, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        record(p.stdout.rstrip())
        record(f'EXIT {p.returncode}')
        if p.returncode != expected:
            raise SystemExit(f'Expected exit {expected}, got {p.returncode}')
        return p.stdout

    record('HEADLESS MOTION: real Rough Seas v5.0.1 equations, real native packet NaN validation, production StartPre Harmony registration.')
    record('LIMITATIONS: no game/render/tick-loop/network I/O. Mock API/sea; ClientMain player getter fixture; native physics body and finite serializer body skipped by last-priority sentinels. Lift return injected at its actual patched method boundary; real Rough Seas SendUnlifted/RestoreLift methods execute.')
    before = hashes()
    record('SOURCE SHA256: ' + json.dumps(before, sort_keys=True))
    run([DOTNET, 'build', 'fixtures/v5.0.1/Upstream.csproj', '-v:q', '-p:WarningLevel=0'])
    run([DOTNET, 'build', 'MotionHarness.csproj', '-v:q', '-p:WarningLevel=0'])
    command = [DOTNET, 'run', '--project', 'MotionHarness.csproj', '--no-build']
    controls = {'dt': 'FAIL invalid dt guard 0', 'lift': 'FAIL Lift normalization NaN',
                'packet': 'FAIL no-snapshot invalid packet skips native NaN validator',
                'snapshot': 'FAIL physics finite snapshot recovery'}
    for control, failure in controls.items():
        text = run(command, 1, control)
        assert failure in text, f'{control}: missing intended behavioral failure {failure}'
        record(f'PASS negative control {control} fails intended behavioral assertion')
    text = run(command)
    match = re.search(r'TOTAL (\d+) assertions passed; 0 failed', text)
    assert match, 'Missing successful runtime total'
    count = sum(line.startswith('PASS ') for line in text.splitlines())
    assert count == int(match[1]), (count, match[1])
    assert hashes() == before, 'Production source changed during execution'
    record(f'AGGREGATE {count} runtime assertions passed; 4 deliberate patch-removal controls failed as intended; 0 unexpected failures; production source unchanged.')
