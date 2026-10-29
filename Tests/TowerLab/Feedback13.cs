using Engine;
using Game;
using GameEntitySystem;
using System.Reflection;
using TemplatesDatabase;

namespace TowerLab;

// 0.13.0 (players via the user, 2026-10-29). lab --mode fb13: medic towers do not heal medic towers; tower placement by real
// clicks (on grass, on a block's side, beside a wall the old south-west-corner rule ran into, flowers cleared, a crop kept); the
// particle amount setting. lab --mode faction-mods (API 1.9.4 with the user's gun mod and its recipe mod): creatures of the
// herd "Terrorist" are targets, "Neutral" and "Guerrillas" are not, a gun tower fires at a terrorist, and an attack target object
// aimed at the player makes a creature hostile while a follow target does not. Results under "fb13"; screenshots f13_*.
public partial class Lab {
    static IEnumerable<bool> Feedback13() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb13"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;var bad=new List<string>();
        var terrain=proj.FindSubsystem<SubsystemTerrain>(true);var time=proj.FindSubsystem<SubsystemTime>(true);int y0=(int)MathF.Floor(Home.Y);
        var defs=MDTowerLibrary.All;var gun=defs.First(t=>t.data==0);
        List<ComponentMDUnit> Units()=>proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null).ToList();
        if(mode=="fb13") {
            // ---- medic towers: two medics and a gun tower at half health; after two heal rounds only the gun tower is healed
            {
                var m1=Place(defs.First(t=>t.data==19),Home+new Vector3(0,0,-30),0);var m2=Place(defs.First(t=>t.data==19),Home+new Vector3(4,0,-30),0);var g=Place(gun,Home+new Vector3(2,0,-35),0);
                g.Cooldown=1e6f;foreach(var u in new[]{m1,m2,g})u.SetHitpoints(.5f);pl.ComponentBody.Position=Home+new Vector3(30,0,30);
                foreach(var x in During(12,()=>{}))yield return x;
                r["medic"]=$"after 12 s: medic {m1.Damage.Hitpoints:0.00} / {m2.Damage.Hitpoints:0.00}, gun tower {g.Damage.Hitpoints:0.00}";
                if(m1.Damage.Hitpoints>.51f||m2.Damage.Hitpoints>.51f||g.Damage.Hitpoints<.85f)bad.Add("medic: "+r["medic"]);
                Remove([m1,m2,g]);while(Wait(.5))yield return true;
            }
            // ---- placement by real clicks (MDDeployPatch -> MDTowerPlacement.Find)
            {
                var pr=new Dictionary<string,object>();r["placement"]=pr;var inv=pl.ComponentMiner.Inventory;
                int Value<T>() where T:Block=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<T>());
                int stone=Value<GraniteBlock>(),grass=Value<TallGrassBlock>(),sapling=Value<SaplingBlock>(),flower=Terrain.MakeBlockValue(BlocksManager.Blocks.First(b=>b is FlowerBlock).BlockIndex);
                void Set(int x,int y,int z,int v)=>terrain.ChangeCell(x,y,z,v);
                void Area(int cx,int cz){for(int x=cx-6;x<=cx+6;x++)for(int z=cz-6;z<=cz+6;z++)for(int y=y0;y<y0+8;y++)Set(x,y,z,0);}
                (ComponentMDUnit unit,string why) Click(Vector3 stand,Vector3 at) {
                    var before=Units().ToHashSet();pl.ComponentBody.Position=stand;pl.ComponentBody.Velocity=Vector3.Zero;
                    inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,MDDeploymentBlock.Value(gun,1),1);inv.ActiveSlotIndex=0;
                    var eye=stand+new Vector3(0,1.6f,0);pl.ComponentMiner.Use(new Ray3(eye,Vector3.Normalize(at-eye)));
                    var made=Units().FirstOrDefault(u=>!before.Contains(u));string why="";
                    if(made==null) {   // what the click hit and why nothing fits there
                        var h=pl.ComponentMiner.Raycast<TerrainRaycastResult>(new Ray3(eye,Vector3.Normalize(at-eye)),RaycastMode.Digging);
                        if(h==null)why="the ray hit nothing";
                        else{MDTowerPlacement.Find(terrain,proj.FindSubsystem<SubsystemBodies>(true),h.Value,gun,out _,out var reason);why=$"hit cell {h.Value.CellFace.X},{h.Value.CellFace.Y},{h.Value.CellFace.Z} face {h.Value.CellFace.Face} block {BlocksManager.Blocks[Terrain.ExtractContents(h.Value.Value)].GetType().Name}: {reason}";}
                    }
                    return (made,why);
                }
                string Foot(ComponentMDUnit u){var s=MDTowerPlacement.Cells(u.Tower);int x0=Terrain.ToCell(u.Body.Position.X-s.X*.5f),z0=Terrain.ToCell(u.Body.Position.Z-s.Z*.5f);return $"x {x0}..{x0+s.X-1}, z {z0}..{z0+s.Z-1}, y {Terrain.ToCell(u.Body.Position.Y+.001f)}";}
                bool Has(int x,int y,int z,int v)=>Terrain.ExtractContents(terrain.Terrain.GetCellValue(x,y,z))==Terrain.ExtractContents(v);
                // A: grass and a flower on the ground, the click lands on the grass
                {
                    int cx=(int)MathF.Floor(Home.X)-20,cz=(int)MathF.Floor(Home.Z)-20;Area(cx,cz);
                    for(int x=cx-3;x<=cx+3;x++)for(int z=cz-3;z<=cz+3;z++)Set(x,y0,z,(x+z)%3==0?flower:grass);
                    while(Wait(.3))yield return true;var (u,why)=Click(new Vector3(cx+.5f,y0,cz-4.5f),new Vector3(cx+.5f,y0+.4f,cz+.5f));
                    int left=0;if(u!=null){var s=MDTowerPlacement.Cells(gun);int x0=Terrain.ToCell(u.Body.Position.X-s.X*.5f),z0=Terrain.ToCell(u.Body.Position.Z-s.Z*.5f);for(int x=x0;x<x0+s.X;x++)for(int z=z0;z<z0+s.Z;z++)if(!Has(x,y0,z,0))left++;}
                    pr["grass"]=u==null?"not placed: "+why:$"placed at {Foot(u)} (ground y {y0}); plants left in the footprint {left}";
                    if(u==null||Terrain.ToCell(u.Body.Position.Y+.001f)!=y0||left>0)bad.Add("placement/grass: "+pr["grass"]);
                    Cam(new Vector3(cx+6,y0+5,cz-8),new Vector3(cx,y0+1,cz));Shot("f13_place_grass");yield return true;if(u!=null)Remove([u]);Area(cx,cz);
                }
                // B: a click on the side of a single stone block
                {
                    int cx=(int)MathF.Floor(Home.X),cz=(int)MathF.Floor(Home.Z)-20;Area(cx,cz);Set(cx,y0,cz,stone);
                    while(Wait(.3))yield return true;var (u,why)=Click(new Vector3(cx+.5f,y0,cz-4.5f),new Vector3(cx+.5f,y0+.5f,cz));
                    bool overlap=u!=null&&Foot(u) is var f&&u.Body.BoundingBox.Intersection(new BoundingBox(new Vector3(cx+.01f,y0+.01f,cz+.01f),new Vector3(cx+.99f,y0+.99f,cz+.99f)));
                    pr["side"]=u==null?"not placed: "+why:$"placed at {Foot(u)}; stone at x {cx}, z {cz}; overlaps the stone {overlap}";
                    if(u==null||overlap||!Has(cx,y0,cz,stone))bad.Add("placement/side: "+pr["side"]);
                    if(u!=null)Remove([u]);Area(cx,cz);
                }
                // C: beside a wall on the +X side: the old rule (clicked cell = south-west corner) ran into the wall
                {
                    int cx=(int)MathF.Floor(Home.X)+20,cz=(int)MathF.Floor(Home.Z)-20;Area(cx,cz);
                    for(int z=cz-4;z<=cz+4;z++)for(int y=y0;y<y0+4;y++)Set(cx+1,y,z,stone);
                    while(Wait(.3))yield return true;var (u,why)=Click(new Vector3(cx-4.5f,y0,cz+.5f),new Vector3(cx+.5f,y0,cz+.5f));
                    pr["wall"]=u==null?"not placed: "+why:$"placed at {Foot(u)}; wall at x {cx+1}";
                    if(u==null||u.Body.BoundingBox.Max.X>cx+1.01f)bad.Add("placement/wall: "+pr["wall"]);
                    Cam(new Vector3(cx-6,y0+6,cz-8),new Vector3(cx,y0+1,cz));Shot("f13_place_wall");yield return true;if(u!=null)Remove([u]);Area(cx,cz);
                }
                // D: a sapling (or crop) beside the clicked cell stays, the footprint goes round it
                {
                    int cx=(int)MathF.Floor(Home.X)-20,cz=(int)MathF.Floor(Home.Z)-36;Area(cx,cz);Set(cx+1,y0,cz,sapling);
                    for(double s0=Time.RealTime;Time.RealTime-s0<1.5;){Cam(new Vector3(cx+6,y0+6,cz-10),new Vector3(cx,y0,cz));yield return true;}   // terrain loads around the camera
                    var (u,why)=Click(new Vector3(cx+.5f,y0,cz-4.5f),new Vector3(cx+.5f,y0,cz+.5f));
                    pr["crop"]=u==null?"not placed: "+why:$"placed at {Foot(u)}; sapling at x {cx+1} still there {Has(cx+1,y0,cz,sapling)}";
                    if(u==null||!Has(cx+1,y0,cz,sapling))bad.Add("placement/crop: "+pr["crop"]);
                    if(u!=null)Remove([u]);Area(cx,cz);
                }
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));
            }
            // ---- particle amount: the same ten explosions at each level
            {
                var w=MDTowerWorld.For(proj);var counts=new int[3];
                for(int lv=0;lv<3;lv++) {
                    MDSettings.FxLevel=lv;foreach(var x in During(4,()=>{}))yield return x;   // the last level's particles die out
                    int p0=w.Particles;for(int i=0;i<10;i++)w.Explosion(Home+new Vector3(i*3-15,0,-50),3,i%2==0?"heavy":"fire",new Color(255,200,120),new Color(255,120,40));
                    counts[lv]=w.Particles-p0;
                }
                MDSettings.Defaults();r["fx"]=$"particles from ten explosions: 完整 {counts[0]}, 精简 {counts[1]}, 极简 {counts[2]}";
                if(!(counts[1]<counts[0]*.8f&&counts[2]<counts[0]*.5f&&counts[2]>0))bad.Add("fx: "+r["fx"]);
            }
        }
        if(mode=="faction-mods") {
            // creatures of the other mods, picked from the database by their herd name (no template names written here)
            var byHerd=new Dictionary<string,List<string>>();
            foreach(var vd in DatabaseManager.EntitiesValuesDictionaries) {
                if(DatabaseManager.FindValuesDictionaryForComponent(vd,typeof(ComponentCreature))==null)continue;
                string herd=null;
                foreach(var kv in vd)if(kv.Value is ValuesDictionary cv&&cv.ContainsKey("HerdName")&&cv.GetValue<object>("HerdName") is string h&&h.Length>0)herd=h;
                if(herd is "Terrorist" or "Neutral" or "Guerrillas"){if(!byHerd.TryGetValue(herd,out var l))byHerd[herd]=l=[];l.Add(vd.DatabaseObject.Name);}
            }
            r["herds"]=string.Join("; ",byHerd.Select(kv=>$"{kv.Key} {kv.Value.Count} templates"));
            if(!byHerd.ContainsKey("Terrorist"))throw new Exception("faction-mods: the gun mod's terrorists are not in the database: "+r["herds"]);
            var spawned=new List<(string herd,ComponentCreature c)>();int i=0;
            foreach(var (herd,names) in byHerd)foreach(var name in names.Where(n=>!n.Contains("Pilot")).Take(3)) {
                Entity e;try{e=DatabaseManager.CreateEntity(proj,name,true);}catch{continue;}
                var b=e.FindComponent<ComponentBody>();if(b==null)continue;b.Position=Home+new Vector3(-12+i%6*5,0,-24-i/6*5);i++;
                if(e.FindComponent<ComponentSpawn>() is {} sp)sp.SpawnDuration=0;proj.AddEntity(e);spawned.Add((herd,e.FindComponent<ComponentCreature>(true)));
            }
            pl.ComponentBody.Position=Home+new Vector3(0,0,30);while(Wait(2))yield return true;
            var verdicts=spawned.Select(s=>(s.herd,hostile:MDFaction.HostileNow(s.c,time.GameTime))).ToList();
            r["verdicts"]=string.Join("; ",verdicts.GroupBy(v=>v.herd).Select(g=>$"{g.Key}: {g.Count(v=>v.hostile)}/{g.Count()} hostile"));
            if(verdicts.Any(v=>v.herd=="Terrorist"&&!v.hostile)||verdicts.Any(v=>v.herd=="Neutral"&&v.hostile))bad.Add("faction: "+r["verdicts"]);
            // an attack target object aimed at the player makes a neutral creature hostile; following the player does not
            var neutral=spawned.FirstOrDefault(s=>s.herd=="Neutral").c;
            if(neutral!=null) {
                object holder=null;PropertyInfo prop=null;
                foreach(var comp in neutral.Entity.Components){prop=comp.GetType().GetProperty("Target1");if(prop!=null&&prop.CanWrite){holder=comp;break;}}
                if(holder!=null) {
                    var tt=prop.PropertyType;var purposeType=tt.GetField("Purpose")?.FieldType;
                    object Make(string purpose)=>Activator.CreateInstance(tt,Enum.Parse(purposeType,purpose),pl.ComponentBody);
                    var keep=prop.GetValue(holder);
                    prop.SetValue(holder,Make("Follow"));bool follow=MDFaction.HostileNow(neutral,time.GameTime);
                    prop.SetValue(holder,Make("Attack"));bool attack=MDFaction.HostileNow(neutral,time.GameTime);
                    prop.SetValue(holder,keep);
                    r["attackTarget"]=$"a neutral creature following the player hostile {follow}, attacking the player hostile {attack}";
                    if(follow||!attack)bad.Add("attack target: "+r["attackTarget"]);
                } else r["attackTarget"]="no writable Target1 on the neutral creature (not checked)";
            }
            // a gun tower beside a terrorist fires at it
            var terrorist=spawned.First(s=>s.herd=="Terrorist").c;
            var tw=Place(gun,terrorist.ComponentBody.Position+new Vector3(9,0,0),0);float h0=terrorist.ComponentHealth.Health;double t0=Time.RealTime,targeted=-1;int s0=tw.ShotCount;
            foreach(var x in During(10,()=>{if(targeted<0&&tw.Target==terrorist.ComponentBody)targeted=Time.RealTime-t0;}))yield return x;
            Cam(tw.Body.Position+new Vector3(4,5,-10),terrorist.ComponentBody.Position);Shot("f13_terrorist");yield return true;
            r["tower"]=$"gun tower targets the terrorist after {targeted:0.0} s, shots {tw.ShotCount-s0}, terrorist health {h0:0.00} -> {(terrorist.Entity.IsAddedToProject?terrorist.ComponentHealth.Health:0):0.00}";
            if(targeted<0||tw.ShotCount<=s0)bad.Add("tower vs terrorist: "+r["tower"]);
            Remove([tw]);foreach(var (_,c) in spawned)if(c.Entity.IsAddedToProject)proj.RemoveEntity(c.Entity,true);
        }
        if(bad.Count>0)throw new Exception("fb13: "+string.Join(" | ",bad));
    }
}
