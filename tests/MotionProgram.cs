using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using QuietRoughSeas;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

public class MotionProxy : DispatchProxy {
 public System.Func<MethodInfo,object[],object> Handler;
 protected override object Invoke(MethodInfo m,object[] a)=>Handler(m,a);
 public static object Make(Type t,System.Func<MethodInfo,object[],object> h){var p=Create(t,typeof(MotionProxy));((MotionProxy)p).Handler=h;return p;}
 public static object Default(MethodInfo m)=>m.ReturnType==typeof(void)?null:m.ReturnType.IsValueType?Activator.CreateInstance(m.ReturnType):null;
}
class MotionMod:Mod{}
class MotionProgram {
 static readonly BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static int count;static bool ran;static double liftValue;static bool throwLift;static int sends;static double sentY;
 static EntityPlayer CurrentPlayer;
 static bool PlayerFixture(ref EntityPlayer __result){__result=CurrentPlayer;return false;}
 static readonly Exception Foreign=new InvalidOperationException("foreign motion sentinel");
 static void Check(bool b,string s){if(!b)throw new Exception("FAIL "+s);count++;Console.WriteLine("PASS "+s);}
 static object Raw(Type t)=>RuntimeHelpers.GetUninitializedObject(t);
 static void Set(object o,string n,object v){for(var t=o is Type z?z:o.GetType();t!=null;t=t.BaseType){var f=t.GetField(n,All|BindingFlags.DeclaredOnly)??t.GetField("<"+n+">k__BackingField",All|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(o is Type?null:o,v);return;}}throw new Exception("Missing field "+n);}
 static MethodInfo M(Type t,string n)=>t.GetMethods(All).Single(x=>x.Name==n);
 static void Trace(bool __runOriginal)=>ran=__runOriginal;
 static bool LiftFixture(ref double __result){if(throwLift)throw Foreign;__result=liftValue;return false;}
 static bool throwSend;
 static bool SendFixture(ClientMain ___game){if(throwSend)throw Foreign;sends++;sentY=___game.EntityPlayer.Pos.Y;return false;}
 static int Main(string[] args){try{Run(args);Console.WriteLine($"TOTAL {count} assertions passed; 0 failed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(string[] args){
 var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(System.IO.Path.GetFullPath("fixtures/v5.0.1/bin/Debug/net10.0/RoughSeas.dll"));
 Console.WriteLine("SDK "+typeof(EntityPlayer).Assembly.GetName().Version+"; upstream "+a.GetName().Version);
 Type T(string n)=>a.GetType("RoughSeas."+n,true);
 // Execute the real FloatingBody equation with precisely the rate produced by zero dt in Afloat.
 var pc=Newtonsoft.Json.JsonConvert.DeserializeObject(System.IO.File.ReadAllText("fixtures/v5.0.1/RoughSeas/assets/roughseas/config/person.json"),T("Person.PersonConfig"));var bc=pc.GetType().GetField("Body")?.GetValue(pc)??pc.GetType().GetProperty("Body").GetValue(pc);
 var body=M(T("Person.Body"),"Of").Invoke(null,new[]{bc});
 var person=MotionProxy.Make(T("Person.IPerson"),(m,v)=>m.Name=="get_Body"?body:MotionProxy.Default(m));
 var waves=Activator.CreateInstance(T("Storms.Waves"),Activator.CreateInstance(T("Storms.WavesConfig")));
 var drawn=MotionProxy.Make(T("Storms.IDrawnSea"),(m,v)=>MotionProxy.Default(m));
 object Step(double seconds,double rate){var fb=Activator.CreateInstance(T("Person.FloatingBody"),person,pc,waves);var step=M(fb.GetType(),"Step");var p=step.GetParameters();var v=p.Select(x=>x.ParameterType.IsValueType?Activator.CreateInstance(x.ParameterType):null).ToArray();v[0]=drawn;v[1]=Activator.CreateInstance(T("Storms.WaterSurface"),2d,0d,false);v[2]=Activator.CreateInstance(T("Person.BodyPlace"),0d,0d,0d,1.6d,0.6d);v[5]=seconds;v[6]=rate;v[7]=1d;v[12]=1d;return step.Invoke(fb,v);}
 double X(object o)=>(double)o.GetType().GetProperty("X").GetValue(o);
 var clock=Activator.CreateInstance(T("SeaAndWeather.SeaClock"),All,null,new object[]{(System.Func<double>)(()=>0d),(System.Func<double>)(()=>.5d),(System.Func<double>)(()=>60d)},null);
 double zero=0;var zeroRate=(double)M(clock.GetType(),"SecondsIn").Invoke(clock,new object[]{zero,60d})/zero;
 Check(double.IsNaN(zeroRate),"RED actual SeaClock SecondsIn(0,60)/0 is NaN");
 var red=Step(0,zeroRate);Check(double.IsNaN(X(red)),"RED actual FloatingBody zero-dt NaN rate contaminates horizontal motion");
 Check(double.IsFinite(X(Step(0.02,1))),"actual FloatingBody finite-dt horizontal motion remains finite");
 var afloat=M(T("Person.BodyAfloat"),"Afloat");var lift=M(T("Floating.VesselMotion"),"Lift");var send=M(typeof(NetworkAPI),"SendPlayerPositionPacket");var tick=M(typeof(EntityBehaviorPlayerPhysics),"OnPhysicsTick");
 var h=new Harmony("motion.fixture");h.Patch(afloat,postfix:new HarmonyMethod(M(typeof(MotionProgram),"Trace")));h.Patch(lift,prefix:new HarmonyMethod(M(typeof(MotionProgram),"LiftFixture")){priority=Priority.First});
 // Native serializer's original NaN validator, without any production or upstream packet patches.
 var player=new EntityPlayer();player.Pos.X=double.NaN;var game=(ClientMain)Raw(typeof(ClientMain));h.Patch(typeof(ClientMain).GetProperty("EntityPlayer").GetMethod,prefix:new HarmonyMethod(M(typeof(MotionProgram),"PlayerFixture")));CurrentPlayer=player;var net=(NetworkAPI)Raw(typeof(NetworkAPI));Set(net,"game",game);h.Patch(send,postfix:new HarmonyMethod(M(typeof(MotionProgram),"Trace")));
 bool NativeThrows(){try{send.Invoke(net,null);return false;}catch(TargetInvocationException e){Console.WriteLine("NATIVE "+e.InnerException.Message);return e.InnerException.Message.Contains("not a number");}}
 Check(NativeThrows(),"RED actual native packet rejects NaN position");
 player.Pos.X=0;player.Pos.Motion.X=double.NaN;Check(NativeThrows(),"RED actual native packet rejects NaN motion");player.Pos.Motion.X=0;player.Pos.X=double.NaN;
 MountChecks.Red(net,Check);
 // Install every production patch through the real lifecycle, with headless API interfaces.
 object Proxy(Type t)=>MotionProxy.Make(t,(m,v)=>m.ReturnType.IsInterface?Proxy(m.ReturnType):MotionProxy.Default(m));
 var logger=(ILogger)MotionProxy.Make(typeof(ILogger),(m,v)=>{if(m.Name.Contains("Warning"))Console.WriteLine("LOG "+v[0]);return null;});
 var mod=(Mod)Raw(typeof(MotionMod));Set(mod,"Info",new ModInfo{Version="5.0.1"});Set(mod,"Logger",logger);Set(mod,"Systems",new ModSystem[]{(ModSystem)Raw(T("Person.PersonModSystem"))});
 var loader=MotionProxy.Make(typeof(IModLoader),(m,v)=>m.Name=="GetMod"?(string)v[0]=="roughseas"?mod:null:m.ReturnType.IsInterface?Proxy(m.ReturnType):MotionProxy.Default(m));
 var api=(ICoreAPI)MotionProxy.Make(typeof(ICoreAPI),(m,v)=>m.Name=="get_ModLoader"?loader:m.Name=="get_Logger"?logger:m.Name=="get_Side"?EnumAppSide.Client:m.ReturnType.IsInterface?Proxy(m.ReturnType):MotionProxy.Default(m));
 var system=new QuietRoughSeasSystem();Set(system,"Mod",mod);system.StartPre(api);
 MountChecks.Green(net,Check);
 foreach(var method in new[]{afloat,lift,send,tick})Check(Harmony.GetPatchInfo(method)?.Owners.Contains("quietroughseas")==true,"StartPre registers "+method.DeclaringType.Name+"."+method.Name);
 var negative=Environment.GetEnvironmentVariable("QRS_MOTION_NEGATIVE");
 if(negative!=null){var target=negative=="dt"?afloat:negative=="lift"?lift:negative=="snapshot"?tick:send;new Harmony("motion.controls").Unpatch(target,HarmonyPatchType.All,"quietroughseas");}
 foreach(float dt in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity}){ran=true;var result=(bool)afloat.Invoke(null,new object[]{dt,player,player.Pos,new EntityControls()});Check(!ran&&result,"invalid dt guard "+dt);}
 ran=false;Check((bool)afloat.Invoke(null,new object[]{0.02f,player,player.Pos,new EntityControls()})&&ran,"finite dt preserves upstream early-return dispatch");
 foreach(double value in new[]{0d,2.5d,-3d,double.NaN,double.PositiveInfinity,double.NegativeInfinity}){liftValue=value;var result=(double)lift.Invoke(null,new object[]{null});Check(result==(double.IsFinite(value)?value:0),"Lift normalization "+value);}
 throwLift=true;try{lift.Invoke(null,new object[]{null});Check(false,"foreign Lift exception unchanged");}catch(TargetInvocationException e){Check(ReferenceEquals(e.InnerException,Foreign),"foreign Lift exception unchanged");}throwLift=false;
 Check(!NativeThrows(),"no-snapshot invalid packet skips native NaN validator");
 foreach(var axis in new[]{"X","Y","Z","Motion.X","Motion.Y","Motion.Z"})foreach(var bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){
 var fresh=new EntityPlayer();CurrentPlayer=fresh;var parts=axis.Split('.');if(parts.Length==1)typeof(EntityPos).GetProperty(axis).SetValue(fresh.Pos,bad);else typeof(Vintagestory.API.MathTools.Vec3d).GetField(parts[1]).SetValue(fresh.Pos.Motion,bad);
 ran=true;send.Invoke(net,null);Check(!ran,"no snapshot skips "+axis+" "+bad);
 }CurrentPlayer=player;
 // Capture after real Rough Seas SendUnlifted, but stop before serializer I/O.
 var upstreamPacket=new Harmony("roughseas.storms.motion");upstreamPacket.Patch(send,prefix:new HarmonyMethod(M(T("Floating.VesselMotion"),"SendUnlifted")),postfix:new HarmonyMethod(M(T("Floating.VesselMotion"),"RestoreLift")));
 h.Patch(send,prefix:new HarmonyMethod(M(typeof(MotionProgram),"SendFixture")){priority=Priority.Last});
 var guard=Harmony.GetPatchInfo(send).Prefixes.Single(p=>p.owner=="quietroughseas");Check(guard.priority==800&&guard.before.Contains("roughseas.storms.motion"),"packet guard precedes Rough Seas mutation");
 var unready=new EntityPlayer();unready.Pos.X=double.NaN;unready.Pos.Y=42;CurrentPlayer=unready;liftValue=2.5;ran=true;send.Invoke(net,null);Check(!ran&&sends==0&&unready.Pos.Y==42,"no-snapshot rejection preserves Y with actual Rough Seas packet patches");CurrentPlayer=player;
 player.Pos.SetPos(10,20,30);player.Pos.Motion.Set(.1,.2,.3);liftValue=2.5;send.Invoke(net,null);Check(sends==1&&sentY==17.5&&player.Pos.Y==20,"finite packet behavior preserves actual Rough Seas unlift/restore");liftValue=double.NaN;
 player.Pos.X=double.NaN;player.Pos.Motion.Z=double.PositiveInfinity;send.Invoke(net,null);Check(sends==2&&player.Pos.X==10&&player.Pos.Motion.Z==.3,"packet finite snapshot recovery");
 // Exercise the real native physics method registration without a game simulation: a last-priority sentinel skips its body.
 h.Patch(tick,prefix:new HarmonyMethod(M(typeof(MotionProgram),"SkipTick")){priority=Priority.Last});
 var p2=new EntityPlayer();p2.Pos.SetPos(40,50,60);p2.Pos.Motion.Set(.4,.5,.6);CurrentPlayer=p2;var physics=new EntityBehaviorPlayerPhysics(p2);tick.Invoke(physics,new object[]{.02f});
 p2.Pos.X=double.NaN;send.Invoke(net,null);Check(p2.Pos.X==40&&sends==3,"physics finite snapshot recovery");
 p2.Pos.Dimension=1;p2.Pos.X=double.NaN;send.Invoke(net,null);Check(sends==3,"cross-dimension snapshot never restored");
 p2.Pos.Dimension=0;send.Invoke(net,null);Check(sends==3&&double.IsNaN(p2.Pos.X),"dimension transition invalidates old sample even on return");
 p2.Pos.SetPos(1,2,3);throwSend=true;try{send.Invoke(net,null);Check(false,"foreign packet exception unchanged");}catch(TargetInvocationException e){Check(ReferenceEquals(e.InnerException,Foreign),"foreign packet exception unchanged");}throwSend=false;
 foreach(var axis in new[]{"X","Y","Z","Motion.X","Motion.Y","Motion.Z"})foreach(var bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){
  var p3=new EntityPlayer();p3.EntityId=7;p3.Pos.SetPos(40,50,60);p3.Pos.Motion.Set(.4,.5,.6);CurrentPlayer=p3;var ph=new EntityBehaviorPlayerPhysics(p3);tick.Invoke(ph,new object[]{.02f});
  var parts=axis.Split('.');if(parts.Length==1)typeof(EntityPos).GetProperty(axis).SetValue(p3.Pos,bad);else typeof(Vintagestory.API.MathTools.Vec3d).GetField(parts[1]).SetValue(p3.Pos.Motion,bad);
  tick.Invoke(ph,new object[]{.02f});int before=sends;liftValue=2.5;send.Invoke(net,null);
  Check(sends==before+1&&p3.Pos.X==40&&p3.Pos.Y==50&&p3.Pos.Z==60&&p3.Pos.Motion.X==.4&&p3.Pos.Motion.Y==.5&&p3.Pos.Motion.Z==.6&&sentY==47.5,"snapshot restores all components after invalid tick "+axis+" "+bad);
 }
 var replacement=new EntityPlayer();replacement.EntityId=7;replacement.Pos.X=double.NaN;CurrentPlayer=replacement;int prior=sends;send.Invoke(net,null);Check(sends==prior&&double.IsNaN(replacement.Pos.X),"same entity ID on replacement object cannot reuse snapshot");
 CurrentPlayer=p2;p2.EntityId=9;p2.Pos.X=double.NaN;send.Invoke(net,null);Check(sends==prior,"changed entity ID on reused object invalidates sample");p2.EntityId=0;send.Invoke(net,null);Check(sends==prior,"returning entity ID cannot resurrect invalidated sample");
 }
 static bool SkipTick()=>false;
}
