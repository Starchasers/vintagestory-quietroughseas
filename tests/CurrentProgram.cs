using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Collections.Generic;
using HarmonyLib;
using QuietRoughSeas;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

public class CurrentProxy : DispatchProxy {
 public System.Func<MethodInfo, object[], object> Handler;
 protected override object Invoke(MethodInfo m, object[] a) => Handler(m,a);
 public static T Make<T>(System.Func<MethodInfo,object[],object> f) where T:class {var p=Create<T,CurrentProxy>(); ((CurrentProxy)(object)p).Handler=f;return p;}
 public static object Default(MethodInfo m)=>m.ReturnType==typeof(void)?null:m.ReturnType.IsValueType?Activator.CreateInstance(m.ReturnType):null;
}
class CurrentMod : Mod {}
class RegisteredHull : EntityAgent { public double ForwardSpeed; }
static class CurrentProgram {
 static int passed; static double cx,cz; static float[] flow=new float[2048]; static int chunkReads,flowReads,samples;
 static readonly BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static MethodInfo Q(string n)=>typeof(QuietRoughSeasSystem).GetMethod(n,All);
 static void Check(bool b,string n){if(!b)throw new Exception("FAIL "+n);passed++;Console.WriteLine("PASS "+n);}
 static bool Near(double a,double b)=>Math.Abs(a-b)<1e-8;
 static object Raw(Type t)=>RuntimeHelpers.GetUninitializedObject(t);
 static void Field(object o,string n,object v){for(var t=o.GetType();t!=null;t=t.BaseType){var f=t.GetField(n,All|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(o,v);return;}}throw new Exception("field "+n);}
 static bool Sample<T>(ref T __result){samples++;__result=(T)Activator.CreateInstance(typeof(T),new object[]{cx,cz,10d,null});return false;}
 static (double,double) Expected(double x,double z,double yaw,double speed){var sign=speed<0?-1:1;var ux=Math.Sin(yaw)*sign;var uz=Math.Cos(yaw)*sign;var d=Math.Max(0,x*ux+z*uz);return(d*ux,d*uz);}
 static string Snapshot(MethodBase m){var i=Harmony.GetPatchInfo(m);if(i==null)return "none";return string.Join("|",i.Prefixes.Concat(i.Postfixes).Concat(i.Transpilers).Concat(i.Finalizers).Select(p=>$"{p.owner}:{p.PatchMethod.DeclaringType.FullName}.{p.PatchMethod.Name}:{p.priority}:{p.index}").OrderBy(x=>x));}
 static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(string[] args){
 var tag=args[0];var neg=Environment.GetEnvironmentVariable("QRS_CURRENT_NEGATIVE");
 var upstream=AssemblyLoadContext.Default.LoadFromAssemblyPath(System.IO.Path.GetFullPath($"fixtures/{tag}/bin/Debug/net10.0/RoughSeas.dll"));
 var rivers=AssemblyLoadContext.Default.LoadFromAssemblyPath(System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("RIVERS_DLL") ?? throw new InvalidOperationException("Set RIVERS_DLL")));
 var drift=upstream.GetType("RoughSeas.Floating.EntityBehaviorCurrentDrift",true);var current=drift.GetMethod("Current",All);
 var seaType=upstream.GetType("RoughSeas.SeaAndWeather.SeaWeatherSystem",true);var sampler=seaType.GetMethod("CurrentAt",All);
 var riverPost=rivers.GetTypes().Single(t=>t.Name=="UpdateBoatAngleAndMotionPostfix"&&t.DeclaringType?.Name=="EntityBehaviorPhysicsPatch").GetMethod("Postfix",All);
 var swim=rivers.GetTypes().Single(t=>t.Name=="DoApplyPrefix"&&t.DeclaringType?.Name=="EntityBehaviorPhysicsPatch").GetMethod("Prefix",All);
 Console.WriteLine($"Headless currents {tag}; actual RS private Current and Rivers 5.0.3 binary (assembly {rivers.GetName().Version}) nested Postfix(EntityBoat); world/chunk interface mocks; sea sampling boundary mocked.");
 var h=new Harmony("headless.current.fixtures");
 h.Patch(sampler,prefix:new HarmonyMethod(typeof(CurrentProgram).GetMethod("Sample",All).MakeGenericMethod(sampler.ReturnType)));
 var boatTarget=AccessTools.Method(typeof(EntityBoat),"updateBoatAngleAndMotion");
 new Harmony("rivers.flow.headless").Patch(boatTarget,postfix:new HarmonyMethod(riverPost));
 var swimTarget=AccessTools.Method(typeof(PModuleInLiquid),"DoApply");
 new Harmony("rivers.flow.headless").Patch(swimTarget,prefix:new HarmonyMethod(swim));
 var beforeSwim=Snapshot(swimTarget);var beforeSwimMethod=Snapshot(swim);var beforeBoat=Snapshot(boatTarget);
 var config=new QuietRoughSeasConfig();var worldConfig=new TreeAttribute();var logger=CurrentProxy.Make<ILogger>((m,a)=>{if(m.Name.Contains("Warning"))Console.WriteLine("LOG "+a[0]);return CurrentProxy.Default(m);});
 var chunk=CurrentProxy.Make<IWorldChunk>((m,a)=>{if(m.Name=="GetModdata"&&m.IsGenericMethod){flowReads++;return flow;}return CurrentProxy.Default(m);});
 bool chunkPresent=true;
 int vanillaBlockReads=0;
 var blocks=CurrentProxy.Make<IBlockAccessor>((m,a)=>{if(m.Name=="GetBlock")vanillaBlockReads++;if(m.Name=="GetChunk"){chunkReads++;return chunkPresent?chunk:null;}return CurrentProxy.Default(m);});
 var world=CurrentProxy.Make<IWorldAccessor>((m,a)=>m.Name=="get_Config"?worldConfig:m.Name=="get_BlockAccessor"?blocks:CurrentProxy.Default(m));
 Mod MakeMod(Assembly a){var mod=(Mod)Raw(typeof(CurrentMod));var sys=(ModSystem)Raw(a.GetTypes().First(t=>!t.IsAbstract&&typeof(ModSystem).IsAssignableFrom(t)));Field(mod,"<Systems>k__BackingField",new ModSystem[]{sys});Field(mod,"<Logger>k__BackingField",logger);return mod;}
 var rsMod=MakeMod(upstream);var rvMod=MakeMod(rivers);
 var loader=CurrentProxy.Make<IModLoader>((m,a)=>m.Name=="GetMod"?(string)a[0]=="roughseas"?rsMod:(string)a[0]=="rivers"?rvMod:null:CurrentProxy.Default(m));
 var api=CurrentProxy.Make<ICoreAPI>((m,a)=>m.Name=="get_World"?world:m.Name=="get_ModLoader"?loader:m.Name=="get_Side"?EnumAppSide.Server:m.Name=="get_Logger"?logger:CurrentProxy.Default(m));
 var system=new QuietRoughSeasSystem();typeof(ModSystem).GetProperty("Mod").SetValue(system,rsMod);Q("LoadHarmony").Invoke(system,null);
 Q("InstallCurrents").Invoke(system,new object[]{api,upstream.GetTypes().ToList()});
 if(neg=="river-prefix") {h.Unpatch(riverPost,Q("SnapshotRiverBoatPush"));}
 if(neg=="river-postfix")h.Unpatch(riverPost,Q("HelpfulRiverBoatPushPostfix"));
 if(neg=="rs-postfix")h.Unpatch(current,Q("HelpfulRoughCurrentPostfix"));
 if(neg==null){Check(Harmony.GetPatchInfo(current)?.Postfixes.Any(p=>p.PatchMethod==Q("HelpfulRoughCurrentPostfix"))==true,"reflection HelpfulCurrents -> Apply installs actual RS postfix");Check(Harmony.GetPatchInfo(riverPost)?.Prefixes.Any(p=>p.PatchMethod==Q("SnapshotRiverBoatPush"))==true&&Harmony.GetPatchInfo(riverPost).Postfixes.Any(p=>p.PatchMethod==Q("HelpfulRiverBoatPushPostfix")),"reflection Apply installs Rivers capture prefix and filter postfix");}
 var boat=new EntityBoat();boat.Api=api;boat.World=world;
 var behavior=Activator.CreateInstance(drift,new object[]{boat});Field(behavior,"sea",Raw(seaType));
 rivers.GetTypes().Single(t=>t.Name=="RiversMod").GetProperty("ClientRiverSpeed",All).SetValue(null,1f);
 var cache=rivers.GetTypes().Single(t=>t.Name=="ModDataCache");
 void SetFlow(double x,double z){cache.GetMethod("OnServerExit",All).Invoke(null,null);Array.Fill(flow,(float)x,0,1024);Array.Fill(flow,(float)z,1024,1024);}
 (double,double) Sea(double x,double z,double yaw,double speed){cx=x;cz=z;boat.Pos.Yaw=(float)yaw;boat.ForwardSpeed=speed;return ((double X,double Z))current.Invoke(behavior,null);}
 (double,double,double) River(double x,double z,double yaw,double speed,double ix=0.7,double iy=0.35,double iz=-0.4){boat.Pos.Yaw=(float)yaw;boat.ForwardSpeed=speed;boat.Pos.Motion.Set(ix,iy,iz);SetFlow(x,z);riverPost.Invoke(null,new object[]{boat});return(boat.Pos.Motion.X,boat.Pos.Motion.Y,boat.Pos.Motion.Z);}
 var cases=new (string,double,double,double,double)[]{("along/downstream",0,2,0,1),("opposed",0,-2,0,1),("lateral",2,0,0,1),("helpful diagonal",2,3,0,1),("opposed diagonal",2,-3,0,1),("yaw quarter turn",2,3,Math.PI/2,1),("reverse",2,-3,0,-1),("rotated reverse",-2,3,Math.PI/2,-1),("idle",2,3,0,0),("zero current",0,0,0,1)};
 foreach(var c in cases){var (_,x,z,yaw,speed)=c;var e=Expected(x,z,(float)yaw,speed);var a=Sea(x,z,yaw,speed);Check(Near(a.Item1,e.Item1)&&Near(a.Item2,e.Item2),"RS actual Current "+c.Item1);if(speed==0)e=(0,0);var r=River(x,z,yaw,speed);Check(Near(r.Item1,.7+e.Item1*.02)&&Near(r.Item2,.35)&&Near(r.Item3,-.4+e.Item2*.02),"Rivers actual Postfix "+c.Item1+" preserves propulsion/vertical");}
 // Composite same-boat path: actual RS Current -> HullSet.Relax/Rowed -> real Rivers postfix.
// Vanilla propulsion reset is explicit; no game tick/render/network runtime is claimed.
var hullSetType=upstream.GetType("RoughSeas.Floating.HullSet",true);
var waterType=upstream.GetType("RoughSeas.Floating.WaterOnHull",true);
var gradientType=upstream.GetType("RoughSeas.Storms.FlowGradient",true);
double ticks=Convert.ToDouble(upstream.GetTypes().Single(t=>t.Name=="WaterDrag").GetField("TicksPerSecond",All).GetRawConstantValue());
foreach(var c in cases){
 var (_,x,z,yaw,speed)=c;var rs=Sea(x,z,yaw,speed);var hs=Activator.CreateInstance(hullSetType);
 var water=Activator.CreateInstance(waterType,new object[]{rs.Item1,rs.Item2,0d,0d,Activator.CreateInstance(gradientType),0d});
 hullSetType.GetMethod("Relax").Invoke(hs,new object[]{water,(double)boat.Pos.Yaw,1d,0.1d,(0d,0d)});
 var re=Expected(x,z,(float)yaw,speed);var riverExpected=speed==0?(0d,0d):re;
 for(int repeat=0;repeat<2;repeat++){
  boat.Pos.Motion.Set(.7,.35,-.4);hullSetType.GetMethod("Rowed").Invoke(hs,new object[]{boat.Pos.Motion,true});SetFlow(x,z);riverPost.Invoke(null,new object[]{boat});
  Check(Near(boat.Pos.Motion.X,.7+re.Item1/ticks+riverExpected.Item1*.02)&&Near(boat.Pos.Motion.Y,.35)&&Near(boat.Pos.Motion.Z,-.4+re.Item2/ticks+riverExpected.Item2*.02),"composite RS HullSet then actual Rivers "+c.Item1+" reset cycle "+repeat+" exactly one contribution each");
 }
}
typeof(QuietRoughSeasSystem).GetField("riversPushBoats",All).SetValue(null,true);
 var unfiltered=Sea(2,-3,0,1);Check(unfiltered==(2d,-3d),"toggle true retains actual RS hindering/lateral current");var ur=River(2,-3,0,1);Check(Near(ur.Item1,.74)&&Near(ur.Item2,.35)&&Near(ur.Item3,-.46),"toggle true retains actual Rivers delta and initial motion");
 typeof(QuietRoughSeasSystem).GetField("riversPushBoats",All).SetValue(null,false);
 var r1=River(2,3,0,1,99,.7,-88);var r2=River(2,3,0,1,-77,-.2,66);Check(Near(r1.Item1-99,r2.Item1+77)&&Near(r1.Item3+88,r2.Item3-66)&&Near(r1.Item2,.7)&&Near(r2.Item2,-.2),"heading rather than existing drift controls actual Rivers projection");
 var sea1=Sea(2,3,0,1);boat.Pos.Motion.Set(100,80,-90);var sea2=Sea(2,3,0,1);Check(sea1==sea2,"heading/drift independence actual RS Current");
 worldConfig.SetBool("oceanCurrents",false);var sampleBefore=samples;Check(Sea(2,3,0,1)==(0d,0d)&&samples==sampleBefore,"RS original Current world toggle executes before postfix");worldConfig.SetBool("oceanCurrents",true);
 chunkPresent=false;var absent=River(2,3,0,1);Check(Near(absent.Item1,.7)&&Near(absent.Item3,-.4),"Rivers missing chunk no-op");chunkPresent=true;
 Check(chunkReads>0&&flowReads>0,"actual Rivers world -> GetChunk -> GetModdata<float[]> flow path executed");
 Check(beforeSwim==Snapshot(swimTarget)&&beforeSwimMethod==Snapshot(swim)&&!Snapshot(swimTarget).Contains("QuietRoughSeas"),"Rivers actual swimmer physics patch list unchanged");
 Check(beforeBoat==Snapshot(boatTarget),"Rivers existing boat patch registration untouched");
 Check(Q("SnapshotRiverBoatPush").GetParameters()[0].Name=="__0"&&Q("HelpfulRiverBoatPushPostfix").GetParameters()[0].Name=="__0"&&riverPost.GetParameters()[0].Name=="__instance","actual Rivers target argument named __instance; runtime Harmony __0 injection and __state round-trip validated");
 var animal=new RegisteredHull { ForwardSpeed=-1 };animal.Api=api;animal.World=world;var animalBehavior=Activator.CreateInstance(drift,new object[]{animal});Field(animalBehavior,"sea",Raw(seaType));cx=2;cz=-3;var animalCurrent=((double X,double Z))current.Invoke(animalBehavior,null);Check(animalCurrent==(0d,-3d),"RS actual Current registered non-EntityBoat copy uses reflected reverse ForwardSpeed");
 var pzero=QuietRoughSeasSystem.ProjectHelpfulCurrent(2,3,0,0);Check(pzero==(0d,0d),"pure projection zero heading rejected");
 var pinvalid=QuietRoughSeasSystem.ProjectHelpfulCurrent(double.NaN,3,0,1);var hinvalid=QuietRoughSeasSystem.ProjectHelpfulCurrent(2,3,double.PositiveInfinity,1);Check(pinvalid==(0d,0d)&&hinvalid==(0d,0d),"pure projection nonfinite current/heading rejected");
 var pscaled=QuietRoughSeasSystem.ProjectHelpfulCurrent(2,3,0,200);Check(pscaled==(0d,3d),"pure projection nonunit heading normalized");
 var rng=new Random(50346);
 for(int i=0;i<2000;i++){double yaw=(rng.NextDouble()*20-10),speed=rng.Next(2)==0?-1:1,x=rng.NextDouble()*20-10,z=rng.NextDouble()*20-10;boat.Pos.Yaw=(float)yaw;boat.ForwardSpeed=speed;var projected=QuietRoughSeasSystem.ProjectHelpfulCurrent(x,z,Math.Sin(boat.Pos.Yaw)*speed,Math.Cos(boat.Pos.Yaw)*speed);double ox=projected.X,oz=projected.Z;var ux=Math.Sin(boat.Pos.Yaw)*speed;var uz=Math.Cos(boat.Pos.Yaw)*speed;double dot=ox*ux+oz*uz;var exp=Expected(x,z,boat.Pos.Yaw,speed);if(!Near(ox,exp.Item1)||!Near(oz,exp.Item2)||dot < -1e-9||Math.Abs(ox*uz-oz*ux)>1e-8||ox*ox+oz*oz>x*x+z*z+1e-8)throw new Exception("FAIL randomized geometry "+i);var b=QuietRoughSeasSystem.ProjectHelpfulCurrent(ox,oz,ux,uz);if(!Near(b.X,ox)||!Near(b.Z,oz))throw new Exception("FAIL idempotence "+i);}
 Check(true,"2000 seeded pure projection cases: independent oracle, nonnegative along, no lateral, norm nonincrease, idempotence");
 // Narrow player-current opt-out: exercise the REAL Rivers prefix and REAL patched vanilla DoApply.
  Q("InstallRiverPlayers").Invoke(system,new object[]{api});
  if(neg=="player-prefix")h.Unpatch(swim,Q("RiverPlayerCurrentPrefix"));
  var player=new EntityPlayer();player.Api=api;player.World=world;
  var pos=new EntityPos().SetPos(4,7,9);var controls=new EntityControls();var liquid=new PModuleInLiquid();
  foreach(float dt in new[]{.01f,.04f}){
   pos.Motion.Set(.7,.35,-.4);SetFlow(2,-3);int chunks=chunkReads;var result=(bool)swim.Invoke(null,new object[]{liquid,dt,player,pos,controls});
   Check(result&&Near(pos.Motion.X,.7)&&Near(pos.Motion.Y,.35)&&Near(pos.Motion.Z,-.4)&&chunkReads==chunks,"player currents false skips only actual Rivers body and returns vanilla continuation true dt="+dt);
   int reads=vanillaBlockReads;swimTarget.Invoke(liquid,new object[]{dt,player,pos,controls});
   Check(vanillaBlockReads==reads+1&&Near(pos.Motion.X,.7)&&Near(pos.Motion.Z,-.4)&&Near(pos.X,4)&&Near(pos.Y,7)&&Near(pos.Z,9),"real vanilla DoApply still runs once with unchanged position dt="+dt);
  }
  typeof(QuietRoughSeasSystem).GetField("riversPushPlayers",All).SetValue(null,true);
  pos.Motion.Set(.7,.35,-.4);SetFlow(2,-3);var originalResult=(bool)swim.Invoke(null,new object[]{liquid,.02f,player,pos,controls});
  Check(originalResult&&pos.Motion.X>.7&&pos.Motion.Z<-.4&&Near(pos.Motion.Y,.35),"player currents true retains actual Rivers original delta");
  double dx=pos.Motion.X-.7,dz=pos.Motion.Z+.4;pos.Motion.Set(.7,.35,-.4);controls.ShiftKey=true;SetFlow(2,-3);swim.Invoke(null,new object[]{liquid,.04f,player,pos,controls});
  Check(Near(pos.Motion.X-.7,dx)&&Near(pos.Motion.Z+.4,dz),"true mode preserves actual dt scaling and shift modifier");
  typeof(QuietRoughSeasSystem).GetField("riversPushPlayers",All).SetValue(null,false);
  var nonplayer=new EntityAgent();pos.Motion.Set(.7,.35,-.4);int beforeChunks=chunkReads;var nonResult=(bool)swim.Invoke(null,new object[]{liquid,.02f,nonplayer,pos,controls});
  Check(nonResult&&chunkReads==beforeChunks&&Near(pos.Motion.X,.7)&&Near(pos.Motion.Y,.35)&&Near(pos.Motion.Z,-.4),"nonplayers preserve actual Rivers prefix no-op");
  Check(Snapshot(swimTarget)==beforeSwim&&Snapshot(boatTarget)==beforeBoat,"player option preserves vanilla target and boat patch registrations");
  Console.WriteLine($"TOTAL {passed} assertions passed; 0 failed; 2000 randomized cases.");
 }
}