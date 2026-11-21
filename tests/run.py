"""Build existing exact-tag fixture and production harness; never run setup implicitly."""
from pathlib import Path
import hashlib, json, os, re, shlex, subprocess
ROOT = Path(__file__).resolve().parent
from support import dependency, dotnet_root
SDK = dotnet_root()
REPO = dependency('ROUGH_SEAS_REPO')
env = os.environ.copy()
env['DOTNET_ROOT'] = str(SDK)
env.pop('QRS_NEGATIVE_CONTROL', None)
dotnet = str(SDK / 'dotnet')
tag = 'v5.0.1'
def hashes():
    return {str(p.relative_to(ROOT.parent)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted((ROOT.parent / 'src').glob('*.cs'))}
source_hashes = hashes()
fixture = ROOT / 'fixtures' / tag
if not (fixture / 'Upstream.csproj').exists():
    raise SystemExit('Exact v5.0.1 fixture is not ready. Have the fixture owner run adapted setup.py first; this runner never invokes setup.')
with (ROOT / 'results.txt').open('w') as out:
    def record(text):
        out.write(text); out.flush(); print(text, end='', flush=True)
    def run(command, expected=0, negative=None):
        child_env = env.copy()
        if negative:
            child_env['QRS_NEGATIVE_CONTROL'] = negative
        record('$ ' + (f'QRS_NEGATIVE_CONTROL={negative} ' if negative else '') + shlex.join(command) + '\n')
        p = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                           text=True, env=child_env, cwd=ROOT)
        record(p.stdout); record(f'EXIT {p.returncode}\n')
        if p.returncode != expected:
            raise SystemExit(f'Expected exit {expected}, got {p.returncode}')
        return p.stdout
    record('Exact Rough Seas 5.0.1 / Quiet Rough Seas 1.7 headless regression; no in-game tests claimed.\n')
    record('Production source SHA256 at execution:\n' + json.dumps(source_hashes, indent=2) + '\n')
    tracked = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', tag, 'RoughSeas'], cwd=REPO, text=True).splitlines()
    expected_files = {p for p in tracked if p.endswith(('.cs', '.json'))}
    actual_files = {str(p.relative_to(fixture)) for p in (fixture / 'RoughSeas').rglob('*') if p.is_file()}
    assert actual_files == expected_files, ('Incomplete/extra fixture files', expected_files - actual_files, actual_files - expected_files)
    for name in sorted(expected_files):
        expected = subprocess.check_output(['git', 'show', f'{tag}:{name}'], cwd=REPO)
        assert (fixture / name).read_bytes() == expected, f'Fixture not verbatim: {name}'
    provenance = json.loads((fixture / 'provenance.json').read_text())
    assert provenance['tag'] == tag
    assert provenance['commit'] == subprocess.check_output(['git', 'rev-parse', tag], cwd=REPO, text=True).strip()
    record(f'PASS fixture {tag}: all {len(expected_files)} cs/json files byte-identical to git, complete file manifest; ' + json.dumps(provenance) + '\n')
    run([dotnet, 'build', f'fixtures/{tag}/Upstream.csproj', '-v:q'])
    run([dotnet, 'build', 'Harness.csproj', '-v:q'])
    command = [dotnet, 'run', '--project', 'Harness.csproj', '--no-build', '--', tag]
    for control, marker in [('logs', 'FAIL actual Harmony skips log '),
                            ('hull', 'FAIL actual hull prefix empty hand resumes original'),
                            ('power', 'FAIL actual config paddling efficiency multiplier 8')]:
        text = run(command, expected=1, negative=control)
        assert marker in text, f'{control} failed somewhere other than intended assertion'
        record(f'PASS process-local {control} negative control fails at intended assertion\n')
    text = run(command)
    match = re.search(r'TOTAL (\d+) assertions passed; 0 failed', text)
    assert match, 'Missing successful runtime assertion total'
    count = sum(line.startswith('PASS ') for line in text.splitlines())
    assert count == int(match[1]), (count, match[1])
    assert hashes() == source_hashes, 'Production source changed during execution; rerun against stable sources'
    record('PASS production source hashes unchanged during build/execution\n')
    record(f'AGGREGATE {count} runtime assertions passed against actual {tag}; 3 process-local negative controls failed as intended; 0 unexpected failures.\n')
