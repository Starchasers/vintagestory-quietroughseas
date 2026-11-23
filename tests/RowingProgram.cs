using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

public class StrokeProxy : DispatchProxy
{
    public System.Func<MethodInfo,object[],object> Handler;
    protected override object Invoke(MethodInfo m,object[] a)=>Handler(m,a);
    public static T Make<T>(System.Func<MethodInfo,object[],object> handler) where T:class { var p=DispatchProxy.Create<T,StrokeProxy>();((StrokeProxy)(object)p).Handler=handler;return p; }
}

static class RowingChecks
{
    static object Stroke(Assembly upstream)
    {
        var mount = upstream.GetType("RoughSeas.Propulsion.MountStrokes", true);
        var stroke=Activator.CreateInstance(mount,new object[]{new Dictionary<string,Dictionary<string,string>>(),null});
        Put(stroke,"playing", ("row",(string)null));
        return stroke;
    }
    static EntityAgent Rower(IAnimationManager manager)
    {
        EntityAgent rower=manager is PlayerAnimationManager ? new EntityPlayer() : new EntityAgent(); rower.AnimManager=manager;
        if(rower is EntityPlayer) typeof(EntityPlayer).GetField("animManager",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(rower,manager);
        typeof(Entity).GetProperty("Properties").SetValue(rower,new EntityProperties { Client=new EntityClientProperties(Array.Empty<Vintagestory.API.Datastructures.JsonObject>(),new Dictionary<string,Vintagestory.API.Datastructures.JsonObject>()) });
        rower.Properties.Client.AnimationsByMetaCode=new Dictionary<string,AnimationMetaData> { ["row"]=new AnimationMetaData { Code="row",Animation="row",AnimationSpeed=2 } };
        return rower;
    }
    static object Call(object stroke,string method,params object[] args) => stroke.GetType().GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(stroke,args);
    static void Put(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    public static void Red(Assembly upstream,Action<bool,string> check)
    {
        foreach(bool fp in new[]{true,false})
        {
            try { Call(Stroke(upstream),"Pace",Rower(new PlayerAnimationManager { UseFpAnmations=fp }),1.5);check(false,"unpatched Pace must reproduce native NRE"); }
            catch(TargetInvocationException e) { check(e.InnerException is NullReferenceException,"RED native PlayerAnimationManager NRE fp="+fp); }
        }
    }
    static void ReadyPlayers(Assembly upstream, Action<bool,string> check)
    {
        foreach(bool fp in new[]{true,false})
        {
            var manager=new PlayerAnimationManager { UseFpAnmations=fp };
            var player=Rower(manager);
            manager.Init(StrokeProxy.Make<ICoreAPI>((m,a)=>m.Name=="get_Side"?EnumAppSide.Server:null),player);
            var running=new RunningAnimation { Active=true,meta=player.Properties.Client.AnimationsByMetaCode["row"] };
            manager.Animator=StrokeProxy.Make<IAnimator>((m,a)=>m.Name=="GetAnimationState"?running:null);
            Call(Stroke(upstream),"Pace",player,2.5);
            check(running.meta.AnimationSpeed==5,"ready native PlayerAnimationManager preserves pace fp="+fp);
        }
    }

    public static void Green(Assembly upstream,Action<bool,string> check)
    {
        foreach(IAnimationManager manager in new IAnimationManager[]{null,new AnimationManager(),new PlayerAnimationManager(),new PlayerAnimationManager { UseFpAnmations=false }})
        {
            var rower=Rower(manager);var stroke=Stroke(upstream);
            Call(stroke,"Pace",rower,1.5);
            check(Call(stroke,"DrawnShare",rower)==null,"not-ready Pace/DrawnShare are safe "+(manager?.GetType().Name??"null"));
            Put(stroke,"finishingFrom",3);
            var seat=(EntityBoatSeat)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(EntityBoatSeat));seat.actionAnim="next";
            Call(stroke,"Finish",null,rower,seat,null);
            check(stroke.GetType().GetField("finishingFrom",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stroke)==null,"not-ready Finish retains upstream bookkeeping");
        }
        var state=new RunningAnimation { CurrentFrame=15,Animation=new Animation { QuantityFrames=30 },Active=true,Running=true };
        string requested=null;
        var animator=StrokeProxy.Make<IAnimator>((m,a)=> { if(m.Name=="GetAnimationState") {requested=(string)a[0];return state;}return null; });
        var ready=new AnimationManager { Animator=animator };var entity=Rower(ready);var mount=Stroke(upstream);
        var original=entity.Properties.Client.AnimationsByMetaCode["row"];state.meta=original;
        Call(mount,"Pace",entity,1.5);
        check(state.meta.AnimationSpeed==3 && original.AnimationSpeed==2 && requested=="row","ready Pace preserves clone and speed scaling");
        check(Convert.ToDouble(Call(mount,"DrawnShare",entity))==0.5,"ready DrawnShare preserves progress");
        ready.Animator=null;Call(mount,"Pace",entity,2.0);
        check(Call(mount,"DrawnShare",entity)==null,"ready to unloaded lifecycle is safe");
        ready.Animator=animator;Call(mount,"Pace",entity,2.0);
        check(state.meta.AnimationSpeed==4,"animator readiness recovers without permanent disable");
        ReadyPlayers(upstream,check);
        ready.Animator=StrokeProxy.Make<IAnimator>((m,a)=>throw new InvalidOperationException("unrelated"));
        try {Call(mount,"Pace",entity,1.0);check(false,"unrelated exceptions must propagate");}
        catch(TargetInvocationException e) {check(e.InnerException is InvalidOperationException,"unrelated exceptions propagate unchanged");}
    }
}
