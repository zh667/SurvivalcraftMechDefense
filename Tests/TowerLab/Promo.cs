using Engine;
using Game;

namespace TowerLab;

// lab --mode promo (1.0.0 (11-14), user 2026-11-14: "专门拍一组宣传图" for the community post). Not a test: it builds scenes on
// natural ground (the world is a Continent world in this mode, Lab.Frame) and takes 1920 x 1080 screenshots promo_*.png. A
// flat-ish grass site near the spawn is levelled (radius Site m, the land round it left as it is); the scenes: the defence
// line by day with robots coming (no core there: its label and the shield's lines cover the picture), the same at sunset, the
// core's shield over the line, three mechs, two bosses, an element fight, all towers in rows.
// Robots get a huge resilience so they live through a scene; damage numbers off; towers leave vanilla creatures alone.
public partial class Lab {
    static IEnumerable<bool> Promo() {
        var r=new Dictionary<string,object>();results["promo"]=r;var shots=new List<string>();r["shots"]=shots;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var st=proj.FindSubsystem<SubsystemTerrain>(true);var T=st.Terrain;
        var gi=proj.FindSubsystem<SubsystemGameInfo>(true);
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        int grass=BlocksManager.GetBlockIndex<GrassBlock>(),dirt=BlocksManager.GetBlockIndex<DirtBlock>(),water=BlocksManager.GetBlockIndex<WaterBlock>(),snow=BlocksManager.GetBlockIndex<SnowBlock>();
        MDSettings.DamageNumbers=false;MDSettings.AttackVanilla=false;
        // ---- a site: grass, little height difference over 60 x 60 m
        const int Site=32;Vector3 S=default;int g=0;
        // the ground under plants (tall grass and flowers stand on the grass)
        int Surface(int x,int z,out int c){int h=T.GetTopHeight(x,z);c=0;for(int k=0;k<4&&h>0;k++){int v=T.GetCellValue(x,h,z);c=Terrain.ExtractContents(v);if(c==snow)return h;if(c==0||!BlocksManager.Blocks[c].IsCollidable_(v)){h--;continue;}break;}return h;}
        {   // the lab player starts at (100, 100), out at sea on a continent world: go where the game would put a new player
            var sp=st.TerrainContentsGenerator.FindCoarseSpawnPosition();r["coarseSpawn"]=sp.ToString();
            pl.ComponentBody.Position=new Vector3(sp.X,Math.Max(sp.Y,70)+20,sp.Z);pl.ComponentBody.Velocity=Vector3.Zero;
        }
        Vector3[] steps=[new(0,0,0),new(160,0,0),new(-320,0,0),new(160,0,160),new(0,0,-320)];
        for(int attempt=0;attempt<5&&S==default;attempt++) {
            if(attempt>0){var q=pl.ComponentBody.Position+steps[attempt];pl.ComponentBody.Position=new Vector3(q.X,110,q.Z);pl.ComponentBody.Velocity=Vector3.Zero;}
            foreach(var x in Seconds(8))yield return x;   // terrain round the player loads
            var p0=pl.ComponentBody.Position;int px=(int)p0.X,pz=(int)p0.Z;double best=1e9;
            {var hist=new Dictionary<string,int>();for(int x=px-120;x<=px+120;x+=16)for(int z=pz-120;z<=pz+120;z+=16){int h=Surface(x,z,out int c);var k=h<=0?"unloaded":BlocksManager.Blocks[c].GetType().Name+(h<63?"(low)":"")+(c==grass?" t"+T.GetTemperature(x,z)/3*3:"");hist[k]=hist.GetValueOrDefault(k)+1;}r["look"+attempt]=$"player {p0}: "+string.Join(", ",hist.OrderByDescending(kv=>kv.Value).Select(kv=>kv.Key+" "+kv.Value));}
            for(int cx=px-120;cx<=px+120;cx+=8)for(int cz=pz-120;cz<=pz+120;cz+=8) {
                var hs=new List<int>();int n=0,ok=0;
                for(int dx=-28;dx<=28;dx+=4)for(int dz=-28;dz<=28;dz+=4) {
                    n++;int x=cx+dx,z=cz+dz,h=Surface(x,z,out int c);if(h<63)continue;
                    if(c==grass&&T.GetTemperature(x,z)>=7){ok++;hs.Add(h);}   // warm: the grass is green (cold grass is grey-blue), no snow
                }
                if(ok<n*.7)continue;hs.Sort();int spread=hs[^1]-hs[0];if(spread>4)continue;
                double score=spread*10+(ok<n?(n-ok)*2:0)+Vector2.Distance(new(cx,cz),new(px,pz))*.02;
                if(score<best){best=score;g=hs[hs.Count/2];S=new Vector3(cx+.5f,g+1,cz+.5f);}
            }
        }
        if(S==default)throw new Exception("promo: no grass site found");
        r["site"]=S.ToString();
        // ---- level it: grass at g, dirt under, nothing above (trees in it go); the land round it stays
        int X0=(int)MathF.Floor(S.X),Z0=(int)MathF.Floor(S.Z);
        for(int x=X0-Site;x<=X0+Site;x++)for(int z=Z0-Site;z<=Z0+Site;z++) {
            float d=MathF.Sqrt((x-X0)*(x-X0)+(z-Z0)*(z-Z0));if(d>Site)continue;
            for(int y=g+1;y<g+40;y++)if(T.GetCellContents(x,y,z)!=0)st.ChangeCell(x,y,z,0);
            st.ChangeCell(x,g,z,grass);for(int y=g-4;y<g;y++){int c=T.GetCellContents(x,y,z);if(c==0||c==water)st.ChangeCell(x,y,z,dirt);}
        }
        Vector3 At(float dx,float dz){var v=S+new Vector3(dx,0,dz);v.Y=T.GetTopHeight(Terrain.ToCell(v.X),Terrain.ToCell(v.Z))+1;return v;}
        void Away(){pl.ComponentBody.Position=At(60,60)+Vector3.UnitY;pl.ComponentBody.Velocity=Vector3.Zero;}
        void Vanilla(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null&&e.FindComponent<ComponentMDRobot>()==null&&e.FindComponent<ComponentMDUnit>()==null).ToList())proj.RemoveEntity(e,true);}
        void Robots(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentMDRobot>()!=null).ToList())proj.RemoveEntity(e,true);}
        void Units(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentMDUnit>()!=null).ToList())proj.RemoveEntity(e,true);}
        ComponentMDRobot Spawn(string t,Vector3 at,float yaw=0) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);at.Y+=.05f;b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);var rb=e.FindComponent<ComponentMDRobot>(true);rb.Health.AttackResilience=1e5f;return rb;
        }
        IEnumerable<bool> Snap(string name,Vector3 eye,Vector3 focus,double settle=.6) {
            Vanilla();foreach(var x in Seconds(settle,()=>Cam(eye,focus)))yield return x;
            Cam(eye,focus);ScreenCaptureManager.Capture(1920,1080,"promo_"+name+".png");shots.Add(name);yield return true;
        }
        void Day(TimeOfDayMode m){gi.WorldSettings.TimeOfDayMode=m;}
        // the defence line: an inner ring and an outer ring (the core only in the shield scene)
        string[] inner=["gatling_3","missile_3","cannon_3","aa_3","frost_3","flame_3","tesla_3","emp_3"];
        string[] outer=["battle_turret_3","battle_sniper_1","battle_cannon_1_dg002","battle_laser_2","battle_lightning_2","battle_rocket_1","battle_shockwave_1","battle_medic_1","battle_fog_1","battle_debuff_1","radar_3","battle_trap_1"];
        void Line() {
            for(int i=0;i<inner.Length;i++){float a=i*MathF.PI*2/inner.Length;Place(MDTowerLibrary.Get(inner[i]),At(MathF.Cos(a)*7,MathF.Sin(a)*7),MathF.PI-a);}
            for(int i=0;i<outer.Length;i++){float a=(i+.5f)*MathF.PI*2/outer.Length;Place(MDTowerLibrary.Get(outer[i]),At(MathF.Cos(a)*13,MathF.Sin(a)*13),MathF.PI-a);}
        }
        void Wave(float dist) {
            string[] kinds=["MDRobot05","MDRobot05","MDRobot05","MDRobot06","MDRobot07","MDRobot05","MDRobot14","MDRobot05","MDRobot06","MDRobot14"];
            for(int i=0;i<kinds.Length;i++)Spawn(kinds[i],At(-12+i*2.6f,-dist-(i%3)*2.5f),0);
            Spawn("MDRobot09",At(-6,-dist+4)+new Vector3(0,7,0));Spawn("MDRobotEN02",At(8,-dist+2)+new Vector3(0,9,0));
        }
        MDSettings.CoreShield=false;Vanilla();Away();Day(TimeOfDayMode.Day);Line();foreach(var x in Seconds(2))yield return x;
        // ---- 1 day: the line, robots coming from -Z
        Wave(26);foreach(var x in Seconds(3.5))yield return x;
        foreach(var x in Snap("line_day",S+new Vector3(20,13,20),S+new Vector3(-2,0,-7)))yield return x;
        foreach(var x in Snap("line_low",S+new Vector3(4,2.4f,9),S+new Vector3(-2,3,-22)))yield return x;
        foreach(var x in Snap("robots",S+new Vector3(-16,4,-28),S+new Vector3(2,2,-14)))yield return x;
        // ---- 2 sunset
        Robots();Day(TimeOfDayMode.Sunset);Wave(26);foreach(var x in Seconds(3.5))yield return x;
        foreach(var x in Snap("line_sunset",S+new Vector3(-22,9,10),S+new Vector3(2,1,-8)))yield return x;
        Robots();Day(TimeOfDayMode.Day);
        // ---- 3 the core and its shield over the line, robots coming
        {
            MDSettings.CoreShield=true;var core=Place(MDTowerLibrary.Get("defense_core"),At(0,0),0);foreach(var x in Seconds(1.5))yield return x;
            Wave(28);foreach(var x in Seconds(3))yield return x;
            foreach(var x in Snap("shield",S+new Vector3(17,21,19),S+new Vector3(0,3,-5)))yield return x;
            Robots();if(core.Entity.IsAddedToProject)proj.RemoveEntity(core.Entity,true);MDSettings.CoreShield=false;foreach(var x in Seconds(1))yield return x;
        }
        // ---- 4 the three mechs, deployed from their items like a player, side by side south of the line
        {
            var inv=pl.ComponentMiner.Inventory;var block=(MDDeploymentBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()];
            int k=0;foreach(var data in new[]{1,30,31}) {
                var foot=At(-7+k*7,18);pl.ComponentBody.Position=foot+Vector3.UnitY*.1f;pl.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);pl.ComponentBody.Velocity=Vector3.Zero;
                foreach(var x in Seconds(.4))yield return x;
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(block.BlockIndex,0,data),1);inv.ActiveSlotIndex=0;
                pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,5f))));k++;
                foreach(var x in Seconds(.6))yield return x;
            }
            Away();foreach(var x in Seconds(1.5))yield return x;
            var mechs=proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u?.Mech!=null).ToList();r["mechs"]=mechs.Select(u=>$"{u.Body.Position} fwd {u.Body.Matrix.Forward}").ToList();
            if(mechs.Count>0) {
                var c=mechs.Aggregate(Vector3.Zero,(a,u)=>a+u.Body.Position)/mechs.Count;var f=mechs[0].Body.Matrix.Forward;f.Y=0;f=f.LengthSquared()>.01f?Vector3.Normalize(f):Vector3.UnitZ;
                var side=Vector3.Normalize(Vector3.Cross(f,Vector3.UnitY));
                foreach(var x in Snap("mechs",c+f*15+new Vector3(0,3,0),c+new Vector3(0,3.2f,0)))yield return x;
                foreach(var x in Snap("mechs_side",c+f*9+side*8+new Vector3(0,2,0),c+new Vector3(0,3.5f,0)))yield return x;
                foreach(var x in Snap("mechs_line",c+f*12-side*10+new Vector3(0,5,0),S+new Vector3(0,2,0)))yield return x;
            }
            foreach(var u in mechs)if(u.Entity.IsAddedToProject)proj.RemoveEntity(u.Entity,true);
        }
        // ---- 5 bosses coming at the line
        foreach(var (tpl,name) in new[]{("MDBoss19","boss_tank"),(MDRobots.Boss,"boss_demon")}) {
            Robots();var boss=MDBoss.Spawn(proj,At(3,-22),S,tpl);if(boss!=null)boss.Health.AttackResilience=1e6f;
            foreach(var x in Seconds(4.5))yield return x;
            var bp=boss?.Entity.FindComponent<ComponentBody>()?.Position??At(3,-22);
            foreach(var x in Snap(name,bp+new Vector3(-11,4,-7),bp+new Vector3(1,2.5f,3)))yield return x;
            foreach(var x in Snap(name+"_line",S+new Vector3(6,3,4),bp+new Vector3(0,3,0)))yield return x;
        }
        Robots();
        // ---- 6 elements: a few robots close to the frost, fire, lightning and fog towers
        {
            foreach(var (dx,dz) in new[]{(-2f,-11f),(1f,-12f),(3f,-10f),(-4f,-13f),(0f,-14f)})Spawn("MDRobot05",At(dx,dz),0);
            foreach(var x in Seconds(4))yield return x;
            foreach(var x in Snap("elements",S+new Vector3(9,4,-3),S+new Vector3(-1,1.5f,-12)))yield return x;
            Robots();
        }
        // ---- 7 every tower in rows
        Units();foreach(var x in Seconds(1))yield return x;
        {
            var all=MDTowerLibrary.All.Where(d=>d.attack.type!="core").ToList();int cols=8;   // the 46 towers (not the core: its light and label)
            float sx=5f,sz=6f;int rows=(all.Count+cols-1)/cols;
            for(int i=0;i<all.Count;i++){int c=i%cols,w=i/cols;Place(all[i],At((c-(cols-1)/2f)*sx,(w-(rows-1)/2f)*sz),0);}
            r["gallery"]=all.Count;foreach(var x in Seconds(2))yield return x;
            foreach(var x in Snap("gallery",S+new Vector3(0,13,29),S+new Vector3(0,0,3)))yield return x;
            foreach(var x in Snap("gallery_low",S+new Vector3(-15,3.5f,21),S+new Vector3(5,1,3)))yield return x;
        }
        Units();Robots();
    }
}
