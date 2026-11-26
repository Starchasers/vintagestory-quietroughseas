using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

// Native validation really executes in Red. Finite Green sends stop at a final
// sentinel: no socket, serializer, game loop or naturally corrupt world claimed.
static class MountChecks
{
    static MethodInfo send = typeof(NetworkAPI).GetMethod("SendPlayerMountPositionPacket");
    static int sends;
    static bool foreign;
    static readonly Exception failure = new ArgumentException("foreign mount sentinel");
    static bool Sentinel(Entity mount) { if (foreign) throw failure; sends++; return false; }
    static MethodInfo Method(string name) => typeof(MountChecks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
    public static void Red(NetworkAPI net, Action<bool,string> check)
    {
        foreach (var motion in new[] { false, true }) {
            var mount = new EntityBoat();
            if (motion) mount.Pos.Motion.X = double.NaN; else mount.Pos.X = double.NaN;
            string expected = motion ? "Mount Motion is not a number" : "Mount Position is not a number";
            try { send.Invoke(net, new object[] { mount }); check(false,"RED native " + expected); }
            catch (TargetInvocationException e) { check(e.InnerException is ArgumentException && e.InnerException.Message == expected,"RED native " + expected); }
        }
    }
    public static void Green(NetworkAPI net, Action<bool,string> check)
    {
        check(Harmony.GetPatchInfo(send)?.Owners.Contains("quietroughseas") == true,"production StartPre registers mount guard");
        if (Environment.GetEnvironmentVariable("QRS_MOTION_NEGATIVE") == "mount")
            new Harmony("mount.control").Unpatch(send,HarmonyPatchType.All,"quietroughseas");
        var badMount = new EntityBoat(); badMount.Pos.Motion.X = double.NaN;
        // No sentinel yet: proves the exact native exception is prevented.
        send.Invoke(net,new object[] { badMount });
        check(double.IsNaN(badMount.Pos.Motion.X),"GREEN native mount NaN packet rejected without state reconstruction");
        new Harmony("mount.fixture").Patch(send,prefix:new HarmonyMethod(Method("Sentinel")){priority=Priority.Last});
        foreach (var mount in new Entity[] { new EntityBoat(), new EntityAgent() }) {
            mount.EntityId=31; mount.Pos.SetPos(11,22,33); mount.Pos.Motion.Set(.1,.2,.3);
            int before=sends; send.Invoke(net,new object[]{mount});
            check(sends==before+1 && mount.Pos.X==11 && mount.Pos.Y==22 && mount.Pos.Z==33 && mount.Pos.Motion.X==.1 && mount.Pos.Motion.Y==.2 && mount.Pos.Motion.Z==.3,"finite mount original path and exact state " + mount.GetType().Name);
            foreach (string axis in new[]{"X","Y","Z","Motion.X","Motion.Y","Motion.Z"}) foreach (double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}) {
                mount.Pos.SetPos(11,22,33); mount.Pos.Motion.Set(.1,.2,.3);
                var parts=axis.Split('.'); if(parts.Length==1)typeof(EntityPos).GetProperty(axis).SetValue(mount.Pos,bad);else typeof(Vintagestory.API.MathTools.Vec3d).GetField(parts[1]).SetValue(mount.Pos.Motion,bad);
                var prior=mount.Pos.Copy(); before=sends;
                send.Invoke(net,new object[]{mount});
                bool Eq(double a,double b)=>a.Equals(b);
                check(sends==before && Eq(prior.X,mount.Pos.X)&&Eq(prior.Y,mount.Pos.Y)&&Eq(prior.Z,mount.Pos.Z)&&Eq(prior.Motion.X,mount.Pos.Motion.X)&&Eq(prior.Motion.Y,mount.Pos.Motion.Y)&&Eq(prior.Motion.Z,mount.Pos.Motion.Z),"mount rejects without mutation " + mount.GetType().Name+" "+axis+" "+bad);
            }
            foreach(var state in new[]{(31L,1),(32L,1),(31L,0)}) {
                mount.EntityId=state.Item1; mount.Pos.Dimension=state.Item2; before=sends;
                send.Invoke(net,new object[]{mount});
                check(sends==before && double.IsNegativeInfinity(mount.Pos.Motion.Z),"mount identity/dimension transition never restores a sample " + mount.GetType().Name+" "+state);
            }
            mount.Pos.SetPos(101,202,303); mount.Pos.Motion.Set(0,0,0); before=sends; send.Invoke(net,new object[]{mount});
            check(sends==before+1&&mount.Pos.X==101&&mount.Pos.Y==202&&mount.Pos.Z==303,"server corrected mount resumes unchanged " + mount.GetType().Name);
        }
        var replacement=new EntityBoat(); replacement.EntityId=31; replacement.Pos.Motion.X=double.NaN; int previous=sends;send.Invoke(net,new object[]{replacement});
        check(sends==previous&&double.IsNaN(replacement.Pos.Motion.X),"replacement mount cannot inherit same-ID finite state");
        foreign=true;
        try {send.Invoke(net,new object[]{new EntityBoat()});check(false,"foreign mount exception unchanged");}
        catch(TargetInvocationException e){check(ReferenceEquals(e.InnerException,failure),"foreign mount exception unchanged");}
        foreign=false;
    }
}
