using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Newtonsoft.Json;
using QuietRoughSeas;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

public class Fake : DispatchProxy {
 public State State; public Type Interface;
 protected override object Invoke(MethodInfo m, object[] args) => State.Invoke(Interface,m,args);
}
public class State {
 public readonly List<string> Calls = new();
 public readonly List<string> Logs = new();
 public readonly Dictionary<Type,object> Objects = new();
 public readonly Dictionary<Type,Delegate> Receivers = new();
 public readonly List<string> Channels = new(); public readonly List<Type> PacketTypes = new();
 public Mod Target; public EnumAppSide Side; public System.Func<Type,object> Resolve;
 public object Get(Type t) { if(!Objects.TryGetValue(t,out var p)) { p=DispatchProxy.Create(t,typeof(Fake)); ((Fake)p).State=this;((Fake)p).Interface=t;Objects[t]=p;}return p; }
 public object Invoke(Type t, MethodInfo m, object[] a) {
  Calls.Add(t.Name+"."+m.Name);
  if(m.Name=="RegisterChannel")Channels.Add((string)a[0]);
  if(m.Name=="RegisterMessageType")PacketTypes.Add(m.GetGenericArguments()[0]);
  if(m.Name=="get_Player") return null; // Headless world: there is no local player entity.
  if(m.Name=="get_Side") return Side;
  if(m.Name=="GetMod") return (string)a[0]=="roughseas"?Target:null;
  if(m.Name=="GetModSystem" && m.IsGenericMethod) return Resolve!=null?Resolve(m.GetGenericArguments()[0]):Activator.CreateInstance(m.GetGenericArguments()[0]);
  if(m.Name=="SetMessageHandler") Receivers[m.GetGenericArguments()[0]]=(Delegate)a[0];
  if(t==typeof(ILogger)) {Logs.Add(m.Name+":"+string.Join(" ",a.Select(x=>x is object[] z?string.Join(" ",z.Select(y=>y?.ToString())):x?.ToString())));return null;}
  var r=m.ReturnType;
  if(r==typeof(void)) return null;
  if(r==typeof(long)) return 42L;
  if(r==typeof(bool)) return false;
  if(r.IsInterface) return Get(r);
  return r.IsValueType?Activator.CreateInstance(r):null;
 }
 public int Count(string suffix)=>Calls.Count(x=>x.EndsWith("."+suffix));
}
class TestMod : Mod { }
class Program {
 static readonly Type ModType=typeof(TestMod);
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static readonly Type Q=typeof(QuietRoughSeasSystem);
 static int assertions; static readonly Dictionary<MethodBase,bool> Ran=new();
 static void Assert(bool v,string name) { if(!v) throw new Exception("FAIL "+name); assertions++;Console.WriteLine("PASS "+name); }
 static void After(MethodBase __originalMethod,bool __runOriginal) => Ran[__originalMethod]=__runOriginal;
 static MethodInfo Method(Type t,string n)=>t.GetMethods(Any).Single(m=>m.Name==n);
 static object Call(object o,string n,params object[] a) => Method(o is Type t?t:o.GetType(),n).Invoke(o is Type?null:o,a);
 static void Set(object o,string n,object v) {var t=o is Type z?z:o.GetType();FieldInfo f=null;for(var b=t;b!=null&&f==null;b=b.BaseType)f=b.GetField(n,Any)??b.GetField("<"+n+">k__BackingField",Any);if(f==null)throw new Exception("Missing fixture field "+t+"."+n);f.SetValue(o is Type?null:o,v);}
 static object Get(object o,string n) {var t=o is Type z?z:o.GetType();var f=t.GetField(n,Any);return f!=null?f.GetValue(o is Type?null:o):t.GetProperty(n,Any).GetValue(o is Type?null:o);}
 static Mod MakeMod(string version,ILogger logger,params ModSystem[] systems) {
  var m=(Mod)RuntimeHelpers.GetUninitializedObject(ModType);
  Set(m,"Info",new ModInfo{Version=version});Set(m,"Logger",logger);Set(m,"Systems",systems);foreach(var s in systems)Set(s,"Mod",m);return m;
 }
 static object ReadConfig(Assembly asm,string root,string type,string file)=>JsonConvert.DeserializeObject(System.IO.File.ReadAllText(System.IO.Path.Combine(root,"RoughSeas/assets/roughseas/config/"+file+".json")),asm.GetType(type,true));
 static void Main(string[] args) {
  try { Run(args[0]);Console.WriteLine($"TOTAL {assertions} assertions passed; 0 failed"); }
  catch(Exception e) {Console.WriteLine(e);Environment.ExitCode=1;}
 }
 static string Negative => Environment.GetEnvironmentVariable("QRS_NEGATIVE_CONTROL");
 static bool OwnPatch(MethodBase m) => Harmony.GetPatchInfo(m)?.Owners.Contains("quietroughseas") == true;
 static void Run(string tag) {
  Assert(tag=="v5.0.1","runner selects exact v5.0.1 fixture");
  string root=System.IO.Path.GetFullPath("fixtures/"+tag);
  var asm=AssemblyLoadContext.Default.LoadFromAssemblyPath(root+"/bin/Debug/net10.0/RoughSeas.dll");
  Console.WriteLine("FIXTURE "+tag+" full upstream assembly; actual game API "+typeof(ModSystem).Assembly.GetName().Version+"; Harmony "+typeof(Harmony).Assembly.GetName().Version);
  Console.WriteLine("LIMIT: headless API DispatchProxy doubles; no game world, physics tick, rendering or in-game test.");
  var state=new State{Side=EnumAppSide.Client};var api=(ICoreClientAPI)state.Get(typeof(ICoreClientAPI));
  var logger=(ILogger)state.Get(typeof(ILogger));
  var propulsionType=asm.GetType("RoughSeas.Propulsion.PropulsionModSystem",true);
  var propulsion=(ModSystem)Activator.CreateInstance(propulsionType);
  Set(propulsion,"Config",ReadConfig(asm,root,"RoughSeas.Propulsion.PropulsionConfig","propulsion"));
  state.Target=MakeMod("5.0.1",logger,propulsion);
  var quiet=new QuietRoughSeasSystem();MakeMod("test",logger,quiet);
  Assert(QuietRoughSeasSystem.SupportedVersion("5.0.1"),"version accepts exact 5.0.1");
  var originalTarget=state.Target;
  var strokes=(IDictionary)Get(Get(propulsion,"Config"),"Strokes");
  var before=strokes.Keys.Cast<string>().ToDictionary(k=>k,k=>(double)Get(strokes[k],"GrossEfficiency"));
  foreach(var v in new[]{"4.5.0","4.6.0","5.0","5.0.0","5.0.2","5.1.0","5.0.1-rc.1","5.0.1+build","v5.0.1","5.0.1 "," 5.0.1","",null}) {
   Assert(!QuietRoughSeasSystem.SupportedVersion(v),"version rejects "+(v??"<null>"));
   state.Target=MakeMod(v,logger,propulsion);quiet.StartPre(api);quiet.AssetsFinalize(api);
   Assert(!(bool)Get(Q,"patched")&&!Harmony.GetAllPatchedMethods().Any(),"unsupported lifecycle installs no patches "+(v??"<null>"));
   Assert(state.Count("StoreModConfig")==0&&((IList)Get(Q,"scaledStrokes")).Count==0,"unsupported lifecycle writes no config or power "+(v??"<null>"));
   Assert(before.All(kv=>(double)Get(strokes[kv.Key],"GrossEfficiency")==kv.Value),"unsupported lifecycle preserves actual stroke efficiencies "+(v??"<null>"));
  }
  state.Target=null;quiet.StartPre(api);quiet.AssetsFinalize(api);
  Assert(!(bool)Get(Q,"patched")&&!Harmony.GetAllPatchedMethods().Any()&&state.Count("StoreModConfig")==0,"missing Rough Seas lifecycle is inert");
  RowingChecks.Red(asm,Assert);
  state.Target=originalTarget;state.Logs.Clear();quiet.StartPre(api);
  if(Environment.GetEnvironmentVariable("QRS_NEGATIVE_CONTROL")=="rowing")
   foreach(string name in new[]{"Pace","DrawnShare","Finish"}) new Harmony("negative.rowing").Unpatch(asm.GetType("RoughSeas.Propulsion.MountStrokes").GetMethod(name,Any),HarmonyPatchType.Transpiler,"quietroughseas");
  try { RowingChecks.Green(asm,Assert); } catch(Exception e) { throw new Exception("FAIL rowing lifecycle readiness",e); }
  Assert((bool)Get(Q,"patched"),"supported StartPre installs via production reflection Harmony loader");
  if(state.Logs.Any(l=>l.StartsWith("Warning:"))) Console.WriteLine(string.Join("\n",state.Logs));
  Assert(!state.Logs.Any(l=>l.StartsWith("Warning:")&&!l.StartsWith("Warning:Cosmetic lifecycle guard ")),"no supported-install warnings");
  Assert(state.Count("StoreModConfig")==1,"supported StartPre writes config exactly once");
  Assert(typeof(QuietRoughSeasConfig).GetField("DisableRealisticSwimming")==null,"removed swimming config absent");
  Assert(!Q.GetMethods(Any).Any(m=>m.Name=="InstallSwimming"||m.Name=="AllowPersonAttachment"||m.Name=="SwimmingTargets"),"no custom swimming hook methods remain");
  var types=asm.GetTypes().ToList();
  var logs=(List<MethodInfo>)Call(Q,"FindLogMethods",types);
  var swings=(List<MethodInfo>)Call(Q,"FindSwingMethods",types);
  var current=Method(asm.GetType("RoughSeas.Floating.EntityBehaviorCurrentDrift",true),"Current");
  Assert(OwnPatch(current),"Floating namespace Current patched");
  Assert(OwnPatch(ReceiveDamage),"Entity.ReceiveDamage patched for lightning");
  var rowing=asm.GetType("RoughSeas.Propulsion.MountStrokes").GetMethods(Any).Where(m=>new[]{"Pace","DrawnShare","Finish"}.Contains(m.Name));
  var motion=new MethodBase[]{Method(asm.GetType("RoughSeas.Person.BodyAfloat"),"Afloat"),Method(asm.GetType("RoughSeas.Floating.VesselMotion"),"Lift"),Method(typeof(Vintagestory.GameContent.EntityBehaviorPlayerPhysics),"OnPhysicsTick"),Method(Type.GetType("Vintagestory.Client.NoObf.NetworkAPI, VintagestoryLib",true),"SendPlayerPositionPacket"),Method(Type.GetType("Vintagestory.Client.NoObf.NetworkAPI, VintagestoryLib",true),"SendPlayerMountPositionPacket")};
  var cosmetics = new List<MethodBase>();
  foreach(var spec in new[]{"Propulsion.MountStrokes:Show", "Vessels.OwnOars:Follow,Match,Stop", "Vessels.SeatPoses:Show", "Shipyard.CollectibleBehaviorLightWithFirestarter:Begin,End", "Shipyard.EntityBehaviorBurnout:Chip", "Cargo.CargoRenderer:Show,OnRenderFrame,Dispose", "Sailing.RigRenderer:Shaped,OnRenderFrame,Dispose", "Storms.BilgeRenderer:Show,OnRenderFrame,Dispose", "Shipyard.CollectibleBehaviorIcon:OnBeforeRender,OnUnloaded", "Shipyard.CollectibleBehaviorCarriedBuild:OnBeforeRender,OnUnloaded"}) { var pair=spec.Split(':'); foreach(var name in pair[1].Split(',')) cosmetics.Add(Method(asm.GetType("RoughSeas."+pair[0]),name)); }
  var allowed=logs.Cast<MethodBase>().Concat(swings).Append(current).Append(ReceiveDamage).Concat(rowing).Concat(motion).Concat(cosmetics).ToHashSet();
  var patched=Harmony.GetAllPatchedMethods().Where(OwnPatch).ToArray();
  Assert(patched.Length==allowed.Count&&patched.All(allowed.Contains),"exact patch allowlist: existing compatibility plus five finite-motion boundaries; no global animation patches");
  foreach(var t in types.Where(t=>t.Namespace=="RoughSeas.Person"||t.Name.Contains("Breath")||t.Name.Contains("Wave")||t.Name=="DrawnWater"||t.Name=="DrawnSea"))
   Assert(!t.GetMethods(Any|BindingFlags.DeclaredOnly).Any(m=>m.Name!="Log"&&!(t.FullName=="RoughSeas.Person.BodyAfloat"&&m.Name=="Afloat")&&OwnPatch(m)),"no unrelated swimming breath wave hooks on "+t.FullName);
  var counts=patched.ToDictionary(m=>m,m=>Harmony.GetPatchInfo(m).Prefixes.Count(p=>p.owner=="quietroughseas")+Harmony.GetPatchInfo(m).Postfixes.Count(p=>p.owner=="quietroughseas")+Harmony.GetPatchInfo(m).Transpilers.Count(p=>p.owner=="quietroughseas"));
  quiet.StartPre(api);
  var serverState=new State{Side=EnumAppSide.Server,Target=state.Target};
  var serverQuiet=new QuietRoughSeasSystem();MakeMod("test",logger,serverQuiet);serverQuiet.StartPre((ICoreAPI)serverState.Get(typeof(ICoreServerAPI)));
  Assert(counts.All(kv=>Harmony.GetPatchInfo(kv.Key).Prefixes.Count(p=>p.owner=="quietroughseas")+Harmony.GetPatchInfo(kv.Key).Postfixes.Count(p=>p.owner=="quietroughseas")+Harmony.GetPatchInfo(kv.Key).Transpilers.Count(p=>p.owner=="quietroughseas")==kv.Value),"repeated client/server startup never duplicates patches");
  Assert(state.Count("StoreModConfig")==1&&serverState.Count("StoreModConfig")==0,"process-wide startup config remains once only");
  if(Negative=="logs"||Negative=="hull") {
   var victim=Negative=="logs"?logs.First():swings.Single();
   new Harmony("headless-negative-control").Unpatch(victim,HarmonyPatchType.Prefix,"quietroughseas");
   Console.WriteLine("NEGATIVE CONTROL: removed production "+Negative+" prefix in this fresh process");
  }
  var probe=new Harmony("headless-regression-probe");
  TestLogsAndHull(asm,types,probe);
  TestPower(quiet,api,propulsion,state);
  TestLightning(asm,probe);
  serverQuiet.AssetsFinalize((ICoreAPI)serverState.Get(typeof(ICoreServerAPI)));
  Assert(before.All(kv=>(double)Get(strokes[kv.Key],"GrossEfficiency")==kv.Value*(kv.Key=="paddling"||kv.Key=="sculling"?8:1)),"shared client/server stroke objects never double-scale");
 }
 static void TestLogsAndHull(Assembly asm,List<Type> types,Harmony probe) {
  var logs=(List<MethodInfo>)Call(Q,"FindLogMethods",types);
  Assert(logs.Count>0,"discovers upstream log targets");
  foreach(var m in logs) {
   probe.Patch(m,postfix:new HarmonyMethod(typeof(Program).GetMethod("After",Any)));
   var obj=m.IsStatic?null:Activator.CreateInstance(m.DeclaringType.IsAbstract?types.First(t=>!t.IsAbstract&&m.DeclaringType.IsAssignableFrom(t)):m.DeclaringType);
   bool threw=false;try{m.Invoke(obj,m.GetParameters().Select(p=>p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray());}catch(TargetInvocationException){threw=true;}
   Assert(!threw&&Ran.TryGetValue(m,out var ran)&&!ran,"actual Harmony skips log "+m.DeclaringType.Name);
  }
  var swings=(List<MethodInfo>)Call(Q,"FindSwingMethods",types);
  Assert(swings.Count==1,"finds actual hull Swing");
  foreach(var m in swings) {
   // Null entity is an intentional sentinel: blocked blows must not enter body; an allowed axe must reach entity access and throw.
   var obj=RuntimeHelpers.GetUninitializedObject(m.DeclaringType);
   foreach(EnumTool? tool in new EnumTool?[]{null,EnumTool.Hammer,EnumTool.Pickaxe,EnumTool.Knife,EnumTool.Sword,EnumTool.Axe}) {
    var slot=tool==null?null:new ItemSlot(null){Itemstack=new ItemStack(new Item{Tool=tool})};
    var args=m.GetParameters().Select(p=>p.Name=="slot"?(object)slot:p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray();
    bool reached=false;try{m.Invoke(obj,args);}catch(TargetInvocationException e) when(e.InnerException is NullReferenceException){reached=true;}
    Assert(reached==(tool==EnumTool.Axe),"actual hull prefix "+(tool?.ToString()??"empty hand")+(reached?" resumes original (intentional null entity sentinel)":" skips original safely"));
   }
  }
 }
 static readonly MethodInfo ReceiveDamage=typeof(Entity).GetMethod("ReceiveDamage",new[]{typeof(DamageSource),typeof(float)});
 // Uninitialized entities: a spared blow never enters Entity.ReceiveDamage; any other blow does (returning or throwing there).
 static bool Entered(Type type,DamageSource source) {
  var entity=RuntimeHelpers.GetUninitializedObject(type);Ran.Remove(ReceiveDamage);
  try{ReceiveDamage.Invoke(entity,new object[]{source,6f});}catch(TargetInvocationException){return true;}
  return Ran[ReceiveDamage];
 }
 static void TestLightning(Assembly asm,Harmony probe) {
  if(Negative=="lightning") {
   new Harmony("headless-negative-control").Unpatch(ReceiveDamage,HarmonyPatchType.Prefix,"quietroughseas");
   Console.WriteLine("NEGATIVE CONTROL: removed production lightning prefix in this fresh process");
  }
  probe.Patch(ReceiveDamage,postfix:new HarmonyMethod(typeof(Program).GetMethod("After",Any)));
  var lightning=new DamageSource{Source=EnumDamageSource.Weather,Type=EnumDamageType.Electricity};
  var hail=new DamageSource{Source=EnumDamageSource.Weather,Type=EnumDamageType.BluntAttack};
  var shock=new DamageSource{Source=EnumDamageSource.Entity,Type=EnumDamageType.Electricity};
  Assert(!new QuietRoughSeasConfig().LightningDamagesAnimals,"config default spares animals from lightning");
  Assert(!Entered(typeof(EntityAgent),lightning),"actual lightning prefix spares animal");
  Assert(Entered(typeof(EntityPlayer),lightning),"lightning still reaches player");
  Assert(Entered(asm.GetType("RoughSeas.Vessels.EntityVessel",true),lightning),"lightning still reaches Rough Seas vessel hull");
  Assert(Entered(typeof(EntityAgent),hail),"other weather damage still reaches animal");
  Assert(Entered(typeof(EntityAgent),shock),"non-weather electricity still reaches animal");
  Set(Q,"lightningDamagesAnimals",true);
  Assert(Entered(typeof(EntityAgent),lightning),"LightningDamagesAnimals=true leaves lightning on animals");
  Set(Q,"lightningDamagesAnimals",false);
 }
 static void TestPower(QuietRoughSeasSystem quiet,ICoreAPI api,ModSystem propulsion,State s) {
  var config=propulsion.GetType().GetProperty("Config").GetValue(propulsion);
  var strokes=(IDictionary)Get(config,"Strokes");
  var before=strokes.Keys.Cast<string>().ToDictionary(k=>k,k=>(double)Get(strokes[k],"GrossEfficiency"));
  if(Negative=="power") { Set(Q,"strokePowerMultiplier",new Dictionary<string,double>{{"paddling",1},{"sculling",1}});Console.WriteLine("NEGATIVE CONTROL: replaced production power multipliers with 1 in this fresh process"); }
  quiet.AssetsFinalize(api);
  foreach(var k in before.Keys)Assert((double)Get(strokes[k],"GrossEfficiency")==before[k]*(k=="paddling"||k=="sculling"?8:1),"actual config "+k+" efficiency multiplier "+(k=="paddling"||k=="sculling"?8:1));
  quiet.AssetsFinalize(api);
  foreach(var k in before.Keys)Assert((double)Get(strokes[k],"GrossEfficiency")==before[k]*(k=="paddling"||k=="sculling"?8:1),"power idempotent "+k);
 }
}
