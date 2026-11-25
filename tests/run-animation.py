#!/usr/bin/env python3
"""Native Rough Seas animation/lifecycle regression harness (no synthetic assembly)."""
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TESTS = ROOT / 'tests'
sys.path.insert(0, str(TESTS))
from support import dotnet_root, dependency


def run():
    sdk = str(dotnet_root() / 'dotnet')
    game = dependency('VINTAGE_STORY_DIR', 'VintagestoryAPI.dll')
    fixture = TESTS / 'fixtures/v5.0.1/bin/Debug/net10.0/RoughSeas.dll'
    if not fixture.exists():
        raise SystemExit('Exact Rough Seas fixture is missing. Run tests/setup.py first (not run concurrently).')
    source = ROOT / 'src'
    refs = list(game.glob('*.dll')) + list((game / 'Lib').glob('*.dll')) + list((game / 'Mods').glob('*.dll')) + [fixture]
    missing = [str(p) for p in refs if not p.exists()]
    if missing:
        raise SystemExit('Missing harness dependencies: ' + ', '.join(missing))
    project = TESTS / 'AnimationHarness.csproj'
    project.write_text('''<Project>
  <PropertyGroup><BaseIntermediateOutputPath>obj-animation/</BaseIntermediateOutputPath>
    <MSBuildProjectExtensionsPath>obj-animation/</MSBuildProjectExtensionsPath></PropertyGroup>
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <NoWarn>CS8632</NoWarn><StartupObject>AnimationProgram</StartupObject>
    <OutputPath>bin-animation/</OutputPath>
  </PropertyGroup><ItemGroup>'''
        + ''.join(f'<Compile Include="{p}" />' for p in sorted(source.glob('*.cs')))
        + '<Compile Include="AnimationProgram.cs" /><Compile Include="Program.cs" /><Compile Include="RowingProgram.cs" />'
        + ''.join(f'<Reference Include="{p.stem}"><HintPath>{p}</HintPath></Reference>' for p in refs)
        + '</ItemGroup><Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" /></Project>\n')
    env = dict(os.environ, VINTAGE_STORY_DIR=str(game))
    env['DOTNET_ROOT'] = str(Path(sdk).parent)
    subprocess.run([sdk, 'build', str(project), '-c', 'Release', '--nologo', '-v:q'], cwd=ROOT, env=env, check=True)
    out = TESTS / 'bin-animation/net10.0'
    for p in (game / 'Lib').glob('*.dll'):
        dst = out / p.name
        if not dst.exists():
            dst.symlink_to(p)
    for p in game.glob('*.dll'):
        dst = out / p.name
        if not dst.exists():
            dst.symlink_to(p)
    import hashlib
    import re
    before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')}
    command = [sdk, str(out / 'AnimationHarness.dll')]
    # A requested single-control/RED invocation is useful during development.
    if env.get('QRS_ANIMATION_NEGATIVE') or env.get('QRS_ANIMATION_RED_ONLY'):
        return subprocess.run(command, cwd=ROOT, env=env).returncode
    controls = {
        'draw': 'FAIL DrawnShare owned missing shape finalizer',
        'fire': 'FAIL firestarter Begin uninitialized animation manager',
        'pose': 'FAIL SeatPoses missing local player and uninitialized manager safe',
        'own': 'FAIL OwnOars missing manager guarded',
        'own-finalizer': 'FAIL OwnOars owned lifecycle NRE finalizer',
        'pace': 'FAIL Pace owned metadata NRE finalizer',
        'render': 'FAIL Cargo.CargoRenderer disposed late callback skipped',
        'unload': 'FAIL Shipyard.CollectibleBehaviorCarriedBuild unloaded render safe',
        'fp-pace': 'FAIL independent TP and selected FP pace row-fp',
        'routing-fp': 'FAIL native TP row and FP lookup routing row-fp',
        'routing-ifp': 'FAIL native TP row and FP lookup routing row-ifp',
    }
    for control, marker in controls.items():
        trial = subprocess.run(command, cwd=ROOT,
                               env=dict(env, QRS_ANIMATION_NEGATIVE=control),
                               text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        print(trial.stdout, end='', flush=True)
        if trial.returncode != 1 or marker not in trial.stdout:
            raise SystemExit(f'Negative {control} did not fail the intended behavioral assertion')
        print(f'CONTROL {control}: intended runtime failure verified', flush=True)
    result = subprocess.run(command, cwd=ROOT, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(result.stdout, end='', flush=True)
    if result.returncode:
        return result.returncode
    total = re.search(r'TOTAL (\d+) assertions passed; 0 failed', result.stdout)
    if not total or int(total[1]) != sum(s.startswith('PASS ') for s in result.stdout.splitlines()):
        raise SystemExit('Harness assertion total mismatch')
    after = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')}
    if before != after:
        raise SystemExit('Production sources changed during harness execution')
    print(f'AGGREGATE {total[1]} assertions; {len(controls)} independent negative controls (8 patch removal, 3 fixture routing/isolation); production sources unchanged.')
    return 0


if __name__ == '__main__':
    raise SystemExit(run())
