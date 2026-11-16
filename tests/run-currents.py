"""Run actual RS/Rivers Harmony current tests, with deliberate patch-removal controls."""
from pathlib import Path
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
from support import dependency, dotnet_root
SDK = dotnet_root()
DOTNET = str(SDK / 'dotnet')
ENV = os.environ.copy()
ENV['DOTNET_ROOT'] = str(SDK)
ENV.pop('QRS_CURRENT_NEGATIVE', None)

def hashes():
    return {str(p.relative_to(ROOT.parent)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted((ROOT.parent / 'src').glob('*.cs'))}

with (ROOT / 'currents-results.txt').open('w') as out:
    def record(s):
        out.write(s)
        out.flush()
        print(s, end='', flush=True)

    def run(cmd, expected=0, negative=None):
        env = ENV.copy()
        if negative:
            env['QRS_CURRENT_NEGATIVE'] = negative
        record(('$ QRS_CURRENT_NEGATIVE=' + negative + ' ' if negative else '$ ') + shlex.join(cmd) + '\n')
        p = subprocess.run(cmd, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                           stderr=subprocess.STDOUT, text=True)
        record(p.stdout)
        record(f'EXIT {p.returncode}\n')
        if p.returncode != expected:
            raise SystemExit(f'Expected exit {expected}, got {p.returncode}')
        return p.stdout

    original_hashes = hashes()
    record('HEADLESS CURRENT REGRESSION: actual upstream RS Current methods + actual Rivers 5.0.3 binary.\n')
    record('LIMITATIONS: no full game, rendering, network or tick-loop simulation; mock world/chunk interfaces; RS SeaWeatherSystem.CurrentAt sampling boundary replaced with deterministic fixture. Actual private RS Current and actual Rivers nested Postfix are not substituted. Vanilla DoApply and Rivers boat registrations unchanged; actual Rivers prefix + real vanilla DoApply exercised for player-current opt-out.\n')
    record('SOURCE SHA256:\n' + json.dumps(original_hashes, indent=2) + '\n')
    river = dependency('RIVERS_DLL')
    record('Rivers.dll SHA256: ' + hashlib.sha256(river.read_bytes()).hexdigest() + '\n')
    for tag in ('v5.0.1',):
        run([DOTNET, 'build', f'fixtures/{tag}/Upstream.csproj', '-v:q'])
    run([DOTNET, 'build', 'CurrentHarness.csproj', '-v:q'])
    controls = {
        'river-prefix': 'FAIL Rivers actual Postfix opposed preserves propulsion/vertical',
        'river-postfix': 'FAIL Rivers actual Postfix opposed preserves propulsion/vertical',
        'rs-postfix': 'FAIL RS actual Current opposed',
        'player-prefix': 'FAIL player currents false skips only actual Rivers body',
    }
    total = 0
    for tag in ('v5.0.1',):
        command = [DOTNET, 'run', '--project', 'CurrentHarness.csproj', '--no-build', '--', tag]
        for control, failure in controls.items():
            text = run(command, expected=1, negative=control)
            assert failure in text, f'{tag} {control}: did not fail the intended behavioral assertion'
            record(f'PASS negative control {tag} {control} fails intended behavioral assertion\n')
        text = run(command)
        match = re.search(r'TOTAL (\d+) assertions passed; 0 failed; 2000 randomized cases\.', text)
        assert match, 'Missing runtime success total'
        count = sum(line.startswith('PASS ') for line in text.splitlines())
        assert count == int(match[1]), (count, match[1])
        total += count
    assert original_hashes == hashes(), 'Production source changed during tests'
    record('PASS production source hashes unchanged during builds/execution\n')
    record(f'AGGREGATE {total} runtime assertions passed across 1 actual upstream tag; 2000 seeded projection cases; 4 deliberate patch-removal negative controls failed as intended; 0 unexpected failures.\n')
