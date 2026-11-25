using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using QuietRoughSeas;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

// Actual pinned upstream bodies and actual native managers; State only substitutes
// client services. No render context, game loop, native mesh or network is created.
static class AnimationProgram
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly upstream;
    static State installServices;
    static int passed;
    static readonly Dictionary<MethodBase, bool> Ran = new();
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
    static Type T(string name) => upstream.GetType("RoughSeas." + name, true);
    static MethodInfo M(Type t, string name) => t.GetMethods(Any).Single(x => x.Name == name);
    static object Call(object target, string name, params object[] args) => M(target.GetType(), name).Invoke(target, args);
    static void Put(object target, string name, object value)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Any) ?? t.GetField("<" + name + ">k__BackingField", Any);
            if (f != null) { f.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    static object Get(object target, string name) => target.GetType().GetField(name, Any)?.GetValue(target) ?? target.GetType().GetProperty(name, Any)?.GetValue(target);
    static Exception Failure(Action action)
    {
        try { action(); return null; }
        catch (TargetInvocationException e) { return e.InnerException; }
        catch (Exception e) { return e; }
    }
    static void Observe(MethodBase __originalMethod, bool __runOriginal) => Ran[__originalMethod] = __runOriginal;
    static void Watch(Type t, string name) => new Harmony("animation.observer").Patch(M(t, name), postfix: new HarmonyMethod(M(typeof(AnimationProgram), nameof(Observe))));
    static EntityBoat Boat(IAnimationManager manager = null)
    {
        var boat = new EntityBoat(); boat.AnimManager = manager;
        boat.World = (IWorldAccessor)new State { Side = EnumAppSide.Client }.Get(typeof(IWorldAccessor));
        Put(boat, "Properties", new EntityProperties { Client = new EntityClientProperties(Array.Empty<JsonObject>(), new Dictionary<string, JsonObject>()) });
        return boat;
    }
    static object Oars(EntityBoat boat)
    {
        var own = RuntimeHelpers.GetUninitializedObject(T("Vessels.OwnOars"));
        Put(own, "vessel", boat); Put(own, "codes", Array.Empty<string>());
        return own;
    }
    static object Mount() { var m = Activator.CreateInstance(T("Propulsion.MountStrokes"), new object[] { new Dictionary<string, Dictionary<string, string>>(), null }); Put(m, "playing", ("row", (string)null)); return m; }
    static EntityAgent Rower(IAnimationManager manager)
    {
        EntityAgent e = manager is PlayerAnimationManager ? new EntityPlayer() : new EntityAgent();
        e.AnimManager = manager; if (e is EntityPlayer) Put(e, "animManager", manager);
        Put(e, "Properties", new EntityProperties { Client = new EntityClientProperties(Array.Empty<JsonObject>(), new Dictionary<string, JsonObject>()) });
        e.Properties.Client.AnimationsByMetaCode = new() { ["row"] = new AnimationMetaData { Code = "row", Animation = "row", AnimationSpeed = 2 } };
        return e;
    }
    static IAnimator Animator(RunningAnimation running) => StrokeProxy.Make<IAnimator>((m, a) => m.Name == "GetAnimationState" ? running : null);
    static object Empty(Type t)
    {
        var obj = RuntimeHelpers.GetUninitializedObject(t);
        foreach (var f in t.GetFields(Any).Where(f => !f.IsStatic && f.FieldType.IsGenericType))
            if (f.FieldType.GetGenericTypeDefinition() == typeof(List<>) || f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) f.SetValue(obj, Activator.CreateInstance(f.FieldType));
        return obj;
    }
    static object Renderer(string name, State state, EntityBoat boat)
    {
        var api = (ICoreClientAPI)state.Get(typeof(ICoreClientAPI));
        var t = T(name);
        if (name == "Cargo.CargoRenderer") return Activator.CreateInstance(t, api, boat, 1f);
        if (name == "Storms.BilgeRenderer") return Activator.CreateInstance(t, api, boat, new double[] { 0.1 });
        var r = Empty(t); Put(r, "capi", api); Put(r, "entity", boat); Put(r, "ropes", Empty(T("Sailing.RigRopes"))); return r;
    }
    static void Late(object r)
    {
        if (r.GetType().Name == "CargoRenderer") Call(r, "Show", new (MeshData, Vec3f)[] { (new MeshData(), new Vec3f()) });
        else if (r.GetType().Name == "BilgeRenderer") Call(r, "Show", 1, 0d);
        else Call(r, "Shaped", new Shape());
    }
    static void SeatLifecycle(bool red)
    {
        var client = new State { Side = EnumAppSide.Client };
        var passenger = new EntityAgent { AnimManager = new PlayerAnimationManager() };
        passenger.Api = (ICoreClientAPI)client.Get(typeof(ICoreClientAPI));
        Type configType = typeof(IMountableSeat).GetProperty("Config").PropertyType;
        object config = Activator.CreateInstance(configType);
        Put(config, "MountRotation", new Vec3f()); Put(config, "Animation", "old");
        var seat = StrokeProxy.Make<IMountableSeat>((m,a) => m.Name == "get_Config" ? config : m.Name == "get_Passenger" ? passenger : null);
        var pose = Activator.CreateInstance(T("Vessels.SeatPose"), new object[] { new Vec3f(1,2,3), "row", null, null });
        var failure = Failure(() => M(T("Vessels.SeatPoses"), "Show").Invoke(null, new object[] { seat, pose, 1.2f, new Vec3f(0,1,0), "old" }));
        Check(red ? failure is NullReferenceException : failure == null, red ? "RED SeatPoses local-player unload reproduces NRE" : "SeatPoses missing local player and uninitialized manager safe");
        if (!red) Check((string)Get(config,"Animation") == "row" && (float)Get(config,"EyeHeight") == 1.2f, "SeatPoses preserves configuration bookkeeping while manager unavailable");
    }
    static EntityPlayer localFixture;
    static bool LocalEntityFixture(ref EntityPlayer __result) { __result=localFixture; return false; }
    static bool LocalCameraFixture(ref EnumCameraMode __result) { __result=EnumCameraMode.FirstPerson; return false; }
    static void ClientManagers()
    {
        var player = new EntityPlayer();
        Put(player, "Properties", new EntityProperties { Client = new EntityClientProperties(Array.Empty<JsonObject>(), new Dictionary<string, JsonObject>()) });
        player.Properties.Client.AnimationsByMetaCode = new();
        foreach (var code in new[] { "row", "row-fp", "row-ifp" })
            player.Properties.Client.AnimationsByMetaCode[code] = new AnimationMetaData { Code=code, Animation=code, AnimationSpeed=2 };
        var local = (IClientPlayer)RuntimeHelpers.GetUninitializedObject(Type.GetType("Vintagestory.Client.NoObf.ClientPlayer, VintagestoryLib", true));
        localFixture=player;
        new Harmony("animation.local-player-fixture").Patch(local.GetType().GetProperty("Entity").GetMethod,prefix:new HarmonyMethod(M(typeof(AnimationProgram),nameof(LocalEntityFixture))));
        new Harmony("animation.local-player-fixture").Patch(local.GetType().GetProperty("CameraMode").GetMethod,prefix:new HarmonyMethod(M(typeof(AnimationProgram),nameof(LocalCameraFixture))));
        Check(local.Entity==player,"native client-manager fixture binds local entity getter (not a client loop)");
        var world = StrokeProxy.Make<IClientWorldAccessor>((m,a) => m.Name == "get_Player" ? local : m.Name == "get_Side" ? EnumAppSide.Client : null);
        var boolValues = new Dictionary<string,bool>();
        var boolSettings = StrokeProxy.Make<ISettingsClass<bool>>((m,a) => {
            if (m.Name=="set_Item") { boolValues[(string)a[0]]=(bool)a[1]; return null; }
            if (m.Name=="get_Item") return boolValues.TryGetValue((string)a[0],out var v) && v;
            return null;
        });
        var settings = StrokeProxy.Make<ISettings>((m,a) => m.Name=="get_Bool" ? boolSettings : null);
        var api = StrokeProxy.Make<ICoreClientAPI>((m,a) => m.Name == "get_World" ? world : m.Name == "get_Side" ? EnumAppSide.Client : m.Name == "get_Settings" ? settings : null);
        player.Api=api; player.World=world;
        player.TpAnimManager.Init(api,player); player.SelfFpAnimManager.Init(api,player);
        var tp = new RunningAnimation { Active=true,meta=player.Properties.Client.AnimationsByMetaCode["row"].Clone() };
        var fp = new RunningAnimation { Active=true,meta=player.Properties.Client.AnimationsByMetaCode["row-fp"].Clone() };
        var ifp = new RunningAnimation { Active=true,meta=player.Properties.Client.AnimationsByMetaCode["row-ifp"].Clone() };
        Check(!ReferenceEquals(tp.meta,fp.meta) && !ReferenceEquals(tp.meta,ifp.meta) && !ReferenceEquals(fp.meta,ifp.meta), "TP FP and immersive FP running metadata are independent");
        var tpLookups = new List<string>(); var fpLookups = new List<string>();
        player.TpAnimManager.Animator = StrokeProxy.Make<IAnimator>((m,a) => {
            if (m.Name!="GetAnimationState") return null;
            var code=(string)a[0]; tpLookups.Add(code); return code=="row" ? tp : null;
        });
        var fpAnimator = StrokeProxy.Make<IAnimator>((m,a) => {
            if (m.Name!="GetAnimationState") return null;
            var code=(string)a[0]; fpLookups.Add(code);
            // Deliberately withhold just FP in a fresh-process control: TP still updates.
            if (Environment.GetEnvironmentVariable("QRS_ANIMATION_NEGATIVE")=="fp-pace") return null;
            return code=="row-fp" ? fp : code=="row-ifp" ? ifp : null;
        });
        player.SelfFpAnimManager.Animator=null;
        Call(Mount(),"Pace",player,3d);
        Check(tp.meta.AnimationSpeed==6 && fp.meta.AnimationSpeed==2 && ifp.meta.AnimationSpeed==2, "local TP remains live while FP animator missing");
        Check(tpLookups.SequenceEqual(new[]{"row"}) && fpLookups.Count==0, "missing FP queries only ready TP row");
        foreach (bool immersive in new[]{false,true})
        {
            string variant=immersive ? "row-ifp" : "row-fp";
            // Stateful settings double, consumed by native PlayerAnimationManager.fpEnding.
            api.Settings.Bool["immersiveFpMode"]=immersive;
            player.SelfFpAnimManager.Animator=fpAnimator;
            ((PlayerAnimationManager)player.SelfFpAnimManager).UseFpAnmations=
                Environment.GetEnvironmentVariable("QRS_ANIMATION_NEGATIVE")!=(immersive ? "routing-ifp" : "routing-fp");
            tpLookups.Clear(); fpLookups.Clear();
            var selected=immersive ? ifp : fp; var other=immersive ? fp : ifp;
            float otherSpeed=other.meta.AnimationSpeed;
            double pace=immersive ? 5d : 4d;
            Call(Mount(),"Pace",player,pace);
            Check(tpLookups.SequenceEqual(new[]{"row"}) && fpLookups.SequenceEqual(new[]{variant}), "native TP row and FP lookup routing "+variant);
            Check(tp.meta.AnimationSpeed==2*pace && selected.meta.AnimationSpeed==2*pace && other.meta.AnimationSpeed==otherSpeed, "independent TP and selected FP pace "+variant);
            Check(player.Properties.Client.AnimationsByMetaCode.Values.All(x=>x.AnimationSpeed==2), "Pace preserves source metadata "+variant);
            player.SelfFpAnimManager.Animator=null;
            tpLookups.Clear(); fpLookups.Clear();
            Call(Mount(),"Pace",player,pace+1);
            Check(tp.meta.AnimationSpeed==2*(pace+1) && selected.meta.AnimationSpeed==2*pace && other.meta.AnimationSpeed==otherSpeed && tpLookups.SequenceEqual(new[]{"row"}) && fpLookups.Count==0, "FP unload preserves TP and frozen variant "+variant);
        }
    }
    static void Firestarter(bool red)
    {
        var item=new Item { Code=new AssetLocation("roughseas:test"), Attributes=JsonObject.FromJson("{\"igniteAnimation\":\"row\"}") };
        var behavior=Activator.CreateInstance(T("Shipyard.CollectibleBehaviorLightWithFirestarter"),item);
        foreach(string name in new[]{"Begin","End"})
        {
            var failure=Failure(()=>Call(behavior,name,Rower(new PlayerAnimationManager())));
            Check(red ? failure is NullReferenceException : failure==null,(red ? "RED " : "")+"firestarter "+name+" uninitialized animation manager");
        }
    }
    static void Red()
    {
        Firestarter(true);
        SeatLifecycle(true);
        Check(Failure(()=>Call(Mount(),"DrawnShare",Rower(new AnimationManager { Animator=Animator(new RunningAnimation { Active=true }) }))) is NullReferenceException,"RED DrawnShare shape animation absent");
        Check(Failure(() => Call(Oars(Boat()), "Follow")) is NullReferenceException, "RED OwnOars missing vessel manager reproduces native NRE");
        var running = new RunningAnimation { Active = true };
        Check(Failure(() => Call(Mount(), "Pace", Rower(new AnimationManager { Animator = Animator(running) }), 2d)) is NullReferenceException, "RED Pace missing running metadata reproduces Rough Seas NRE");
        var s = new State(); var r = Renderer("Cargo.CargoRenderer", s, Boat()); Call(r, "Dispose"); var before = s.Count("UploadMultiTextureMesh"); Late(r);
        Check(s.Count("UploadMultiTextureMesh") == before + 1, "RED disposed Cargo.Show still uploads without protection");
        foreach (bool fp in new[] { true, false })
        {
            var manager = new PlayerAnimationManager { UseFpAnmations = fp, Animator = Animator(running) };
            Check(Failure(() => Call(Mount(), "Pace", Rower(manager), 2d)) is NullReferenceException, "RED native player manager entity not initialized fp=" + fp);
        }
    }
    static void Green()
    {
        Firestarter(false);
        SeatLifecycle(false);
        ClientManagers();
        Check(Failure(()=>Check(Call(Mount(),"DrawnShare",Rower(new AnimationManager { Animator=Animator(new RunningAnimation { Active=true }) }))==null,"DrawnShare unavailable shape returns null"))==null,"DrawnShare owned missing shape finalizer");
        var ownState=new RunningAnimation { Active=true, CurrentFrame=1 };
        var ownManager=new AnimationManager { Animator=Animator(ownState) };
        var syncedBoat=Boat(ownManager);
        syncedBoat.Properties.Client.AnimationsByMetaCode=new() { ["row"]=new AnimationMetaData { Code="row",Animation="row" } };
        ownManager.Init((ICoreAPI)new State { Side=EnumAppSide.Client }.Get(typeof(ICoreAPI)),syncedBoat);
        ownManager.ActiveAnimationsByAnimCode["row"]=syncedBoat.Properties.Client.AnimationsByMetaCode["row"];
        var synced=Oars(syncedBoat); Put(synced,"system",Activator.CreateInstance(T("Vessels.VesselsModSystem")));
        Call(synced,"Match","row",new RunningAnimation { Active=true,CurrentFrame=17 });
        Check(ownState.CurrentFrame==17,"valid own-oar Match preserves rower frame synchronization");
        Call(synced,"Stop","row");
        Check(!ownManager.IsAnimationActive("row"),"valid own-oar Stop preserves native stop bookkeeping");
        var boat = Boat(); var own = Oars(boat);
        Check(Failure(() => Call(own, "Follow")) == null && !Ran[M(own.GetType(), "Follow")], "OwnOars missing manager guarded");
        boat.AnimManager = new AnimationManager(); Check(Failure(() => Call(own, "Follow")) == null, "OwnOars missing animator guarded");
        boat.AnimManager.Animator = Animator(null); Call(own, "Follow");
        Check(Ran[M(own.GetType(), "Follow")], "OwnOars ready recovery executes upstream body");
        boat.AnimManager = null; Call(own, "Follow"); Check(!Ran[M(own.GetType(), "Follow")], "OwnOars unload skips upstream body");
        foreach (bool fp in new[] { true, false })
        {
            var manager = new PlayerAnimationManager { UseFpAnmations = fp, Animator = Animator(new RunningAnimation { Active = true }) };
            var player = Rower(manager);
            Check(Failure(() => Call(Mount(), "Pace", player, 2d)) == null, "native PlayerAnimationManager private entity readiness fp=" + fp);
            manager.Init(StrokeProxy.Make<ICoreAPI>((m,a) => m.Name == "get_Side" ? EnumAppSide.Server : null), player);
            var state = new RunningAnimation { Active = true, meta = player.Properties.Client.AnimationsByMetaCode["row"] };
            manager.Animator = Animator(state); Call(Mount(), "Pace", player, 2d);
            Check(state.meta.AnimationSpeed == 4, "native player initialized recovery preserves pace fp=" + fp);
        }
        var malformed = new RunningAnimation { Active = true };
        var malformedOars = Oars(Boat(new AnimationManager { Animator = Animator(null) })); Put(malformedOars, "codes", null);
        Check(Failure(() => Call(malformedOars, "Follow")) == null, "OwnOars owned lifecycle NRE finalizer");
        Check(Failure(() => Call(Mount(), "Pace", Rower(new AnimationManager { Animator = Animator(malformed) }), 2d)) == null, "Pace owned metadata NRE finalizer");
        var diagnostics=(IDictionary)typeof(QuietRoughSeasSystem).GetField("cosmeticLogs",Any).GetValue(null);
        diagnostics.Clear(); installServices.Logs.Clear();
        for(int repeat=0;repeat<1000;repeat++) Call(malformedOars,"Follow");
        Check(installServices.Logs.Count(l=>l.Contains("OwnOars.Follow"))==1,"1000 owned failures emit one rate-limited warning");
        object diagnostic=diagnostics["OwnOars.Follow"];
        Check((int)Get(diagnostic,"Skipped")==999,"rate limiter counts suppressed repeats");
        Put(diagnostic,"Next",0L); // deterministic elapsed-window control; no sleep or fake production clock.
        Call(malformedOars,"Follow");
        Check(installServices.Logs.Count(l=>l.Contains("OwnOars.Follow"))==2 && installServices.Logs.Last().Contains("suppressed repeats=999"),"warning resumes with repeat summary after interval");
        Put(malformedOars,"codes",Array.Empty<string>()); Call(malformedOars,"Follow");
        Check(Ran[M(malformedOars.GetType(),"Follow")],"owned failure recovers on same instance after data readiness");
        foreach (Exception foreign in new Exception[] { new NullReferenceException("foreign"), new InvalidOperationException("foreign"), new IndexOutOfRangeException("foreign"), new ArgumentException("foreign") })
        {
            var manager = new AnimationManager { Animator = StrokeProxy.Make<IAnimator>((m,a) => throw foreign) };
            Check(ReferenceEquals(Failure(() => Call(Mount(), "Pace", Rower(manager), 2d)), foreign), "Pace preserves foreign throw-site " + foreign.GetType().Name);
            var foreignManager = StrokeProxy.Make<IAnimationManager>((m,a) => throw foreign);
            Check(ReferenceEquals(Failure(() => Call(Oars(Boat(foreignManager)), "Follow")), foreign), "OwnOars preserves foreign throw-site " + foreign.GetType().Name);
        }
        foreach (string name in new[] { "Cargo.CargoRenderer", "Sailing.RigRenderer", "Storms.BilgeRenderer" })
        {
            var state = new State(); var b = Boat(); b.Properties.Client.LoadedShapeForEntity = new Shape(); var r = Renderer(name, state, b);
            var update = name.Contains("RigRenderer") ? "Shaped" : "Show";
            var props=b.Properties.Client; b.Properties.Client=null;
            Call(r,"OnRenderFrame",0.016f,EnumRenderStage.Opaque);
            Check(!Ran[M(r.GetType(),"OnRenderFrame")],name+" remote spawn incomplete properties skips render");
            b.Properties.Client=props;
            b.Alive=false; Call(r,"OnRenderFrame",0.016f,EnumRenderStage.Opaque);
            Check(!Ran[M(r.GetType(),"OnRenderFrame")],name+" despawn skips render before disposal");
            b.Alive=true;
            if (name.Contains("CargoRenderer")) Call(r, "Show", Array.Empty<(MeshData, Vec3f)>());
            else if (name.Contains("BilgeRenderer")) Call(r, "Show", 0, 0d);
            else Call(r, "Shaped", new Shape());
            Check(Ran[M(r.GetType(), update)], name + " live callback executes upstream");
            Call(r, "Dispose"); var gpu = state.Count("UploadMultiTextureMesh"); var unregister = state.Count("UnregisterRenderer");
            Late(r); Check(!Ran[M(r.GetType(), name.Contains("RigRenderer") ? "Shaped" : "Show")], name + " disposed late callback skipped");
            foreach (var stage in new[] { EnumRenderStage.Opaque, EnumRenderStage.ShadowFar, EnumRenderStage.ShadowNear })
            { Call(r, "OnRenderFrame", 0.016f, stage); Check(!Ran[M(r.GetType(), "OnRenderFrame")], name + " disposed render skipped " + stage); }
            Call(r, "Dispose"); Check(state.Count("UnregisterRenderer") == unregister, name + " repeated Dispose idempotent");
            Check(state.Count("UploadMultiTextureMesh") == gpu && state.Count("PreparedStandardShader") == 0 && state.Count("RenderMesh") == 0 && state.Count("RenderMultiTextureMesh") == 0, name + " no late GPU work");
        }
        foreach (string name in new[] { "Shipyard.CollectibleBehaviorIcon", "Shipyard.CollectibleBehaviorCarriedBuild" })
        {
            var state = new State(); var api = (ICoreClientAPI)state.Get(typeof(ICoreClientAPI));
            var behavior = Activator.CreateInstance(T(name), new Item { Code = new AssetLocation("roughseas:test") });
            Call(behavior, "OnUnloaded", api);
            // Null stack/info deliberately make accidental post-unload execution observable.
            Check(Failure(() => Call(behavior, "OnBeforeRender", api, null, EnumItemRenderTarget.Gui, null)) == null, name + " unloaded render safe");
            Check(!Ran[M(behavior.GetType(), "OnBeforeRender")], name + " unloaded render skipped");
            Check(state.Count("UploadMultiTextureMesh") == 0, name + " unload prevents reupload");
        }
    }
    static void Run()
    {
        upstream = Assembly.LoadFrom(System.IO.Path.GetFullPath("tests/fixtures/v5.0.1/bin/Debug/net10.0/RoughSeas.dll"));
        Console.WriteLine("FIXTURE exact setup.py v5.0.1; native " + typeof(EntityBoat).Assembly.GetName() + "; Harmony " + typeof(Harmony).Assembly.GetName());
        Console.WriteLine("LIMIT headless DispatchProxy client services; no GPU, world, network or in-game validation.");
        Red();
        if (Environment.GetEnvironmentVariable("QRS_ANIMATION_RED_ONLY") == "1") return;
        var s = new State { Side = EnumAppSide.Client }; installServices=s; var quiet = new QuietRoughSeasSystem();
        var logger = (ILogger)s.Get(typeof(ILogger));
        var propulsion = (ModSystem)Activator.CreateInstance(T("Propulsion.PropulsionModSystem"));
        s.Target = (Mod)M(typeof(Program), "MakeMod").Invoke(null, new object[] { "5.0.1", logger, new ModSystem[] { propulsion } });
        M(typeof(Program), "MakeMod").Invoke(null, new object[] { "test", logger, new ModSystem[] { quiet } });
        quiet.StartPre((ICoreClientAPI)s.Get(typeof(ICoreClientAPI)));
        Check(Harmony.GetPatchInfo(M(T("Vessels.OwnOars"), "Follow"))?.Owners.Contains("quietroughseas") == true, "production StartPre installs animation safety");
        var watch = new[] { ("Vessels.OwnOars", "Follow"), ("Cargo.CargoRenderer", "Show"), ("Sailing.RigRenderer", "Shaped"), ("Storms.BilgeRenderer", "Show"), ("Cargo.CargoRenderer", "OnRenderFrame"), ("Sailing.RigRenderer", "OnRenderFrame"), ("Storms.BilgeRenderer", "OnRenderFrame"), ("Shipyard.CollectibleBehaviorIcon", "OnBeforeRender"), ("Shipyard.CollectibleBehaviorCarriedBuild", "OnBeforeRender") };
        foreach (var (type, method) in watch) Watch(T(type), method);
        var negative = Environment.GetEnvironmentVariable("QRS_ANIMATION_NEGATIVE");
        if (negative != null && negative != "fp-pace" && negative != "routing-fp" && negative != "routing-ifp")
        {
            var h = new Harmony("animation.negative");
            var target = negative switch { "draw" => M(T("Propulsion.MountStrokes"), "DrawnShare"), "fire" => M(T("Shipyard.CollectibleBehaviorLightWithFirestarter"), "Begin"), "pose" => M(T("Vessels.SeatPoses"), "Show"), "own" => M(T("Vessels.OwnOars"), "Follow"), "own-finalizer" => M(T("Vessels.OwnOars"), "Follow"), "pace" => M(T("Propulsion.MountStrokes"), "Pace"), "render" => M(T("Cargo.CargoRenderer"), "Show"), "unload" => M(T("Shipyard.CollectibleBehaviorCarriedBuild"), "OnBeforeRender"), _ => throw new Exception("unknown negative control") };
            h.Unpatch(target, negative == "pose" || negative == "fire" ? HarmonyPatchType.Transpiler : negative == "draw" || negative == "pace" || negative == "own-finalizer" ? HarmonyPatchType.Finalizer : HarmonyPatchType.Prefix, "quietroughseas");
            if (negative == "own") h.Unpatch(target, HarmonyPatchType.Finalizer, "quietroughseas");
        }
        Green();
    }
    static void Main()
    {
        try { Run(); Console.WriteLine($"TOTAL {passed} assertions passed; 0 failed"); }
        catch (Exception e) { Console.WriteLine(e); Environment.ExitCode = 1; }
    }
}
