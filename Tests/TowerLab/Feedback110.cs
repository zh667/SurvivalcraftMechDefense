using System.Xml.Linq;
using Engine;
using Engine.Input;
using Game;

namespace TowerLab;

// lab --mode fb110 (1.1.0, players' reports via the user 2026-11-20; parts fb110-<part>). Results under "fb110"; screenshots f110_*.
// furnace  a furnace of the game (two ingredient slots): two robot scraps match the alloy plate (FindSmeltingRecipe) and, with coal,
//          smelt into one; scrap + iron ingot (the old three-slot recipe's ingredients) match nothing.
// glow     at night three robots (walker, elite walker, the devil mech) glow red (ComponentModel.EmissionColor) within GlowFade s;
//          with the setting off, or by day, the glow is gone; 40 robots: frame time with the glow on and off (no draw is added).
// topdown  the top-down view: picking a tower up does not go up (setting off by default) but shows the panel; its "俯瞰" goes up
//          (MDTopDownCamera), a click at the screen centre puts the tower under it and the view comes back down; O (the key) goes up without a
//          tower, a click picks a tower up, a click puts it and the view stays, O again ends it; the key is free in the game; the pivot stays within 48 m of the
//          player; an injury, the game's camera key end it; the setting on: picking up goes up by itself, the move key's cancel ends
//          it; the zoom buttons change the height within 12-48 m; the panel's own area is "on the panel"; the camera is not in the
//          game's camera cycle.
// settings the settings page has the new rows; the panel editor edits the top-down panel (scaled, moved, saved, read back) and still
//          the mech panel.
public partial class Lab {
    static IEnumerable<bool> Feedback110() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        DialogsManager.HideAllDialogs();
        var r=new Dictionary<string,object>();results["fb110"]=r;var bad=new List<string>();results["fb110bad"]=bad;
        bool Part(string p)=>mode=="fb110"||mode=="fb110-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var gameInfo=proj.FindSubsystem<SubsystemGameInfo>(true);
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);dums.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        ComponentMDRobot Robot(string t,Vector3 at) {
            var e=DatabaseManager.CreateEntity(proj,t,true);e.FindComponent<ComponentBody>(true).Position=at;
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);var rb=e.FindComponent<ComponentMDRobot>(true);rb.ForceClip="idle";return rb;
        }
        var C0=Home+new Vector3(0,0,-6);int X0=(int)MathF.Floor(C0.X),Z0=(int)MathF.Floor(C0.Z);
        void Fresh(){foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).m_pickables)p.ToRemove=true;ClearAll();Remove(proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null));Clear(X0-34,X0+35,Z0-34,Z0+35);pl.ComponentBody.Position=C0+new Vector3(0,0,-12);pl.ComponentBody.Velocity=Vector3.Zero;}
        var inv=pl.ComponentMiner.Inventory;
        void EmptyHand(){inv.RemoveSlotItems(inv.ActiveSlotIndex,inv.GetSlotCount(inv.ActiveSlotIndex));}
        float Ground(float x,float z)=>terrain.Terrain.CalculateTopmostCellHeight(Terrain.ToCell(x),Terrain.ToCell(z))+1;
        void Capture(string name){SettingsManager.ShowGuiInScreenshots=true;ScreenCaptureManager.Capture(1280,720,name+".png");}

        // ---------------------------------------------------------------- furnace
        if(Part("furnace")) {
            var f=new Dictionary<string,object>();r["furnace"]=f;Fresh();while(Wait(.5))yield return true;
            int x=X0+3,z=Z0+3,y=(int)Ground(x,z);
            terrain.ChangeCell(x,y,z,Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<FurnaceBlock>(),0,0));
            ComponentFurnace furnace=null;
            foreach(var s in Seconds(3)){furnace=proj.FindSubsystem<SubsystemBlockEntities>(true).GetBlockEntity(x,y,z)?.Entity.FindComponent<ComponentFurnace>();if(furnace!=null)break;yield return s;}
            if(furnace==null)bad.Add("furnace: no furnace entity at the placed block");
            else {
                int scrap=MDPartBlock.Value(MDPartBlock.Kind.Scrap),alloy=MDPartBlock.Value(MDPartBlock.Kind.Alloy),iron=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<IronIngotBlock>(),0,0),coal=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<CoalChunkBlock>(),0,0);
                f["slots"]=$"{furnace.SlotsCount} slots, ingredients {furnace.m_furnaceSize}, fuel {furnace.FuelSlotIndex}, result {furnace.ResultSlotIndex}";
                void Empty(){for(int i=0;i<furnace.SlotsCount;i++)furnace.RemoveSlotItems(i,furnace.GetSlotCount(i));}
                string Match(){var rc=furnace.FindSmeltingRecipe(2);return rc==null?"none":$"{BlocksManager.Blocks[Terrain.ExtractContents(rc.ResultValue)].GetType().Name}:{Terrain.ExtractData(rc.ResultValue)} x{rc.ResultCount}";}
                Empty();furnace.AddSlotItems(0,scrap,1);furnace.AddSlotItems(1,scrap,1);
                f["twoScrap"]=Match();if(furnace.FindSmeltingRecipe(2)?.ResultValue is not int v2||Terrain.ExtractContents(v2)!=Terrain.ExtractContents(alloy)||Terrain.ExtractData(v2)!=Terrain.ExtractData(alloy))bad.Add("furnace: two scraps match "+f["twoScrap"]);
                Empty();furnace.AddSlotItems(0,scrap,1);furnace.AddSlotItems(1,iron,1);
                f["scrapIron"]=Match();if(furnace.FindSmeltingRecipe(2)!=null)bad.Add("furnace: scrap + iron ingot match "+f["scrapIron"]);
                // a real smelt: two scraps and coal
                Empty();furnace.AddSlotItems(0,scrap,1);furnace.AddSlotItems(1,scrap,1);furnace.AddSlotItems(furnace.FuelSlotIndex,coal,4);
                double t0=Time.RealTime;foreach(var s in Seconds(40)){if(furnace.GetSlotCount(furnace.ResultSlotIndex)>0)break;yield return s;}
                int got=furnace.GetSlotCount(furnace.ResultSlotIndex),gv=furnace.GetSlotValue(furnace.ResultSlotIndex);
                f["smelted"]=got>0?$"{got} x {BlocksManager.Blocks[Terrain.ExtractContents(gv)].GetType().Name}:{Terrain.ExtractData(gv)} after {Time.RealTime-t0:0.0} s, scraps left {furnace.GetSlotCount(0)}+{furnace.GetSlotCount(1)}":"nothing in 40 s";
                if(got!=1||gv!=alloy)bad.Add("furnace: smelting two scraps gave "+f["smelted"]);
                Empty();
            }
            terrain.ChangeCell(x,y,z,0);Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- glow
        if(Part("glow")) {
            var g=new Dictionary<string,object>();r["glow"]=g;Fresh();while(Wait(.5))yield return true;
            bool was=MDSettings.RobotGlow;MDSettings.RobotGlow=true;
            gameInfo.WorldSettings.TimeOfDayMode=TimeOfDayMode.Night;
            foreach(var s in Seconds(8)){if(MDWildSpawns.Night(proj))break;yield return s;}
            g["night"]=MDWildSpawns.Night(proj);if(!MDWildSpawns.Night(proj))bad.Add("glow: the lab could not make it night");
            var bots=new List<ComponentMDRobot>{Robot("MDRobot05",C0+new Vector3(-6,0,8)),Robot("MDRobot05B",C0+new Vector3(0,0,10)),Robot("MDDevilMech",C0+new Vector3(9,0,14))};
            foreach(var b in bots)b.Health.AttackResilience=1e6f;
            string Em(ComponentMDRobot b)=>b.Entity.FindComponent<ComponentModel>()?.EmissionColor is Vector4 e?$"({e.X:0.00},{e.Y:0.00},{e.Z:0.00})":"none";
            var eye=C0+new Vector3(0,6,-14);var at=C0+new Vector3(0,2,8);
            foreach(var s in Seconds(ComponentMDRobot.GlowFade+.8,()=>Cam(eye,at)))yield return s;
            g["on"]=string.Join(" ",bots.Select(Em));
            foreach(var b in bots)if(b.Entity.FindComponent<ComponentModel>()?.EmissionColor is not Vector4 e||e.X<ComponentMDRobot.GlowColor.X*.9f||e.Y>e.X*.3f)bad.Add($"glow: at night {b.Entity.ValuesDictionary.DatabaseObject.Name} emits {Em(b)}");
            Shot("f110_glow_on");yield return true;
            MDSettings.RobotGlow=false;foreach(var s in Seconds(ComponentMDRobot.GlowFade+.8,()=>Cam(eye,at)))yield return s;
            g["off"]=string.Join(" ",bots.Select(Em));
            foreach(var b in bots)if(b.Entity.FindComponent<ComponentModel>()?.EmissionColor!=null)bad.Add($"glow: setting off, {b.Entity.ValuesDictionary.DatabaseObject.Name} still emits {Em(b)}");
            Shot("f110_glow_off");yield return true;
            // perf: 40 robots, frame time with the glow on / off / on / off
            MDSettings.RobotGlow=true;
            for(int i=0;i<37;i++){var rb=Robot("MDRobot05",C0+new Vector3(-15+(i%8)*4,0,16+(i/8)*4));rb.Health.AttackResilience=1e6f;bots.Add(rb);}
            foreach(var s in Seconds(3,()=>Cam(eye+new Vector3(0,6,-6),at+new Vector3(0,0,10))))yield return s;
            var perf=new List<string>();
            foreach(bool on in new[]{true,false,true,false}) {
                MDSettings.RobotGlow=on;foreach(var s in Seconds(ComponentMDRobot.GlowFade+.5,()=>Cam(eye+new Vector3(0,6,-6),at+new Vector3(0,0,10))))yield return s;
                int frames=0;double t0=Time.RealTime;while(Time.RealTime-t0<3){Cam(eye+new Vector3(0,6,-6),at+new Vector3(0,0,10));frames++;yield return true;}
                perf.Add($"{(on?"on":"off")} {1000*(Time.RealTime-t0)/frames:0.000} ms/frame ({frames} frames)");
            }
            g["perf40"]=string.Join("; ",perf);
            // by day
            MDSettings.RobotGlow=true;gameInfo.WorldSettings.TimeOfDayMode=TimeOfDayMode.Day;
            foreach(var s in Seconds(8)){if(!MDWildSpawns.Night(proj))break;yield return s;}
            foreach(var s in Seconds(ComponentMDRobot.GlowFade+.8,()=>Cam(eye,at)))yield return s;
            g["day"]=string.Join(" ",bots.Take(3).Select(Em));
            foreach(var b in bots.Take(3))if(b.Entity.FindComponent<ComponentModel>()?.EmissionColor!=null)bad.Add($"glow: by day {b.Entity.ValuesDictionary.DatabaseObject.Name} emits {Em(b)}");
            MDSettings.RobotGlow=was;Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- topdown
        if(Part("topdown")) {
            var d=new Dictionary<string,object>();r["topdown"]=d;Fresh();while(Wait(.5))yield return true;
            bool autoWas=MDSettings.TopDownOnMove;MDSettings.TopDownOnMove=false;
            d["defaultAuto"]=MDSettingsScreen.Robots.Default().TopDownAuto;if(MDSettingsScreen.Robots.Default().TopDownAuto)bad.Add("topdown: the auto setting is on by default");
            var tk=MDMechKeys.Get(MDMechAction.TopDown);d["key"]=$"{MDMechKeys.KeyName(tk)}, shared with: '{MDMechKeys.SharedWith(tk)}'";if(tk!=Engine.Input.Key.O||MDMechKeys.SharedWith(tk).Length>0)bad.Add("topdown: the key "+d["key"]);
            var cycle=string.Join(",",ModSettingsManager.CombinedCameraManageSettings.Keys);d["cameraCycle"]=cycle;if(cycle.Contains("MDTopDown"))bad.Add("topdown: the camera is in the game's camera cycle");
            float gy=Ground(C0.X,C0.Z);var T=new Vector3(C0.X,gy,C0.Z);
            var gun=Place(MDTowerLibrary.Get("battle_turret_1"),T,0);foreach(var x in Seconds(1))yield return x;
            pl.ComponentBody.Position=T+new Vector3(0,0,-3.2f);pl.ComponentBody.Velocity=Vector3.Zero;
            // a right-click on a tower from beside it (the player stands 3.2 m in front, the camera at eye height)
            IEnumerable<bool> Interact(ComponentMDUnit u){var at=u.Body.Position+Vector3.UnitY*1.2f;var eye=u.Body.Position+new Vector3(0,2.4f,-3.4f);pl.ComponentBody.Position=u.Body.Position+new Vector3(0,0,-3.2f);pl.ComponentBody.Velocity=Vector3.Zero;foreach(var x in Hold(eye,at,3))yield return x;LabInteract.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<.4||LabInteract.Pending;){Cam(eye,at);yield return true;}foreach(var x in Seconds(.3,()=>Cam(eye,at)))yield return x;}
            // the same without the lab camera after the click (the view the click may open must stay the active camera)
            IEnumerable<bool> InteractOnce(ComponentMDUnit u){var at=u.Body.Position+Vector3.UnitY*1.2f;var eye=u.Body.Position+new Vector3(0,2.4f,-3.4f);pl.ComponentBody.Position=u.Body.Position+new Vector3(0,0,-3.2f);pl.ComponentBody.Velocity=Vector3.Zero;foreach(var x in Hold(eye,at,3))yield return x;LabInteract.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<.4||LabInteract.Pending;)yield return true;}
            IEnumerable<bool> Frames(double s){for(double s0=Time.RealTime;Time.RealTime-s0<s;)yield return true;}   // no Cam: the view's own camera
            IEnumerable<bool> ClickCentre(bool right){if(right)LabInteract.Pending=true;else LabClick.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<.5||LabInteract.Pending||LabClick.Pending;)yield return true;}
            IEnumerable<bool> Press(Key k){LabKeys.Taps.Enqueue(k);for(double s0=Time.RealTime;Time.RealTime-s0<.4||LabKeys.Busy;)yield return true;}
            string Cam0()=>pl.GameWidget.ActiveCamera?.GetType().Name;
            MDTopDownCamera Td()=>MDTopDown.Camera(pl);
            float Flat(Vector3 a,Vector3 b)=>new Vector2(a.X-b.X,a.Z-b.Z).Length();
            d["cameraAdded"]=Td()!=null;if(Td()==null){bad.Add("topdown: the camera is not in the game view (ManageCameras)");goto done;}
            // 1 pick up: no view, the panel
            EmptyHand();foreach(var x in Interact(gun))yield return x;
            d["picked"]=$"moving {(MDTowerTools.Moving==gun?"yes":"no")}, view {MDTopDown.Active}, camera {Cam0()}, panel {(MDTopDownHost.Current?.ParentWidget!=null?MDTopDownHost.Current.Panel.Toggle.Text:"none")}";
            if(MDTowerTools.Moving!=gun)bad.Add("topdown: the tower was not picked up: "+d["picked"]);
            if(MDTopDown.Active)bad.Add("topdown: picking up went up with the setting off");
            if(MDTopDownHost.Current?.ParentWidget==null)bad.Add("topdown: no panel while a tower is picked up");
            Capture("f110_panel_picked");yield return true;
            // 2 the panel's button: up; a click at the centre puts it there; down again
            MDTopDownHost.LabPress="toggle";foreach(var x in Frames(.6))yield return x;
            d["up"]=$"view {MDTopDown.Active} auto {MDTopDown.Auto}, camera {Cam0()}, panel {MDTopDownHost.Current?.Panel.Toggle.Text}, height {Td()?.Height:0}";
            if(!MDTopDown.On(pl)||!MDTopDown.Auto)bad.Add("topdown: the panel's button did not go up for the move: "+d["up"]);
            var spot=T+new Vector3(9,0,6);Td().Pivot=new Vector3(spot.X,Ground(spot.X,spot.Z),spot.Z);foreach(var x in Frames(.6))yield return x;
            Capture("f110_topdown_moving");yield return true;
            var c=Td();var centreGround=c.Pivot;int moves=MDTowerTools.Moves;
            foreach(var x in ClickCentre(false))yield return x;foreach(var x in Frames(.4))yield return x;
            d["put"]=$"moves {MDTowerTools.Moves-moves}, tower at {gun.Body.Position}, pivot {centreGround}, off {Flat(gun.Body.Position,centreGround):0.0} m, view {MDTopDown.Active} ({MDTopDown.LastExit}), camera {Cam0()} ({MDTowerTools.LastText})";
            if(MDTowerTools.Moves-moves!=1||Flat(gun.Body.Position,centreGround)>2.5f)bad.Add("topdown: the click did not put the tower under the centre: "+d["put"]);
            if(MDTopDown.Active||pl.GameWidget.ActiveCamera is MDTopDownCamera)bad.Add("topdown: the view stayed after the move it was entered for");
            // 3 G: free view, a click picks the tower, a click puts it, the view stays, G ends it
            pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();foreach(var x in Frames(.3))yield return x;
            foreach(var x in Press(Engine.Input.Key.O))yield return x;
            d["free"]=$"view {MDTopDown.Active} auto {MDTopDown.Auto} camera {Cam0()}";
            if(!MDTopDown.On(pl)||MDTopDown.Auto)bad.Add("topdown: G did not go up: "+d["free"]);
            else {
                Td().Pivot=gun.Body.Position;foreach(var x in Frames(.6))yield return x;
                foreach(var x in ClickCentre(true))yield return x;foreach(var x in Frames(.3))yield return x;
                d["freePick"]=$"moving {(MDTowerTools.Moving==gun?"yes":"no")}, view {MDTopDown.Active}";
                if(MDTowerTools.Moving!=gun)bad.Add("topdown: a click on the tower from the free view did not pick it up");
                else {
                    var spot2=T+new Vector3(-7,0,10);Td().Pivot=new Vector3(spot2.X,Ground(spot2.X,spot2.Z),spot2.Z);foreach(var x in Frames(2.2))yield return x;   // past the pick guard
                    var p2=Td().Pivot;moves=MDTowerTools.Moves;foreach(var x in ClickCentre(false))yield return x;foreach(var x in Frames(.4))yield return x;
                    d["freePut"]=$"moves {MDTowerTools.Moves-moves}, off {Flat(gun.Body.Position,p2):0.0} m, view {MDTopDown.Active}";
                    if(MDTowerTools.Moves-moves!=1||Flat(gun.Body.Position,p2)>2.5f)bad.Add("topdown: the free view's click did not put the tower: "+d["freePut"]);
                    if(!MDTopDown.On(pl))bad.Add("topdown: the free view ended with the move");
                }
                // 4 the pivot stays within 48 m
                Td().Pivot=pl.ComponentBody.Position+new Vector3(100,0,0);foreach(var x in Frames(.3))yield return x;
                d["clamp"]=$"{Flat(Td().Pivot,pl.ComponentBody.Position):0.0} m";if(Flat(Td().Pivot,pl.ComponentBody.Position)>MDTopDown.MaxOffset+.1f)bad.Add("topdown: the pivot went "+d["clamp"]);
                Td().Pivot=T;
                // 5 zoom
                float h0=Td().Height;MDTopDownHost.LabPress="farther";foreach(var x in Frames(.2))yield return x;float h1=Td().Height;
                for(int i=0;i<12;i++){MDTopDownHost.LabPress="farther";yield return true;yield return true;}float hMax=Td().Height;
                for(int i=0;i<16;i++){MDTopDownHost.LabPress="closer";yield return true;yield return true;}float hMin=Td().Height;
                d["zoom"]=$"{h0:0.0} -> {h1:0.0}, max {hMax:0.0}, min {hMin:0.0}";
                if(!(h1>h0)||MathF.Abs(hMax-MDTopDown.MaxHeight)>.01f||MathF.Abs(hMin-MDTopDown.MinHeight)>.01f)bad.Add("topdown: zoom "+d["zoom"]);
                Td().Height=MDTopDown.DefaultHeight;foreach(var x in Frames(.4))yield return x;
                // 6 the panel's area
                var panel=MDTopDownHost.Current?.Panel;
                if(panel!=null){var mid=panel.WidgetToScreen(panel.ActualSize/2);d["panelHit"]=$"centre {MDTopDownHost.Covers(mid)}, screen corner {MDTopDownHost.Covers(new Vector2(4,4))}";if(!MDTopDownHost.Covers(mid)||MDTopDownHost.Covers(new Vector2(4,4)))bad.Add("topdown: panel area "+d["panelHit"]);}
                else bad.Add("topdown: no panel in the free view");
                Capture("f110_topdown_free");yield return true;
                foreach(var x in Press(Engine.Input.Key.O))yield return x;
                d["freeEnd"]=$"view {MDTopDown.Active} ({MDTopDown.LastExit}) camera {Cam0()}";if(MDTopDown.Active)bad.Add("topdown: G again did not end it");
            }
            // 7 an injury ends it
            foreach(var x in Press(Engine.Input.Key.O))yield return x;
            float hp=pl.ComponentHealth.Health;pl.ComponentHealth.Health=Math.Max(.2f,hp-.1f);foreach(var x in Frames(.3))yield return x;
            d["hurt"]=$"view {MDTopDown.Active} ({MDTopDown.LastExit}), camera {Cam0()}";if(MDTopDown.Active||MDTopDown.LastExit!="hurt")bad.Add("topdown: an injury did not end it: "+d["hurt"]);
            pl.ComponentHealth.Health=hp;
            // 8 the game's camera key (another camera made active) ends it
            foreach(var x in Press(Engine.Input.Key.O))yield return x;
            pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<TppCamera>();foreach(var x in Frames(.3))yield return x;
            d["otherCamera"]=$"view {MDTopDown.Active} ({MDTopDown.LastExit}), camera {Cam0()}";if(MDTopDown.Active||pl.GameWidget.ActiveCamera is not TppCamera)bad.Add("topdown: another camera did not end it: "+d["otherCamera"]);
            pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();foreach(var x in Frames(.3))yield return x;
            // 9 the setting on: picking up goes up, the move key cancels and comes down
            MDSettings.TopDownOnMove=true;EmptyHand();
            foreach(var x in InteractOnce(gun))yield return x;foreach(var x in Frames(.4))yield return x;
            d["autoPick"]=$"moving {(MDTowerTools.Moving==gun?"yes":"no")}, view {MDTopDown.Active} auto {MDTopDown.Auto}, camera {Cam0()}";
            if(MDTowerTools.Moving!=gun||!MDTopDown.On(pl)||!MDTopDown.Auto)bad.Add("topdown: with the setting on, picking up did not go up: "+d["autoPick"]);
            foreach(var x in Press(MDMechKeys.Get(MDMechAction.MoveTower)))yield return x;
            d["autoCancel"]=$"moving {(MDTowerTools.Moving==null?"no":"yes")}, view {MDTopDown.Active} ({MDTopDown.LastExit})";
            if(MDTowerTools.Moving!=null||MDTopDown.Active)bad.Add("topdown: cancelling did not end the view: "+d["autoCancel"]);
            done:
            if(MDTowerTools.Moving!=null)MDTowerTools.Stop(pl,"");if(MDTopDown.Active)MDTopDown.Exit(pl,"lab");
            MDSettings.TopDownOnMove=autoWas;SettingsManager.ShowGuiInScreenshots=false;Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- settings
        if(Part("settings")) {
            var s=new Dictionary<string,object>();r["settings"]=s;
            string Texts(Widget w)=>w is ContainerWidget cw?string.Join(" | ",cw.AllChildren.Select(c=>c switch{LabelWidget l=>l.Text,BevelledButtonWidget b=>"["+b.Text+"]",_=>null}).Where(x=>!string.IsNullOrWhiteSpace(x))):w?.GetType().Name;
            var saved=MDSettings.TopDownHud.Copy();bool shown=MDSettings.TopDownHudShow;var mech=MDSettings.Hud.Copy();
            MDSettingsScreen.Open();foreach(var x in Seconds(1))yield return x;
            if(ScreensManager.CurrentScreen is MDSettingsScreen page) {
                var content=typeof(MDSettingsScreen).GetField("content",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.GetValue(page) as ContainerWidget;
                var all=content==null?"":Texts(content);
                foreach(var want in new[]{"机兵夜间发红光","拿起炮塔时自动俯瞰","调整俯瞰面板","俯瞰视角（开／关）","俯瞰面板　显示"})if(!all.Contains(want))bad.Add("settings: no row "+want);
                s["rows"]=string.Join(" / ",new[]{"机兵夜间发红光","拿起炮塔时自动俯瞰","调整俯瞰面板","俯瞰视角（开／关）"}.Select(w=>w+(all.Contains(w)?" ok":" MISSING")));
                Capture("f110_settings");yield return true;
                page.OpenEditor(MDHudTarget.TopDown);foreach(var x in Seconds(1))yield return x;
                if(ScreensManager.CurrentScreen is MDHudLayoutScreen ed) {
                    s["editor"]=$"editing {ed.Editing}, top-down sample shown {ed.TopDownSample.IsVisible}, mech sample shown {ed.Sample.IsVisible}";
                    if(ed.Editing!=MDHudTarget.TopDown||!ed.TopDownSample.IsVisible||ed.Sample.IsVisible)bad.Add("settings: editor "+s["editor"]);
                    Capture("f110_editor_default");yield return true;
                    ed.MoveTo(new Vector2(.3f,.35f));ed.Scale(1.21f);ed.Turn(MathF.PI/12);foreach(var x in Seconds(.5))yield return x;
                    Capture("f110_editor_moved");yield return true;
                    ed.SaveNow();foreach(var x in Seconds(.5))yield return x;
                    var t=MDSettings.TopDownHud;s["saved"]=$"custom {t.Custom} x {t.X:0.00} y {t.Y:0.00} scale {t.Scale:0.00} turn {t.Rotation*180/MathF.PI:0}";
                    if(!t.Custom||MathF.Abs(t.X-.3f)>.01f||MathF.Abs(t.Scale-1.21f)>.01f)bad.Add("settings: top-down panel saved as "+s["saved"]);
                    if(MDSettings.Hud.Custom!=mech.Custom||MathF.Abs(MDSettings.Hud.X-mech.X)>1e-4f)bad.Add("settings: saving the top-down panel changed the mech panel");
                    var xe=new XElement("Mod");MDSettings.Save(xe);MDSettings.Load(xe);var b=MDSettings.TopDownHud;
                    s["readBack"]=$"custom {b.Custom} x {b.X:0.00} y {b.Y:0.00} scale {b.Scale:0.00}";if(!b.Custom||MathF.Abs(b.X-.3f)>.01f||MathF.Abs(b.Scale-1.21f)>.01f)bad.Add("settings: read back "+s["readBack"]);
                } else bad.Add("settings: the editor did not open: "+ScreensManager.CurrentScreen?.GetType().Name);
                MDSettingsScreen.Open();foreach(var x in Seconds(.6))yield return x;
                if(ScreensManager.CurrentScreen is MDSettingsScreen page2){page2.OpenEditor();foreach(var x in Seconds(.8))yield return x;
                    if(ScreensManager.CurrentScreen is MDHudLayoutScreen ed2){s["mechEditor"]=$"editing {ed2.Editing}, mech sample shown {ed2.Sample.IsVisible}";if(ed2.Editing!=MDHudTarget.Mech||!ed2.Sample.IsVisible||ed2.TopDownSample.IsVisible)bad.Add("settings: mech editor "+s["mechEditor"]);Capture("f110_editor_mech");yield return true;}}
                MDSettings.TopDownHud=saved;MDSettings.TopDownHudShow=shown;MDSettings.Hud=mech;
                ScreensManager.SwitchScreen("Game");foreach(var x in Seconds(1))yield return x;
            } else bad.Add("settings: page not open: "+ScreensManager.CurrentScreen?.GetType().Name);
            SettingsManager.ShowGuiInScreenshots=false;
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb110: "+string.Join(" || ",bad));
    }
}
