from pathlib import Path
import subprocess, json
root=Path(__file__).resolve().parent
from support import dependency
repo=dependency('ROUGH_SEAS_REPO')
vs=dependency('VINTAGE_STORY_DIR', 'VintagestoryAPI.dll')
from xml.sax.saxutils import escape
refs='\n'.join(f'<Reference Include="{escape(p.stem)}"><HintPath>{escape(str(p))}</HintPath><Private>true</Private></Reference>' for p in list(vs.glob('*.dll'))+list((vs/'Lib').glob('*.dll'))+list((vs/'Mods').glob('*.dll')))
for tag in ['v5.0.1']:
 d=root/'fixtures'/tag;d.mkdir(parents=True,exist_ok=True)
 (d/'UPSTREAM-LICENSE').write_bytes(subprocess.check_output(['git','show',f'{tag}:LICENSE'],cwd=repo))
 files=subprocess.check_output(['git','ls-tree','-r','--name-only',tag,'RoughSeas'],cwd=repo,text=True).splitlines()
 for file in files:
  if file.endswith(('.cs','.json')):
   dest=d/file;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(subprocess.check_output(['git','show',f'{tag}:{file}'],cwd=repo))
 (d/'Upstream.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><AssemblyName>RoughSeas</AssemblyName></PropertyGroup><ItemGroup>{refs}<PackageReference Include="OpenTK.Graphics" Version="4.9.4" /></ItemGroup><ItemGroup><EmbeddedResource Include="RoughSeas/Units/voxel.json" LogicalName="RoughSeas.Units.voxel.json" /></ItemGroup></Project>')
 (d/'provenance.json').write_text(json.dumps({'tag':tag,'commit':subprocess.check_output(['git','rev-parse',tag],cwd=repo,text=True).strip(),'source':'git show tag:RoughSeas/...; all cs/json verbatim'},indent=2))
(root/'Harness.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="../src/*.cs"/>{refs}<PackageReference Include="OpenTK.Graphics" Version="4.9.4" /></ItemGroup></Project>')
(root/'CurrentHarness.csproj').write_text(f'<Project><PropertyGroup><BaseIntermediateOutputPath>obj-currents/</BaseIntermediateOutputPath><MSBuildProjectExtensionsPath>obj-currents/</MSBuildProjectExtensionsPath></PropertyGroup><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk"/><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable><OutputPath>bin-currents/</OutputPath></PropertyGroup><ItemGroup><Compile Include="CurrentProgram.cs"/><Compile Include="../src/*.cs"/>{refs}<PackageReference Include="OpenTK.Graphics" Version="4.9.4" /></ItemGroup><Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk"/></Project>')
