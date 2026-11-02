using System.Reflection;
using Engine;
using Engine.Input;
using Game;
using HarmonyLib;

namespace TowerLab;

// A left-click for the lab: the ray the game builds from a click or a tap (PlayerInput.Hit), set before the mod reads the input.
[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class LabClick {
    public static bool Pending;
    [HarmonyPriority(Priority.First)]
    public static void Postfix(ComponentInput __instance){if(!Pending)return;Pending=false;var c=__instance.m_componentPlayer.GameWidget.ActiveCamera;__instance.m_playerInput.Hit=new Ray3(c.ViewPosition,c.ViewDirection);}
}

// lab --mode tools17 (0.17.0, user 2026-11-02; parts tools17-<part>). Keys go in as real key presses (LabKeys), the click as the
// game's PlayerInput.Hit (LabClick). Results under "tools17".
// ranges: Z shows every tower's ring (drawn), Z again hides them; ghost: a tower's deploy item in the hand shows a green footprint on
//   free ground and a red one with the reason on another tower; move: M on a tower picks it up (the bar is shown), R turns it 90 deg,
//   a click puts it at the crosshair with its health and turned, it fires there; M cancels; a click on an occupied spot refuses with a
//   reason and leaves it; terrain: the mech's cannon shell and grenade break blocks only with the setting on, a tower's shell never;
//   a boat beside the blast is damaged, a tower and the player beside it are not (Spared); pressures 20 / 40 / 80 in dirt and granite.
public partial class Lab {
    static IEnumerable<bool> Tools17() {
        var r=new Dictionary<string,object>();results["tools17"]=r;var bad=new List<string>();
        bool Part(string p)=>mode=="tools17"||mode=="tools17-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);
        IEnumerable<bool> Tap(Key key,double after=.3){LabKeys.Taps.Enqueue(key);for(double s0=Time.RealTime;Time.RealTime-s0<after||LabKeys.Busy;)yield return true;}
        IEnumerable<bool> Click(double after=.3){LabClick.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<after||LabClick.Pending;)yield return true;}
        var T=origin+new Vector3(-18,0,-23);   // a free part of the lab ground (the gallery is at x >= 0, z >= 0)
        pl.ComponentBody.Position=T+new Vector3(0,0,6);
        var gun=MDTowerLibrary.All.First(d=>d.attack.type=="bolt"&&!d.attack.directional&&d.attack.range>5&&MDTowerPlacement.Cells(d).X==2&&MDTowerPlacement.Cells(d).Z==2);   // 2 x 2: the walls of "ghost" leave it no room
        var band=MDTowerLibrary.All.FirstOrDefault(d=>d.attack.directional);var lobber=MDTowerLibrary.All.FirstOrDefault(d=>d.attack.minRange>0);
        r["towers"]=$"gun {gun.id}, band {band?.id}, minimum range {lobber?.id}";
        while(Wait(.5))yield return true;

        if(Part("ranges")) {
            var rg=new Dictionary<string,object>();r["ranges"]=rg;
            var a=Place(gun,T+new Vector3(-6,0,-6),0);var extra=new List<ComponentMDUnit>{a};
            if(band!=null)extra.Add(Place(band,T+new Vector3(6,0,-8),0));if(lobber!=null)extra.Add(Place(lobber,T+new Vector3(0,0,-14),0));
            MDTowerTools.ShowAll=false;while(Wait(.6))yield return true;
            foreach(var x in Tap(Key.Z))yield return x;bool on=MDTowerTools.ShowAll;
            foreach(var x in Hold(T+new Vector3(0,22,14),T+new Vector3(0,0,-7)))yield return x;int rings=MDTowerWorld.RingsDrawn;Shot("t17_ranges_on");yield return true;
            foreach(var x in Tap(Key.Z))yield return x;bool off=!MDTowerTools.ShowAll;foreach(var x in Hold(T+new Vector3(0,22,14),T+new Vector3(0,0,-7)))yield return x;int after=MDTowerWorld.RingsDrawn;
            rg["ranges"]=$"Z -> shown {on}, rings drawn {rings} (towers in the lab {proj.Entities.Count(e=>e.FindComponent<ComponentMDUnit>()?.Kind=="Turret")}); Z again -> hidden {off}, rings {after}; message {MDTowerTools.LastText}";
            if(!on||rings<extra.Count||!off||after!=0)bad.Add("ranges: "+rg["ranges"]);
            Remove(extra);
        }
        if(Part("ghost")) {
            var gh=new Dictionary<string,object>();r["ghost"]=gh;var inv=pl.ComponentMiner.Inventory;inv.ActiveSlotIndex=0;inv.RemoveSlotItems(0,inv.GetSlotCount(0));
            inv.AddSlotItems(0,MDDeploymentBlock.Value(gun,1),1);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
            // two 1-high granite walls with a 1-cell lane between them: a 2 x 2 tower has no room in the lane
            int lx=(int)MathF.Floor(T.X)-6,lz=(int)MathF.Floor(T.Z)-1;var walls=new List<Point3>();for(int z=lz-2;z<=lz+2;z++)foreach(int x in new[]{lx-1,lx+1}){terrain.ChangeCell(x,70,z,Terrain.MakeBlockValue(3));walls.Add(new Point3(x,70,z));}
            while(Wait(.4))yield return true;
            pl.ComponentBody.Position=T+new Vector3(0,0,3);
            var eye=T+new Vector3(0,2.4f,3);foreach(var x in Hold(eye,T+new Vector3(0,0,-.5f),6))yield return x;
            var g1=MDTowerTools.Current;string free=g1==null?"none":$"has {g1.Has} ok {g1.Ok} at {g1.At} ({g1.Reason})";int ghosts=MDTowerWorld.GhostsDrawn;Shot("t17_ghost_free");yield return true;
            foreach(var x in Hold(new Vector3(lx+.5f,72.4f,lz+4.5f),new Vector3(lx+.5f,70,lz+.5f),6))yield return x;
            var g2=MDTowerTools.Current;string taken=g2==null?"none":$"has {g2.Has} ok {g2.Ok} at {g2.At} ({g2.Reason})";Shot("t17_ghost_taken");yield return true;
            gh["ghost"]=$"free ground: {free}, ghosts drawn {ghosts}; in a 1-cell lane: {taken}";
            if(g1==null||!g1.Has||!g1.Ok||ghosts<1||g2==null||g2.Ok||string.IsNullOrEmpty(g2.Reason))bad.Add("ghost: "+gh["ghost"]);
            inv.RemoveSlotItems(0,inv.GetSlotCount(0));foreach(var c in walls)terrain.ChangeCell(c.X,c.Y,c.Z,0);while(Wait(.3))yield return true;
        }
        if(Part("move")) {
            var mv=new Dictionary<string,object>();r["move"]=mv;var inv=pl.ComponentMiner.Inventory;inv.RemoveSlotItems(0,inv.GetSlotCount(0));
            var at0=T+new Vector3(-4,0,-6);var a=Place(gun,at0,0);a.SetHitpoints(.6f);var b=Place(gun,T+new Vector3(4,0,-6),0);while(Wait(.5))yield return true;
            pl.ComponentBody.Position=T+new Vector3(0,0,4);var eye=T+new Vector3(0,3,4);
            // M looking at a: picked up, the bar shown
            foreach(var x in Hold(eye,a.Body.Position+Vector3.UnitY*.8f))yield return x;foreach(var x in Tap(Key.M))yield return x;
            bool picked=MDTowerTools.Moving==a;var to=T+new Vector3(-2,0,-10);   // inside the lab's levelled ground (z >= origin - 35)
            foreach(var x in Hold(eye,to,5))yield return x;var gm=MDTowerTools.Current;
            {var cr=MDTowerTools.CameraRay(pl);var ts=proj.FindSubsystem<SubsystemTerrain>(true);var e1=cr.Position+cr.Direction*24;
             var h1=ts.Raycast(cr.Position,e1,false,true,(x,_)=>BlocksManager.Blocks[Terrain.ExtractContents(x)].IsCollidable_(x));var h2=ts.Raycast(cr.Position,e1,true,true,(x,_)=>BlocksManager.Blocks[Terrain.ExtractContents(x)].IsCollidable_(x));
             var ga=MDTowerTools.Aim(pl,gun,cr,24,a.Body,Quaternion.Identity);
             mv["aimDiag"]=$"input {MDTowerTools.LastInput}; camera ray {cr.Position} {cr.Direction}; terrain hit (boxes off) {h1?.Distance} {h1?.CellFace}, (on) {h2?.Distance} {h2?.CellFace}; Aim: has {ga.Has} ok {ga.Ok} at {ga.At} ({ga.Reason})";}
            var yaw0=a.Yaw;foreach(var x in Tap(Key.R))yield return x;foreach(var x in Hold(eye,to,3))yield return x;
            var gr=MDTowerTools.Current;float turned=gr==null?float.NaN:MathF.Abs(MDCombat.AngleDelta(MathF.Atan2(-Matrix.CreateFromQuaternion(gr.Rotation).Forward.X,-Matrix.CreateFromQuaternion(gr.Rotation).Forward.Z),yaw0));
            var barW=pl.ComponentGui.ControlsContainerWidget.Children.OfType<MDMoveBar>().FirstOrDefault();bool barShown=barW!=null&&barW.IsVisible&&barW.BoxSize.X>100&&barW.BoxSize.Y>40;mv["bar"]=barW==null?"none":$"box {barW.BoxSize} at {barW.BoxAt}: {barW.Line}";Shot("t17_move_ghost");yield return true;
            bool mounted=pl.ComponentRider?.Mount!=null;
            foreach(var x in Click(.5))yield return x;
            var moved=a.Entity.FindComponent<ComponentMDTowerBody>(true).Anchor;bool put=MDTowerTools.Moving==null&&Vector3.Distance(new Vector3(moved.X,0,moved.Z),new Vector3(gr?.At.X??0,0,gr?.At.Z??0))<.01f&&Vector3.Distance(moved,at0)>4;
            float yawAfter=MDCombat.AngleDelta(a.Yaw,yaw0);bool kept=MathF.Abs(a.Damage.Hitpoints-.6f)<.01f;bool barGone=!pl.ComponentGui.ControlsContainerWidget.Children.OfType<MDMoveBar>().Any();
            mv["move"]=$"M on the tower: picked {picked}; ghost at the target ok {gm?.Ok} ({gm?.Reason}); R turned the ghost {turned*180/MathF.PI:0} deg, the mount key mounted {mounted}; bar shown {barShown}; "+
                $"click: put {put} at {moved} (ghost {gr?.At}), yaw {yawAfter*180/MathF.PI:0} deg from before, health kept {kept} ({a.Damage.Hitpoints:0.00}), bar gone {barGone}; message {MDTowerTools.LastText}";
            if(!picked||gm==null||!gm.Ok||MathF.Abs(turned-MathF.PI/2)>.05f||mounted||!barShown||!put||MathF.Abs(MathF.Abs(yawAfter)-MathF.PI/2)>.05f||!kept||!barGone)bad.Add("move: "+mv["move"]);
            // it fires from the new place
            // it fires from the new place: the same check once more on a tower that was not moved (the control), each with what it did
            var dum=Dummy(moved+new Vector3(0,0,-7));var ctl=Place(gun,T+new Vector3(12,0,-6),0);var dum2=Dummy(T+new Vector3(12,0,-13));
            var seen=new List<string>();int shots0=a.ShotCount,ctl0=ctl.ShotCount;bool locked=false;
            foreach(var x in During(6,()=>{locked|=a.Target==dum.Body;if(seen.Count<6&&Time.FrameIndex%40==0)seen.Add($"target {(a.Target==dum.Body?"dummy":a.Target==null?"none":"other")} aim {a.AimYaw:0.00} shots {a.ShotCount-shots0} | control target {(ctl.Target==dum2.Body?"dummy":ctl.Target==null?"none":"other")} shots {ctl.ShotCount-ctl0}");}))yield return x;
            mv["fires"]=$"a dummy 7 m from the new place hit {dum.Hit} (shots {a.ShotCount-shots0}); control tower hit {dum2.Hit} (shots {ctl.ShotCount-ctl0}); {string.Join(" / ",seen)}";
            // the dummy's health is no proof (a lab wolf heals; the control tower's hit showed up on one run in two): it locks on and fires
            if(!locked||a.ShotCount<=shots0)bad.Add("move: the moved tower did not take the dummy and fire: "+mv["fires"]);Remove([dum,dum2]);Remove([ctl]);
            // M on b, M again: cancelled, b stays
            var bAt=b.Body.Position;foreach(var x in Hold(eye,b.Body.Position+Vector3.UnitY*.8f))yield return x;foreach(var x in Tap(Key.M))yield return x;bool picked2=MDTowerTools.Moving==b;
            foreach(var x in Tap(Key.M))yield return x;bool cancelled=MDTowerTools.Moving==null&&Vector3.Distance(b.Body.Position,bAt)<.01f;string cancelText=MDTowerTools.LastText;
            // b onto a's new place: refused with a reason, b stays, still picked up
            foreach(var x in Hold(eye,b.Body.Position+Vector3.UnitY*.8f))yield return x;foreach(var x in Tap(Key.M))yield return x;
            foreach(var x in Hold(eye,moved,4))yield return x;var gb=MDTowerTools.Current;foreach(var x in Click(.4))yield return x;
            bool refused=Vector3.Distance(b.Body.Position,bAt)<.01f&&MDTowerTools.Moving==b;string refusedText=MDTowerTools.LastText;
            foreach(var x in Tap(Key.M))yield return x;
            // M looking at nothing: a hint, nothing picked
            foreach(var x in Hold(eye,eye+new Vector3(0,5,-3)))yield return x;foreach(var x in Tap(Key.M))yield return x;bool none=MDTowerTools.Moving==null;string noneText=MDTowerTools.LastText;
            mv["cancel"]=$"M on b picked {picked2}, M again cancelled {cancelled} ({cancelText}); onto a's spot: ghost ok {gb?.Ok}, refused {refused} ({refusedText}); M at the sky: none {none} ({noneText})";
            if(!picked2||!cancelled||gb==null||gb.Ok||!refused||!none)bad.Add("move: "+mv["cancel"]);
            Remove([a,b]);MDTowerTools.Stop(pl,"");while(Wait(.3))yield return true;
        }
        if(Part("terrain")) {
            var tr=new Dictionary<string,object>();r["terrain"]=tr;var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
            int dirt=Terrain.MakeBlockValue(2),granite=Terrain.MakeBlockValue(3);var C0=T+new Vector3(0,0,-14);int cx=(int)MathF.Floor(C0.X),cz=(int)MathF.Floor(C0.Z),top=69;
            // a field 40 x 12: x < cx dirt, x >= cx granite, 6 deep over the bedrock
            void Field(){for(int x=cx-20;x<cx+20;x++)for(int z=cz-6;z<cz+6;z++){for(int y=top-5;y<=top;y++)terrain.ChangeCell(x,y,z,x<cx?dirt:granite);for(int y=top+1;y<top+8;y++)terrain.ChangeCell(x,y,z,0);}}
            int Changed(int x0,int x1){int n=0;for(int x=x0;x<x1;x++)for(int z=cz-6;z<cz+6;z++)for(int y=top-5;y<=top;y++)if(terrain.Terrain.GetCellValue(x,y,z)!=(x<cx?dirt:granite))n++;return n;}
            Field();while(Wait(.5))yield return true;
            var mech=DatabaseManager.CreateEntity(proj,MDMechType.Mercury.Template,true);mech.FindComponent<ComponentBody>(true).Position=T+new Vector3(0,0,10);mech.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(mech);
            var mu=mech.FindComponent<ComponentMDUnit>(true);var artillery=Place(lobber??gun,T+new Vector3(8,0,10),0);while(Wait(.6))yield return true;
            IEnumerable<bool> Shell(ComponentMDUnit owner,MDTowerDef d,Vector3 at){w.LaunchShell(owner,at+new Vector3(0,6,0),new Vector3(0,-40,0),0,d.attack.power,d.attack.splash,false,d);while(Wait(.8))yield return true;}
            var pts=new Vector3(cx-14.5f,top+1,cz+.5f);   // in the dirt
            MDSettings.MechTerrain=false;foreach(var x in Shell(mu,MDMechBrain.CannonWeapon,pts))yield return x;int off=Changed(cx-20,cx);
            MDSettings.MechTerrain=true;int b0=MDTerrainBlast.Blasts;foreach(var x in Shell(artillery,artillery.Tower,pts))yield return x;int towerShell=Changed(cx-20,cx);int towerBlasts=MDTerrainBlast.Blasts-b0;
            // the cannon in the dirt with a boat, a tower and the player beside it
            var boat=DatabaseManager.CreateEntity(proj,"Boat",true);var spot=new Vector3(cx-8.5f,top+1,cz+.5f);boat.FindComponent<ComponentBody>(true).Position=spot+new Vector3(1.5f,.2f,0);proj.AddEntity(boat);
            var near=Place(gun,spot+new Vector3(0,0,3.5f),0);float nearHp=near.Damage.Hitpoints;var pp=pl.ComponentBody.Position;pl.ComponentBody.Position=spot+new Vector3(-2.5f,0,0);float plHealth=pl.ComponentHealth.Health;
            while(Wait(.5))yield return true;var boatDmg=boat.FindComponent<ComponentDamage>(true);float boat0=boatDmg.Hitpoints;int spared0=MDTerrainBlast.Spared;
            foreach(var x in Shell(mu,MDMechBrain.CannonWeapon,spot))yield return x;int cannon=Changed(cx-20,cx)-towerShell;
            float boat1=boat.IsAddedToProject?boatDmg.Hitpoints:0;int spared=MDTerrainBlast.Spared-spared0;bool nearKept=near.Damage.Hitpoints>=nearHp-.001f,plKept=pl.ComponentHealth.Health>=plHealth-.001f;
            Cam(spot+new Vector3(-6,7,9),spot);for(int i=0;i<6;i++)yield return true;Shot("t17_cannon_crater");yield return true;
            pl.ComponentBody.Position=pp;
            // smoke: the same blast (the cannon's radius) with the old smoke (1) and the mech's (MDMechBrain.BlastSmoke), terrain off; particles alive
            // 0.1 s and 0.8 s after, and a shot of each at 0.8 s (user: "火炮和榴弹的烟雾效果可以减弱一点")
            {MDSettings.MechTerrain=false;var cw=MDMechBrain.CannonWeapon;var sm=new List<string>();
             foreach(var (f,name) in new[]{(1f,"old"),(MDMechBrain.BlastSmoke,"new")}) {
                while(Wait(3))yield return true;var at=new Vector3(cx-4.5f,top+1,cz+.5f);int p0=w.Particles;
                w.Explosion(at,Math.Max(cw.attack.splash,.5f),"heavy",new Color(cw.fx.color[0],cw.fx.color[1],cw.fx.color[2]),new Color(cw.fx.color2[0],cw.fx.color2[1],cw.fx.color2[2]),f);
                while(Wait(.1))yield return true;int p1=w.Particles-p0;Cam(at+new Vector3(-6,7,9),at);while(Wait(.7))yield return true;int p2=w.Particles-p0;Shot("t17_smoke_"+name);yield return true;
                sm.Add($"{name} (smoke x{f}): {p1} particles at 0.1 s, {p2} at 0.8 s");
             }
             tr["smoke"]=string.Join("; ",sm);}
            // the grenade in the granite
            int g0=Changed(cx,cx+20);foreach(var x in Shell(mu,MDMechBrain.GrenadeWeapon,new Vector3(cx+8.5f,top+1,cz+.5f)))yield return x;int grenade=Changed(cx,cx+20)-g0;
            tr["switch"]=$"setting off: cannon changed {off} cells; on: a tower's shell {towerShell} ({towerBlasts} blasts), the cannon in dirt {cannon}, the grenade in granite {grenade}";
            tr["beside"]=$"boat hit points {boat0:0.00} -> {boat1:0.00}; spared bodies {spared}; the tower beside kept {nearKept}, the player kept {plKept}; smoke trails of thrown blocks left out {MDTerrainBlastTrails.Skipped}, smoke cells skipped {MDTerrainBlastSmoke.Skipped} / put out {MDTerrainBlastUpdate.CellsOff}; thrown blocks' hits on spared bodies {MDTerrainBlast.DebrisSpared}; tower beside {nearHp:0.000} -> {near.Damage.Hitpoints:0.000}";
            if(off!=0||towerShell!=0||towerBlasts!=0||cannon<20||boat1>=boat0||spared<2||!nearKept||!plKept)bad.Add("terrain: "+tr["switch"]+"; "+tr["beside"]);
            // pressures (MDTerrainBlast directly, a fresh field each): cells broken in dirt / granite
            var sweep=new List<string>();tr["pressures"]="(started)";tr["game explosives"]=string.Join(", ",BlocksManager.Blocks.Where(b=>b!=null&&b.GetExplosionPressure(Terrain.MakeBlockValue(b.BlockIndex))>0).Select(b=>$"{b.GetType().Name} {b.GetExplosionPressure(Terrain.MakeBlockValue(b.BlockIndex)):0}"));
            string step="sweep";
            foreach(float pr in new[]{40f,80f,160f,320f}) {
                step=$"field {pr}";Field();while(Wait(.4))yield return true;step=$"blast {pr}";
                MDTerrainBlast.Blast(proj,new Vector3(cx-10.5f,top+1.05f,cz+.5f),-Vector3.UnitY,pr);MDTerrainBlast.Blast(proj,new Vector3(cx+10.5f,top+1.05f,cz+.5f),-Vector3.UnitY,pr);while(Wait(.8))yield return true;
                step=$"count {pr}";sweep.Add($"{pr:0}: dirt {Changed(cx-20,cx)}, granite {Changed(cx,cx+20)}");tr["pressures"]=string.Join("; ",sweep)+" (at "+step+")";
            }
            tr["pressures"]=string.Join("; ",sweep);tr["used"]=$"cannon {MDMechBrain.CannonPressure}, grenade {MDMechBrain.GrenadePressure}";
            MDSettings.MechTerrain=false;Remove([near,artillery]);if(mech.IsAddedToProject)proj.RemoveEntity(mech,true);if(boat.IsAddedToProject)proj.RemoveEntity(boat,true);
        }
        if(bad.Count>0)throw new Exception("tools17: "+string.Join(" || ",bad));
    }
}
