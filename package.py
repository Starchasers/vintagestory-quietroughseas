from pathlib import Path
import hashlib,json,zipfile
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'dist';OUT.mkdir(exist_ok=True)
def pack(name,files):
 with zipfile.ZipFile(OUT/name,'w',zipfile.ZIP_DEFLATED) as z:
  for p in sorted(files):z.write(p,p.relative_to(ROOT).as_posix())
 with zipfile.ZipFile(OUT/name) as z:
  assert z.testzip() is None
  for p in files:assert z.read(p.relative_to(ROOT).as_posix())==p.read_bytes()
 return {'path':str(OUT/name),'sha256':hashlib.sha256((OUT/name).read_bytes()).hexdigest(),'bytes':(OUT/name).stat().st_size,'entries':len(files)}
modfiles=[ROOT/'modinfo.json',ROOT/'README.md',*sorted((ROOT/'src').glob('*.cs'))]
assert json.loads((ROOT/'modinfo.json').read_text())['version']=='1.8.0'
config=json.loads((ROOT/'config/quietroughseas.json').read_text())
assert config=={'RiversPushBoats':False,'RiversPushPlayers':False,'LightningDamagesAnimals':False,'StrokePowerMultiplier':{'paddling':8.0,'sculling':8.0}}
results=[pack('quietroughseas_1.8.0.zip',modfiles)]
copy=OUT/'quietroughseas.json';copy.write_bytes((ROOT/'config/quietroughseas.json').read_bytes())
results.append({'path':str(copy),'sha256':hashlib.sha256(copy.read_bytes()).hexdigest(),'bytes':copy.stat().st_size})
files=[ROOT/'README.md',ROOT/'modinfo.json',ROOT/'package.py',*sorted((ROOT/'src').glob('*.cs')),*sorted((ROOT/'config').glob('*'))]
for p in (ROOT/'tests').rglob('*'):
 if p.is_file() and not any(x in ('bin','obj','__pycache__') or x.startswith(('bin-','obj-')) for x in p.parts) and (p.suffix in ('.cs','.json','.csproj','.py','.md','.txt') or p.name=='UPSTREAM-LICENSE'):files.append(p)
for p in (ROOT/'research').glob('*.md'):files.append(p)
results.append(pack('quietroughseas_1.8.0-source-tests.zip',files))
(OUT/'manifest.json').write_text(json.dumps(results,indent=2)+'\n')
print(json.dumps(results,indent=2));print('PASS ZIP CRC checks, entry content equality, source inclusion, 1.8.0 metadata and exact matching config')
