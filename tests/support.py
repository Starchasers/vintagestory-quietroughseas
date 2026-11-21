"""Resolve caller-supplied, non-redistributed regression dependencies."""
import os
from pathlib import Path
import shutil


def dependency(name, required_file=None):
    value = os.environ.get(name)
    if not value:
        raise SystemExit(f'Set {name}; see tests/README.md.')
    path = Path(value).expanduser().resolve()
    if not path.exists() or (required_file and not (path / required_file).is_file()):
        raise SystemExit(f'Invalid {name}: {path}; see tests/README.md.')
    return path


def dotnet_root():
    value = os.environ.get('DOTNET_ROOT')
    if value:
        return dependency('DOTNET_ROOT', 'dotnet')
    executable = shutil.which('dotnet')
    if not executable:
        raise SystemExit('Install .NET SDK 10 and set DOTNET_ROOT or add dotnet to PATH.')
    return Path(executable).resolve().parent
