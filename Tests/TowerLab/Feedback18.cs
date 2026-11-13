using System.Reflection;
using Engine;
using Engine.Input;
using Game;
using HarmonyLib;

namespace TowerLab;

// A right-click for the lab: the ray the game builds from a right-click or a long tap (PlayerInput.Interact), set before the mod
// and the game read the input (the game then calls the OnPlayerInputInteract hook as for a real click).
[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class LabInteract {
    public static bool Pending;
    [HarmonyPriority(Priority.First)]
    public static void Postfix(ComponentInput __instance){if(!Pending)return;Pending=false;var c=__instance.m_componentPlayer.GameWidget.ActiveCamera;__instance.m_playerInput.Interact=new Ray3(c.ViewPosition,c.ViewDirection);}
}

// lab --mode fb18 (0.18.0, six player reports via the user, 2026-11-04; parts fb18-<part>). Results under "fb18"; screenshots f18_*.
// Each fix has a lab switch that brings back the 0.17.0 behaviour, so one run gives the numbers before and after:
// reach   (MDTowerBrain.ReachEnabled): every tower kind against a fish in a pond, a target in a cave and one on open ground, then the
//         mortar firing for real: before it takes the fish / the cave and fires, after it never does and still fires at the open one.
// core    the core's light column from 40 / 150 / 260 m, a right-click on the core shows every tower's ring and health bar and hides
//         them again, Z on one tower agrees with it; the core destroyed: blast, towers within 30 m offline (they stop firing, red line),
//         one 45 m off keeps firing, the terrain untouched with the setting off (broken with it on), a new core brings them back.
// stuck   (ComponentMDRobot.SmartEnabled): a tank in a 3-deep pit, a walker behind a wall it sees over, the siege boss RD19 in a pit:
//         before they stay and fire into the walls, after they climb out / go round and fire only along clear lines.
// spawn   spots by a cliff: the old spot finder against the new one with the siege boss's body.
// control (MDTowerWorld.TenacityEnabled): two bosses under the same stream of slows, element hits (reactions) and control hits for
//         40 s each, the time they are held (slowed below x0.75 or staggered) before and after.
// stagger (TenacityEnabled): a boss asked for a 4 s stagger every 3 s for 40 s: the time it stands staggered before and after.
// missile (MDMechBrain.MissileCooldownEnabled): each mech tapping the missile trigger for 6 s: missiles before and after, and the HUD.
public partial class Lab {
    static IEnumerable<bool> Feedback18() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb18"]=r;var bad=new List<string>();results["fb18bad"]=bad;
        bool Part(string p)=>mode=="fb18"||mode=="fb18-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var clock=proj.FindSubsystem<SubsystemTime>(true);
        int dirt=Terrain.MakeBlockValue(2),water=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<WaterBlock>()),granite=Terrain.MakeBlockValue(3);
        var keep=new HashSet<GameEntitySystem.Entity>();
        // wild animals come in after a while and the towers would shoot them: away with every creature the test did not make
        void Tidy(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null&&!keep.Contains(e)&&!dums.Any(d=>d.Entity==e)).ToList())proj.RemoveEntity(e,true);}
        // a block of dirt from y 60 to 69 (the lab ground is bedrock under its top layer) and air over it
        void Ground(int x0,int x1,int z0,int z1){for(int x=x0;x<x1;x++)for(int z=z0;z<z1;z++){for(int y=60;y<70;y++)terrain.ChangeCell(x,y,z,dirt);for(int y=70;y<90;y++)terrain.ChangeCell(x,y,z,0);}}
        void Box(int x0,int x1,int y0,int y1,int z0,int z1,int v){for(int x=x0;x<x1;x++)for(int z=z0;z<z1;z++)for(int y=y0;y<y1;y++)terrain.ChangeCell(x,y,z,v);}
        int X(float v)=>(int)MathF.Floor(v);
        GameEntitySystem.Entity Creature(string template,Vector3 at,bool still=true) {
            var e=DatabaseManager.CreateEntity(proj,template,true);e.FindComponent<ComponentBody>(true).Position=at;e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;
            if(e.FindComponent<ComponentSpawn>() is {} sp)sp.AutoDespawn=false;proj.AddEntity(e);keep.Add(e);
            var c=e.FindComponent<ComponentCreature>(true);c.ComponentHealth.AttackResilience=1e7f;
            if(still){var l=c.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.SwimSpeed=l.FlySpeed=0;}
            return e;
        }
        ComponentMDRobot Robot(string t,Vector3 at,float yaw=0) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;e.FindComponent<ComponentSpawn>(true).AutoDespawn=false;proj.AddEntity(e);keep.Add(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        void Kill(ComponentMDRobot x){if(x!=null&&x.Entity.IsAddedToProject)proj.RemoveEntity(x.Entity,true);}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        var findTarget=typeof(MDTowerBrain).GetMethod("FindTarget",BindingFlags.NonPublic|BindingFlags.Static);
        ComponentBody Find(ComponentMDUnit u)=>(ComponentBody)findTarget.Invoke(null,[u]);
        pl.ComponentBody.Position=Home;Tidy();

        // ---------------------------------------------------------------- reach
        if(Part("reach")) {
            var rr=new Dictionary<string,object>();r["reach"]=rr;
            var R0=origin+new Vector3(-12,0,-18);int rx=X(R0.X),rz=X(R0.Z);
            Ground(rx-18,rx+18,rz-4,rz+18);
            // two ponds (5 x 5, 5 deep) east at 6 and 14 m, two caves (5 x 5, 3 high, roof 2 thick) north at 6 and 14 m
            foreach(int k in new[]{6,14})Box(rx+k-2,rx+k+3,65,70,rz-2,rz+3,water);
            foreach(int k in new[]{6,14})Box(rx-2,rx+3,65,68,rz+k-2,rz+k+3,0);
            while(Wait(1))yield return true;
            // a fish (the first water predator this game has), a wolf in each cave, a wolf on open ground west at 6 and 14 m
            string fishName=new[]{"Piranha","Shark_Bull","Shark_Tiger","Barracuda"}.FirstOrDefault(n=>DatabaseManager.FindEntityValuesDictionary(n,false)!=null);
            var fish=new List<ComponentBody>();
            foreach(int k in new[]{6,14}) {
                var e=fishName!=null?Creature(fishName,new Vector3(rx+k+.5f,66.2f,rz+.5f)):null;
                var d=Dummy(new Vector3(rx+k+.5f,65.05f,rz+.5f+1.5f));keep.Add(d.Entity);typeof(ComponentHealth).GetProperty("AirCapacity")?.SetValue(d.C.ComponentHealth,-1f);   // a wolf on the pond's floor as well (under water; it does not drown)
                if(e!=null)fish.Add(e.FindComponent<ComponentBody>(true));fish.Add(d.Body);
            }
            var cave=new List<ComponentBody>();foreach(int k in new[]{6,14}){var d=Dummy(new Vector3(rx+.5f,65.05f,rz+k+.5f));keep.Add(d.Entity);cave.Add(d.Body);}
            while(Wait(1.5))yield return true;
            rr["fish"]=$"{fishName??"none in this game"} ×{fish.Count(b=>b.Entity.FindComponent<ComponentMDRobot>()==null&&b.Entity.ValuesDictionary.DatabaseObject.Name!="Wolf")}, wolves on the ponds' floors; submerged: {string.Join(",",fish.Select(b=>MDTowerBrain.Submerged(b)))}; in the caves submerged: {string.Join(",",cave.Select(MDTowerBrain.Submerged))}";
            var unreachable=new HashSet<ComponentBody>(fish.Concat(cave));
            // every tower kind on R0: what it takes with the old choice and the new one, with only the unreachable targets about
            var census=new List<string>();var tookBefore=new List<string>();var tookAfter=new List<string>();var missedOpen=new List<string>();
            var kinds=MDTowerLibrary.All.Where(d=>d.attack.type is not ("radar" or "heal" or "core")).ToList();
            var open=new List<ComponentBody>();
            foreach(var phase in new[]{"unreachable","open"}) {
                if(phase=="open")foreach(int k in new[]{6,14}){var d=Dummy(new Vector3(rx-k+.5f,70.05f,rz+.5f));keep.Add(d.Entity);open.Add(d.Body);}
                foreach(var d in kinds) {
                    var u=Place(d,R0,MathF.PI/2);   // facing -X? yaw pi/2: the directional sprayers' band points west (to the open targets)
                    foreach(var x in Seconds(.12))yield return x;
                    MDTowerBrain.ReachEnabled=false;var before=Find(u);MDTowerBrain.ReachEnabled=true;var after=Find(u);
                    string Name(ComponentBody b)=>b==null?"-":unreachable.Contains(b)?(cave.Contains(b)?"CAVE":"WATER"):"open";
                    if(phase=="unreachable"){if(before!=null)tookBefore.Add($"{d.id}:{Name(before)}");if(after!=null)tookAfter.Add($"{d.id}:{Name(after)}");}
                    else if(after==null||unreachable.Contains(after))missedOpen.Add($"{d.id}:{Name(after)}");
                    census.Add($"{phase} {d.id} ({d.attack.type}{(d.attack.needsSight?"":", no sight")}): 0.17.0 {Name(before)} / 0.18.0 {Name(after)}");
                    Remove([u]);
                }
            }
            rr["census"]=census;rr["unreachableTakenBefore"]=tookBefore;rr["unreachableTakenAfter"]=tookAfter;rr["openMissedAfter"]=missedOpen;
            if(tookAfter.Count>0)bad.Add("reach: towers still take unreachable targets: "+string.Join(" ",tookAfter));
            // directional sprayers reach only their band and the AA / long-range kinds have minimum ranges: a tower that can never see
            // the open targets here is not a failure; one that takes nothing although an open target is in its area is
            var missedReal=missedOpen.Where(m=>{var id=m[..m.IndexOf(':')];var d=MDTowerLibrary.Get(id);return !d.attack.directional&&d.attack.range>=6.5f;}).ToList();
            if(missedReal.Count>0)bad.Add("reach: towers that no longer take the open target: "+string.Join(" ",missedReal));
            Remove(dums.Where(d=>open.Contains(d.Body)).ToList());open.Clear();
            // the mortar for real: 12 s with only the unreachable targets (the old choice, then the new one), then a wolf in the open
            var mortarDef=MDTowerLibrary.Get("battle_rocket_1");var m=Place(mortarDef,R0,0);
            pl.ComponentBody.Position=R0+new Vector3(-8,0,-3);
            foreach(var x in Seconds(1))yield return x;
            var fired=new Dictionary<string,string>();
            foreach(var reach in new[]{false,true}) {
                MDTowerBrain.ReachEnabled=reach;m.Target=null;m.ScanTimer=0;foreach(var x in Seconds(.5))yield return x;int s0=m.ShotCount;var locked=new HashSet<string>();
                foreach(var x in Seconds(12,()=>{Tidy();if(m.Target!=null)locked.Add(cave.Contains(m.Target)?"cave":fish.Contains(m.Target)?"water":"other");}))yield return x;
                fired[reach?"0.18.0":"0.17.0"]=$"locked [{string.Join(",",locked)}], shells {m.ShotCount-s0}";
                if(!reach){Cam(R0+new Vector3(-6,9,-12),R0+new Vector3(5,0,6));Shot("f18_reach_before");yield return true;}
            }
            var dOpen=Dummy(new Vector3(rx-14+.5f,70.05f,rz+.5f));keep.Add(dOpen.Entity);int s1=m.ShotCount;bool lockedOpen=false;
            foreach(var x in Seconds(12,()=>{Tidy();lockedOpen|=m.Target==dOpen.Body;}))yield return x;
            fired["0.18.0 with a wolf in the open at 14 m"]=$"locked it {lockedOpen}, shells {m.ShotCount-s1}, hit {dOpen.Hit}";
            Cam(R0+new Vector3(-6,9,-12),R0+new Vector3(-6,0,4));Shot("f18_reach_open");yield return true;
            rr["mortar"]=fired;rr["unreachableSkipped"]=MDTowerBrain.Unreachable;rr["arcTraces"]=MDCombat.ArcTraces;
            if(fired["0.18.0"].Contains("cave")||fired["0.18.0"].Contains("water")||!fired["0.18.0"].EndsWith("shells 0")||!lockedOpen||m.ShotCount-s1<1)bad.Add("reach: mortar "+string.Join(" | ",fired.Select(kv=>kv.Key+": "+kv.Value)));
            Remove([m]);Remove(dums.ToList());foreach(var e in keep.ToList())if(e.IsAddedToProject)proj.RemoveEntity(e,true);keep.Clear();
            Ground(rx-18,rx+18,rz-4,rz+18);while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- core
        if(Part("core")) {
            var cr=new Dictionary<string,object>();r["core"]=cr;
            var C0=origin+new Vector3(40,0,-15);int cx=X(C0.X),cz=X(C0.Z);Ground(cx-25,cx+35,cz-20,cz+42);while(Wait(.5))yield return true;
            var defs=MDTowerLibrary.All;var coreDef=defs.First(d=>d.attack.type=="core");
            var core=Place(coreDef,C0,0);MDThreat.Placed(core,pl);
            var near=new List<ComponentMDUnit>{Place(defs.First(d=>d.id=="battle_turret_1"),C0+new Vector3(-8,0,4),0),Place(defs.First(d=>d.id=="battle_rocket_1"),C0+new Vector3(9,0,6),0),
                Place(defs.First(d=>d.id=="battle_medic_1"),C0+new Vector3(-6,0,-9),0),Place(defs.First(d=>d.id=="radar_1"),C0+new Vector3(6,0,-8),0),Place(defs.First(d=>d.id=="emp_1"),C0+new Vector3(0,0,14),0)};
            var far=Place(defs.First(d=>d.id=="battle_turret_1"),C0+new Vector3(28,0,32),0);   // 42 m off
            near[0].SetHitpoints(.35f);near[1].SetHitpoints(.7f);
            pl.ComponentBody.Position=C0+new Vector3(0,0,-14);while(Wait(1))yield return true;
            // the light column from near and far (the lab ground ends 35-75 m from the core; further out it is the game's own land)
            var beam=new List<string>();
            foreach(var (name,eye) in new[]{("near",C0+new Vector3(-10,4,-30)),("far150",C0+new Vector3(-100,40,-110)),("far260",C0+new Vector3(-190,60,-180))}) {
                pl.ComponentBody.Position=new Vector3(eye.X,90,eye.Z);pl.ComponentBody.Velocity=Vector3.Zero;   // the terrain loads round the player: stand by the camera (in the air)
                foreach(var x in Seconds(name=="near"?.3:2.5,()=>{pl.ComponentBody.Position=new Vector3(eye.X,90,eye.Z);pl.ComponentBody.Velocity=Vector3.Zero;Cam(eye,C0+Vector3.UnitY*(name=="near"?14:30));}))yield return x;beam.Add($"{name}: {MDTowerWorld.BeamsDrawn} quads at {Vector3.Distance(eye,C0):0} m");Shot("f18_beam_"+name);yield return true;
            }
            pl.ComponentBody.Position=C0+new Vector3(0,0,-14);pl.ComponentBody.Velocity=Vector3.Zero;foreach(var x in Seconds(1.5))yield return x;
            cr["beam"]=beam;if(!beam.All(b=>!b.Contains(": 0 quads")))bad.Add("core: beam not drawn "+string.Join("; ",beam));
            // a right-click on the core: every tower's ring and bar; Z on one hides it (all off); the core again: all; again: none
            var eyeC=C0+new Vector3(0,2.6f,-3.6f);var steps=new List<string>();int t0=MDTowerTools.CoreToggles;
            IEnumerable<bool> ClickCore(){pl.ComponentBody.Position=C0+new Vector3(0,0,-3.2f);pl.ComponentBody.Velocity=Vector3.Zero;foreach(var x in Hold(eyeC,core.Body.Position+Vector3.UnitY*2.5f,3))yield return x;LabInteract.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<.4||LabInteract.Pending;){Cam(eyeC,core.Body.Position+Vector3.UnitY*2.5f);yield return true;}}
            IEnumerable<bool> Look(Vector3 at,int frames=4){foreach(var x in Hold(eyeC,at,frames))yield return x;}
            void Step(string what)=>steps.Add($"{what}: all {MDTowerTools.ShowAll}, shown {MDTowerTools.Shown.Count}, rings {MDTowerWorld.RingsDrawn}, fills {MDTowerWorld.FillsDrawn}, bars {MDTowerWorld.TowerBarsDrawn} ({MDTowerTools.LastText})");
            MDTowerTools.Shown.Clear();MDTowerTools.ShowAll=false;
            typeof(MDTowerTools).GetField("CoreToggleEnabled")?.SetValue(null,true);   // 1.0.0 (11-13): the core's click opens its store; this part checks the 0.18.0 toggle
            foreach(var x in ClickCore())yield return x;bool viaGame=MDTowerTools.CoreToggles>t0;
            if(!viaGame){MDTowerTools.ToggleAll(pl);steps.Add("the injected right-click did not reach the hook; ToggleAll called directly");}   // fall back, and say so
            foreach(var x in Look(C0,4))yield return x;Step("core clicked");int allN=MDTowerTools.Shown.Count,bars=MDTowerWorld.TowerBarsDrawn;bool allOn=MDTowerTools.ShowAll;
            foreach(var x in Hold(C0+new Vector3(-2,30,-20),C0+new Vector3(0,0,4),5))yield return x;Shot("f18_core_all_on");yield return true;
            LabKeys.Taps.Enqueue(Key.Z);foreach(var x in Hold(eyeC,near[0].Body.Position+Vector3.UnitY*1.2f,1))yield return x;while(LabKeys.Busy){Cam(eyeC,near[0].Body.Position+Vector3.UnitY*1.2f);yield return true;}
            foreach(var x in Look(near[0].Body.Position+Vector3.UnitY*1.2f,3))yield return x;Step("Z on the gun tower");bool zHid=!MDTowerTools.Shown.Contains(near[0])&&!MDTowerTools.ShowAll&&MDTowerTools.Shown.Count==allN-1;
            int t1=MDTowerTools.CoreToggles;foreach(var x in ClickCore())yield return x;if(MDTowerTools.CoreToggles==t1)MDTowerTools.ToggleAll(pl);Step("core clicked again");bool back=MDTowerTools.ShowAll&&MDTowerTools.Shown.Count==allN;
            t1=MDTowerTools.CoreToggles;foreach(var x in ClickCore())yield return x;if(MDTowerTools.CoreToggles==t1)MDTowerTools.ToggleAll(pl);foreach(var x in Look(C0,3))yield return x;Step("core clicked once more");bool none=!MDTowerTools.ShowAll&&MDTowerTools.Shown.Count==0&&MDTowerWorld.RingsDrawn==0&&MDTowerWorld.TowerBarsDrawn==0;
            cr["toggle"]=steps;cr["toggleViaGame"]=viaGame;typeof(MDTowerTools).GetField("CoreToggleEnabled")?.SetValue(null,false);
            if(!allOn||allN!=near.Count+1||bars<1||!zHid||!back||!none)bad.Add("core: toggle "+string.Join(" | ",steps));
            // destroyed: the blast, the towers within 30 m offline, the far one firing on; terrain untouched with the setting off
            var dNear=Dummy(near[0].Body.Position+new Vector3(-7,0,0));var dFar=Dummy(far.Body.Position+new Vector3(-7,0,0));keep.Add(dNear.Entity);keep.Add(dFar.Entity);
            MDSettings.MechTerrain=false;int Changed(){int n=0;for(int x=cx-8;x<cx+8;x++)for(int z=cz-8;z<cz+8;z++)for(int y=64;y<70;y++)if(terrain.Terrain.GetCellValue(x,y,z)!=dirt)n++;return n;}
            int changed0=Changed();int blasts0=MDTerrainBlast.Blasts;
            foreach(var x in Hold(C0+new Vector3(-14,9,-20),C0+Vector3.UnitY*2,3))yield return x;
            core.Damage.Hitpoints=0;
            foreach(var x in Seconds(.12,()=>Cam(C0+new Vector3(-14,9,-20),C0+Vector3.UnitY*2)))yield return x;Shot("f18_core_blast");yield return true;
            foreach(var x in Seconds(.35,()=>Cam(C0+new Vector3(-14,9,-20),C0+Vector3.UnitY*2)))yield return x;Shot("f18_core_blast_late");yield return true;
            var offline=near.Select(u=>u.Offline).ToList();int sNear=near[0].ShotCount,sFar=far.ShotCount;
            foreach(var x in Seconds(6,()=>Tidy()))yield return x;
            int nearShots=near[0].ShotCount-sNear,farShots=far.ShotCount-sFar;
            var vd=new TemplatesDatabase.ValuesDictionary();near[0].Save(vd,null);float savedLeft=vd.GetValue("OfflineLeft",0f);
            foreach(var x in Hold(near[0].Body.Position+new Vector3(-3,4,-9),near[0].Body.Position+Vector3.UnitY*1.5f,5))yield return x;int lines=MDTowerWorld.OfflineDrawn;Shot("f18_offline");yield return true;
            foreach(var x in Hold(C0+new Vector3(-2,18,-24),C0+new Vector3(0,0,4),5))yield return x;Shot("f18_offline_wide");yield return true;
            cr["destroyed"]=$"towers put out {MDThreat.LastDown} (within {MDThreat.DownRadius} m: offline {string.Join(",",offline)}; far one offline {far.Offline}); in 6 s the near gun fired {nearShots}, the far gun {farShots}; red lines drawn {lines}; "+
                $"saved OfflineLeft {savedLeft:0} s; terrain cells changed {Changed()-changed0} (setting off), game explosions {MDTerrainBlast.Blasts-blasts0}; announced: {MDThreat.LastAnnounce}";
            if(offline.Any(o=>!o)||far.Offline||nearShots!=0||farShots<1||lines<1||Changed()!=changed0||savedLeft<100)bad.Add("core: destroyed "+cr["destroyed"]);
            // a new core: they come back and fire
            var core2=Place(coreDef,C0+new Vector3(0,0,-4),0);MDThreat.Placed(core2,pl);sNear=near[0].ShotCount;bool backOn=near.All(u=>!u.Offline);
            foreach(var x in Seconds(6,()=>Tidy()))yield return x;
            cr["rebuilt"]=$"all back {backOn} ({MDThreat.LastRestored} restored), the near gun fired {near[0].ShotCount-sNear} in 6 s";
            if(!backOn||near[0].ShotCount-sNear<1)bad.Add("core: rebuilt "+cr["rebuilt"]);
            // the setting on: the game's own explosion breaks the ground
            MDSettings.MechTerrain=true;int c1=Changed();blasts0=MDTerrainBlast.Blasts;core2.Damage.Hitpoints=0;foreach(var x in Seconds(1.5))yield return x;
            cr["terrainOn"]=$"cells changed {Changed()-c1}, game explosions {MDTerrainBlast.Blasts-blasts0}";MDSettings.MechTerrain=false;
            if(MDTerrainBlast.Blasts-blasts0<1)bad.Add("core: terrain setting on "+cr["terrainOn"]);
            Remove(near);Remove([far]);foreach(var u in proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u?.Tower?.attack.type=="core").ToList())Remove([u]);
            Remove(dums.ToList());foreach(var e in keep.ToList())if(e.IsAddedToProject)proj.RemoveEntity(e,true);keep.Clear();MDTowerTools.Shown.Clear();MDTowerTools.ShowAll=false;
            Ground(cx-25,cx+35,cz-20,cz+42);while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- stuck
        if(Part("stuck")) {
            var sr=new Dictionary<string,object>();r["stuck"]=sr;
            var S0=origin+new Vector3(5,0,38);int sx=X(S0.X),sz=X(S0.Z);
            var gunDef=MDTowerLibrary.Get("battle_turret_1");
            string Case(ComponentMDRobot rb,float floorY,float startY)=>$"rose {rb.Body.Position.Y-startY:0.0} m (surface {70-floorY:0} m over the floor), fired {rb.ShotsFired}, into walls {rb.WallShots}, held {rb.HeldShots}, jumps {rb.Unstuck.Jumps}, climbs {rb.Unstuck.Climbs}";
            // a tank in a 3-deep pit (8 x 8), a gun tower on the ground 16 m off
            foreach(var smart in new[]{false,true}) {
                Ground(sx-14,sx+26,sz-14,sz+14);Box(sx-4,sx+4,67,70,sz-4,sz+4,0);
                var tower=Place(gunDef,S0+new Vector3(16,0,0),0);tower.Damage.AttackResilience=1e6f;while(Wait(.6))yield return true;
                ComponentMDRobot.SmartEnabled=smart;var tank=Robot("MDRobot14",new Vector3(S0.X,67.05f,S0.Z),MathF.PI/2);tank.Health.AttackResilience=1e7f;
                float y0=tank.Body.Position.Y,top=y0;int firedOut=0;
                foreach(var x in Seconds(25,()=>{Tidy();top=Math.Max(top,tank.Body.Position.Y);if(tank.Body.Position.Y>69.5f)firedOut=tank.ShotsFired;}))yield return x;
                sr[$"tank in a pit ({(smart?"0.18.0":"0.17.0")})"]=Case(tank,67,y0)+$", highest {top-y0:0.0} m, now {(tank.Body.Position.Y>69.5f?"out":"in the pit")}";
                Cam(S0+new Vector3(-10,12,-14),S0+new Vector3(4,0,0));Shot(smart?"f18_tank_after":"f18_tank_before");yield return true;
                _=firedOut;if(smart&&(tank.Body.Position.Y<69.5f||tank.WallShots>0||tank.ShotsFired<1))bad.Add("stuck: tank "+sr[$"tank in a pit (0.18.0)"]);
                Kill(tank);Remove([tower]);keep.Clear();
            }
            // a tank behind a 1-high wall (9 wide, 4 m in front), a gun tower 14 m beyond: its barrels clear the wall, so it must keep firing
            // (the check holds back only shots that would hit a block)
            foreach(var smart in new[]{false,true}) {
                Ground(sx-14,sx+26,sz-14,sz+14);Box(sx+4,sx+5,70,71,sz-4,sz+5,granite);
                var tower=Place(gunDef,S0+new Vector3(18,0,0),0);tower.Damage.AttackResilience=1e6f;while(Wait(.6))yield return true;
                ComponentMDRobot.SmartEnabled=smart;var walker=Robot("MDRobot14",new Vector3(S0.X,70.05f,S0.Z),-MathF.PI/2);walker.Health.AttackResilience=1e7f;
                int wallBefore=0;float y0=walker.Body.Position.Y;var start=walker.Body.Position;
                foreach(var x in Seconds(20,()=>{Tidy();}))yield return x;
                sr[$"tank behind a wall ({(smart?"0.18.0":"0.17.0")})"]=Case(walker,70,y0)+$", moved {Vector3.Distance(start,walker.Body.Position):0.0} m, target {(walker.Target==tower.Body?"the tower":walker.Target==null?"none":"other")}";
                Cam(S0+new Vector3(-8,9,-12),S0+new Vector3(6,0,0));Shot(smart?"f18_wall_after":"f18_wall_before");yield return true;
                if(smart&&(walker.WallShots>0||walker.ShotsFired<1))bad.Add("stuck: tank behind a wall "+sr["tank behind a wall (0.18.0)"]);
                Kill(walker);Remove([tower]);keep.Clear();_=wallBefore;
            }
            // the siege boss RD19 in a 3-deep pit (12 x 12), two gun towers 28 m off on the ground
            foreach(var smart in new[]{false,true}) {
                Ground(sx-24,sx+40,sz-24,sz+24);Box(sx-6,sx+6,67,70,sz-6,sz+6,0);
                var towers=new[]{Place(gunDef,S0+new Vector3(28,0,-4),0),Place(gunDef,S0+new Vector3(28,0,6),0)};foreach(var t in towers)t.Damage.AttackResilience=1e6f;while(Wait(.6))yield return true;
                ComponentMDRobot.SmartEnabled=smart;var boss=MDBoss.Spawn(proj,new Vector3(S0.X,67.05f,S0.Z),new Vector3(S0.X,67.05f,S0.Z),"MDBoss19");keep.Add(boss.Entity);boss.Boss.SetArmour(1e7f);
                float y0=boss.Body.Position.Y,top=y0;
                foreach(var x in Seconds(40,()=>{Tidy();top=Math.Max(top,boss.Body.Position.Y);}))yield return x;
                var siege=(MDSiegeState)boss.Boss;
                sr[$"RD19 in a pit ({(smart?"0.18.0":"0.17.0")})"]=Case(boss,67,y0)+$", highest {top-y0:0.0} m, phase {siege.Now}, main gun {siege.MainShots}, moves {siege.Moves2} (because blocked {siege.BlockedMoves}), blocks cleared {MDBoss.BlocksCleared}";
                Cam(S0+new Vector3(-14,16,-20),S0+new Vector3(6,0,0));Shot(smart?"f18_rd19_after":"f18_rd19_before");yield return true;
                if(smart&&(boss.WallShots>0||top-y0<2))bad.Add("stuck: RD19 "+sr["RD19 in a pit (0.18.0)"]);
                Kill(boss);Remove(towers);keep.Clear();
            }
            ComponentMDRobot.SmartEnabled=true;Ground(sx-24,sx+40,sz-24,sz+24);while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- spawn
        if(Part("spawn")) {
            var S0=origin+new Vector3(5,0,38);int sx=X(S0.X),sz=X(S0.Z);
            Ground(sx-20,sx+20,sz-20,sz+20);Box(sx,sx+20,70,76,sz-20,sz+20,dirt);   // a cliff 6 high at x >= sx
            for(int x=sx-14;x<sx-10;x++)for(int z=sz-3;z<sz+3;z++)terrain.ChangeCell(x,74,z,granite);   // and an overhang (a roof 4 up) west of it
            while(Wait(.5))yield return true;
            var box=MDRobots.Box("MDBoss19");var rng=new System.Random(7);var res=new Dictionary<string,string>();
            foreach(var smart in new[]{false,true}) {
                ComponentMDRobot.SmartEnabled=smart;int found=0,cramped=0;
                for(int i=0;i<200;i++) {
                    var at=MDRaids.Ground(terrain,S0+new Vector3(-6,0,0),(float)(rng.NextDouble()*Math.PI*2),4,rng,box);if(at==null)continue;found++;
                    ComponentMDRobot.SmartEnabled=true;if(!MDRaids.Room(terrain,X(at.Value.X),X(at.Value.Y-1.05f),X(at.Value.Z),box))cramped++;ComponentMDRobot.SmartEnabled=smart;
                }
                res[smart?"0.18.0":"0.17.0"]=$"{found} spots of 200 tries, {cramped} without room for RD19's body ({box})";
            }
            ComponentMDRobot.SmartEnabled=true;r["spawn"]=res;
            if(!res["0.18.0"].Contains(", 0 without"))bad.Add("spawn: "+res["0.18.0"]);
            Ground(sx-20,sx+20,sz-20,sz+20);while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- control
        if(Part("control")) {
            var cr=new Dictionary<string,object>();r["control"]=cr;
            var B0=origin+new Vector3(30,0,52);int bx=X(B0.X),bz=X(B0.Z);Ground(bx-20,bx+20,bz-16,bz+16);while(Wait(.5))yield return true;
            // the towers whose hits make the stream (far away and offline: they only lend their element and numbers)
            var defs=MDTowerLibrary.All;var src=new[]{"frost_3","battle_fog_1","tesla_3","flame_3","battle_debuff_1"}.Select(id=>defs.First(d=>d.id==id)).ToList();
            var parked=src.Select((d,i)=>{var u=Place(d,origin+new Vector3(-25+i*4,0,60),0);u.OfflineUntil=1e9;u.Damage.AttackResilience=1e9f;return u;}).ToList();
            var jet=src[0].attack;var fog=src[1].attack;
            foreach(var template in new[]{"MDDevilMech","MDBoss13"})foreach(var on in new[]{false,true}) {
                MDTowerWorld.TenacityEnabled=on;var boss=MDBoss.Spawn(proj,B0,B0+new Vector3(0,template=="MDDevilMech"?3:0,0),template);keep.Add(boss.Entity);boss.Boss.SetArmour(1e7f);
                foreach(var x in Seconds(1.5))yield return x;
                var b=boss.Body;float held=0,slowed=0,staggered=0,stood=0,sum=0,total=0,tick=0,elem=0,big=0;int k=0,reacts0=MDElements.Count,res0=MDTowerWorld.Resisted;float asked0=boss.Boss.StaggerAsked;
                var walked=0f;var last=b.Position;
                foreach(var x in Seconds(40,()=>{
                    Tidy();float dt=clock.GameTimeDelta;if(dt<=0)return;total+=dt;tick-=dt;elem-=dt;big-=dt;
                    if(tick<=0){tick=.25f;w.Slow(b,jet.slow,jet.slowTime,null);}                                   // a frost jet's tick (冰冻塔 Ⅲ)
                    if(big<=0){big=5;w.Slow(b,fog.slow,fog.slowTime,null);boss.Boss.AddPoise(MDBossState.PoiseControl);}   // a fog / liquid nitrogen shell and an EMP pulse (a boss is not frozen or stunned: poise)
                    if(elem<=0){elem=.7f;var u=parked[k++%5];MDTowerBrain.Hit(u,b,50,MDCombat.Center(b),Vector3.UnitY);}   // element hits in turn: cold, corrosive, electric, heat, corrosive (reactions)
                    float f=w.SpeedFactor(b);bool st=boss.Boss.Stagger>0;sum+=(st?0:f)*dt;
                    if(f<.75f||st)held+=dt;if(f<1)slowed+=dt;if(st)staggered+=dt;if(st||f<.3f)stood+=dt;
                    walked+=Vector2.Distance(new(b.Position.X,b.Position.Z),new(last.X,last.Z));last=b.Position;
                }))yield return x;
                var key=$"{boss.Kind.Name} ({(on?"0.18.0":"0.17.0")})";
                cr[key]=$"held {held:0.0} s of {total:0.0} s ({held/Math.Max(1,total)*100:0} %; slowed below x0.75 or staggered), slowed at all {slowed:0.0} s, staggered {staggered:0.0} s (asked {boss.Boss.StaggerAsked-asked0:0.0} s), "+
                    $"standing (x0.3 or less, or staggered) {stood:0.0} s, mean speed factor {sum/Math.Max(1,total):0.00}, reactions {MDElements.Count-reacts0}, resisted {MDTowerWorld.Resisted-res0}, moved {walked:0} m";
                if(!on){cr["_"+template]=held;}else if(held>(float)cr["_"+template]*.6f||held/Math.Max(1,total)>.4f)bad.Add("control: "+key+" "+cr[key]);
                Kill(boss);keep.Remove(boss.Entity);foreach(var x in Seconds(1))yield return x;
            }
            MDTowerWorld.TenacityEnabled=true;Remove(parked);
        }

        // ---------------------------------------------------------------- stagger: the hard kind (staggers, stun) on a boss, asked again and again
        if(Part("stagger")) {
            var hr=new Dictionary<string,object>();r["stagger"]=hr;var B0=origin+new Vector3(30,0,52);int bx=X(B0.X),bz=X(B0.Z);Ground(bx-20,bx+20,bz-16,bz+16);while(Wait(.5))yield return true;
            var share=new Dictionary<bool,float>();
            foreach(var on in new[]{false,true}) {
                MDTowerWorld.TenacityEnabled=on;var boss=MDBoss.Spawn(proj,B0,B0,"MDBoss13");keep.Add(boss.Entity);boss.Boss.SetArmour(1e7f);foreach(var x in Seconds(1))yield return x;
                float asked=0,held=0,total=0,tick=0;
                foreach(var x in Seconds(40,()=>{Tidy();float dt=clock.GameTimeDelta;if(dt<=0)return;total+=dt;tick-=dt;
                    if(tick<=0){tick=3;asked+=4;boss.Boss.Stagger=Math.Max(boss.Boss.Stagger,w.StaggerFor(boss.Body,4));}   // a 4 s stagger every 3 s (layers, off balance, a charge broken off)
                    if(boss.Boss.Stagger>0)held+=dt;}))yield return x;
                share[on]=held/Math.Max(1,total);
                hr[on?"0.18.0":"0.17.0"]=$"a 4 s stagger asked every 3 s for {total:0} s ({asked:0} s asked): staggered {held:0.0} s ({share[on]*100:0} %)";
                Kill(boss);keep.Remove(boss.Entity);foreach(var x in Seconds(1))yield return x;
            }
            MDTowerWorld.TenacityEnabled=true;
            if(share[false]<.9f||share[true]>.4f)bad.Add("stagger: "+string.Join(" / ",hr.Values));
        }

        // ---------------------------------------------------------------- missile
        if(Part("missile")) {
            var mr=new Dictionary<string,object>();r["missile"]=mr;MDMechPilot.ExternalControls=true;
            var M0=origin+new Vector3(-10,0,40);Ground(X(M0.X)-10,X(M0.X)+10,X(M0.Z)-10,X(M0.Z)+10);while(Wait(.5))yield return true;
            foreach(var type in MDMechType.All) {
                var e=DatabaseManager.CreateEntity(proj,type.Template,true);e.FindComponent<ComponentBody>(true).Position=M0;e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(e);
                var u=e.FindComponent<ComponentMDUnit>(true);var m=u.Mech;pl.ComponentBody.Position=M0+new Vector3(2.6f,.1f,0);foreach(var x in Seconds(.5))yield return x;
                MDMechPilot.Board(pl,u);foreach(var x in Seconds(.6))yield return x;
                var line=new List<string>();
                foreach(var on in new[]{false,true}) {
                    MDMechBrain.MissileCooldownEnabled=on;m.Switch=false;while(m.Weapon!=MDMechWeapon.Missile){m.Switch=true;foreach(var x in Seconds(.1))yield return x;}
                    foreach(var x in Seconds(2.2,()=>m.Trigger=false))yield return x;int n0=MDMechBrain.MissilesLaunched;var states=new HashSet<string>();
                    double tap=0;foreach(var x in Seconds(6,()=>{m.Trigger=Time.RealTime-tap<.08;if(Time.RealTime-tap>.2)tap=Time.RealTime;states.Add(MDMechBrain.Status(u).Split(' ')[0]);}))yield return x;
                    m.Trigger=false;foreach(var x in Seconds(.8))yield return x;
                    line.Add($"{(on?"0.18.0":"0.17.0")}: {MDMechBrain.MissilesLaunched-n0} missiles in 6 s of tapping, HUD [{string.Join(",",states)}]");
                }
                mr[type.Name]=string.Join(" / ",line);
                if(!line[1].Contains("冷却中")||!(int.Parse(line[1].Split(' ')[1])<=int.Parse(line[0].Split(' ')[1])/2))bad.Add("missile: "+type.Name+" "+mr[type.Name]);
                MDMechPilot.Exit(pl,u,true);foreach(var x in Seconds(.3))yield return x;proj.RemoveEntity(e,true);foreach(var x in Seconds(.3))yield return x;
            }
            MDMechBrain.MissileCooldownEnabled=true;MDMechPilot.ExternalControls=false;
        }
        MDTowerBrain.ReachEnabled=true;ComponentMDRobot.SmartEnabled=true;MDTowerWorld.TenacityEnabled=true;MDMechBrain.MissileCooldownEnabled=true;
        if(bad.Count>0)throw new Exception("fb18: "+string.Join(" || ",bad));
    }
}
