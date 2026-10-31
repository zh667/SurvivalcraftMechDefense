using Engine;
using Engine.Input;
using HarmonyLib;

namespace Game;

// Piloting Mercury: the game's mount key (ToggleMount, R by default) boards the nearest mech within reach and leaves it; with no
// mech near it mounts horses as usual. E stays the inventory. The mouse turns the body and aims, WASD walks and strafes, Shift
// (held) hovers, Space jumps, the left button fires, B cycles rifle / missiles / cannon, F strikes, V switches the camera. Read right
// after ComponentInput turns keys into PlayerInput (the earliest update), and what the mech uses is taken out before the GUI
// (inventory, creative fly, camera mode) or the locomotion see it. The rider is hidden and set down on free ground when leaving.
public static class MDMechPilot {
    public static bool ExternalControls;   // tests drive MDMechState directly; the player's input then only boards and leaves
    public static ComponentPlayer Driver(ComponentMDUnit u)=>u.Mount?.Rider?.Entity.FindComponent<ComponentPlayer>();
    public static bool FirstPerson(ComponentMDUnit u)=>Driver(u)?.GameWidget.ActiveCamera is FppCamera;
    // First person: fixed to the body (not to the walking head, which bobs), 3.6 m up at the cockpit, 0.5 m in front of the
    // body's centre, smoothed. The model then draws the weapons only (ComponentMDMechModel): the rifle on its forearm at the
    // lower right (the arms aim it at the crosshair) and the cannon when it is open.
    public static Vector3 FirstPersonEye(ComponentMDUnit u,float dt) {
        var d=MDMechBrain.AimDirection(u);var flat=Vector3.Normalize(new Vector3(d.X,0,d.Z)+new Vector3(1e-4f,0,0));
        var m=u.Mech;var e=m.Def.cockpit?.firstPersonEye;var want=u.Body.Position+Vector3.UnitY*(e?.Length>2?e[1]:3.6f)+flat*(e?.Length>2?-e[2]:.5f);   // per mech (cockpit.firstPersonEye)
        m.CameraAt=m.CameraAt is {} at&&Vector3.DistanceSquared(at,want)<4?Vector3.Lerp(at,want,1-MathF.Exp(-dt*18)):want;return m.CameraAt.Value;
    }
    public static ComponentMDUnit Mounted(ComponentPlayer p)=>p?.ComponentRider?.Mount?.Entity.FindComponent<ComponentMDUnit>() is {Mech:not null} u?u:null;
    static bool Collidable(int v,float _)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v);
    public static bool Clear(SubsystemTerrain t,Vector3 foot,Vector3 size) {
        var min=foot-new Vector3(size.X*.5f,0,size.Z*.5f);var max=foot+new Vector3(size.X*.5f,size.Y,size.Z*.5f);
        for(int x=Terrain.ToCell(min.X);x<=Terrain.ToCell(max.X);x++)for(int z=Terrain.ToCell(min.Z);z<=Terrain.ToCell(max.Z);z++)for(int y=Terrain.ToCell(min.Y);y<=Terrain.ToCell(max.Y);y++) {
            int v=t.Terrain.GetCellValue(x,y,z);if(t.Terrain.GetChunkAtCell(x,z)==null||BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v))return false;
        }
        return true;
    }
    public static bool CanBoard(ComponentPlayer p,ComponentMDUnit u) {
        if(u.Mech==null||u.Destroyed||Driver(u)!=null||u.Damage.Hitpoints<=0)return false;
        var from=p.ComponentBody.Position+Vector3.UnitY;var box=u.Body.BoundingBox;var near=Vector3.Min(Vector3.Max(from,box.Min),box.Max);
        return Vector3.DistanceSquared(from,near)<9&&!u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(from,near,false,true,Collidable).HasValue;
    }
    public static bool Board(ComponentPlayer p,ComponentMDUnit u) {
        if(!CanBoard(p,u))return false;
        u.AimYaw=u.Yaw;u.AimPitch=0;p.ComponentRider.StartMounting(u.Mount);p.GameWidget.ActiveCamera=p.GameWidget.FindCamera<TppCamera>();
        MDPlayer.Message(p,MDSettings.HudKeys?$"已登上{u.Mech.Type.Name}：{KeyHints(true)}":$"已登上{u.Mech.Type.Name}");return true;
    }
    // the bound keys (MDMechKeys), for the boarding message (long) and the panel's key line (short)
    public static string KeyHints(bool full)=>full
        ?$"鼠标转向瞄准，WASD 移动，按住 {MDMechKeys.Hint(MDMechAction.Hover)} 悬浮，{MDMechKeys.Hint(MDMechAction.Jump)} 跳，{MDMechKeys.Hint(MDMechAction.Fire)} 开火，{MDMechKeys.Hint(MDMechAction.Switch)} 换武器，{MDMechKeys.Hint(MDMechAction.Ammo)} 换弹药元素，{MDMechKeys.Hint(MDMechAction.Melee)} 近战，{MDMechKeys.Hint(MDMechAction.View)} 视角，{MDMechKeys.Hint(MDMechAction.Board)} 下车"
        :$"{MDMechKeys.Hint(MDMechAction.Board)} 下车  {MDMechKeys.Hint(MDMechAction.View)} 视角  {MDMechKeys.Hint(MDMechAction.Switch)} 武器  {MDMechKeys.Hint(MDMechAction.Ammo)} 弹药  {MDMechKeys.Hint(MDMechAction.Melee)} 近战  {MDMechKeys.Hint(MDMechAction.Hover)} 悬浮  {MDMechKeys.Hint(MDMechAction.Jump)} 跳";
    public static bool Exit(ComponentPlayer p,ComponentMDUnit u,bool force=false) {
        var t=u.Project.FindSubsystem<SubsystemTerrain>(true);var bodies=u.Project.FindSubsystem<SubsystemBodies>(true);Vector3? destination=null;
        for(int radius=3;radius<=6&&destination==null;radius++)for(int i=0;i<8&&destination==null;i++) {
            var q=u.Body.Position+new Vector3(MathF.Cos(i*MathF.PI/4)*radius,2,MathF.Sin(i*MathF.PI/4)*radius);
            var ground=t.Raycast(q,q-Vector3.UnitY*5,false,true,Collidable);if(!ground.HasValue)continue;
            q.Y-=ground.Value.Distance-.08f;if(!Clear(t,q,p.ComponentBody.StanceBoxSize))continue;
            var near=new DynamicArray<ComponentBody>();bodies.FindBodiesAroundPoint(new(q.X,q.Z),2,near);
            if(near.Any(b=>b!=p.ComponentBody&&b!=u.Body&&new Vector2(b.Position.X-q.X,b.Position.Z-q.Z).LengthSquared()<2))continue;
            destination=q;
        }
        if(destination==null&&!force){MDPlayer.Message(p,"机甲周围没有安全下车位置，请移动到空地。");return false;}
        destination??=u.Body.Position+Vector3.UnitY*(u.Body.StanceBoxSize.Y+.2f);
        p.ComponentRider.m_isAnimating=false;p.ComponentBody.ParentBody=null;p.ComponentBody.Position=destination.Value;p.ComponentBody.Velocity=Vector3.Zero;
        p.GameWidget.ActiveCamera=p.GameWidget.FindCamera<FppCamera>();Idle(u.Mech);RemoveHud(u);return true;
    }
    // boarding or leaving took the mount key (or the extra board key) for the rest of the frame (MDMechKeys.Consume)
    static void Consumed(WidgetInput gi){MDMechKeys.Consume(MDMechKeys.GameKey("ToggleMount"));MDMechKeys.ConsumePressed(gi,MDMechAction.Board);}
    public static void Idle(MDMechState m){m.Move=Vector2.Zero;m.Hover=m.Trigger=false;m.AimPoint=null;}
    static ComponentMDUnit Nearest(ComponentPlayer p) {
        var near=new DynamicArray<ComponentBody>();p.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.ComponentBody.Position.X,p.ComponentBody.Position.Z),5,near);
        return near.Select(b=>b.Entity.FindComponent<ComponentMDUnit>()).Where(x=>x?.Mech!=null&&CanBoard(p,x)).OrderBy(x=>Vector3.DistanceSquared(x.Body.Position,p.ComponentBody.Position)).FirstOrDefault();
    }
    public static void Input(ComponentPlayer p,float dt) {
        if(p?.ComponentRider==null)return;
        var u=Mounted(p);var gi=p.GameWidget.Input;ref var v=ref p.ComponentInput.m_playerInput;
        bool blocked=p.ComponentHealth.Health<=0||DialogsManager.HasDialogs(p.GuiWidget)||p.ComponentGui.ModalPanelWidget!=null;
        if((v.ToggleMount||MDMechKeys.Pressed(gi,MDMechAction.Board))&&!blocked) {   // the game's mount key, or the mech's own extra key
            if(u!=null){Exit(p,u);v.ToggleMount=false;MDMechKeys.Suppress(gi,ref v,MDMechAction.Board);Consumed(gi);}
            else if(p.ComponentRider.Mount==null&&Nearest(p) is {} found){Board(p,found);v.ToggleMount=false;MDMechKeys.Suppress(gi,ref v,MDMechAction.Board);Consumed(gi);}
        }
        u=Mounted(p);if(u==null||ExternalControls)return;var m=u.Mech;var input=v;
        if(blocked)Idle(m);
        else {
            m.Move=new Vector2(input.Move.X,input.Move.Z);m.Hover=MDMechKeys.Down(gi,MDMechAction.Hover);   // bound keys (MDMechKeys)
            if(input.Jump||MDMechKeys.Pressed(gi,MDMechAction.Jump))m.Jump=true;
            if(MDMechKeys.Pressed(gi,MDMechAction.Melee))m.Melee=true;
            if(MDMechKeys.Pressed(gi,MDMechAction.Ammo)){m.NextAmmo();MDPlayer.Message(p,"弹药："+MDMechState.AmmoName(m.Ammo)+(m.Ammo==MDElement.Kinetic?"（步枪动能，火炮、导弹、榴弹爆炸）":$"（除激光外的武器都造成{MDElements.Name(m.Ammo)}伤害，命中留下“{MDElements.Statuses[(int)m.Ammo]}”）"));}   // 0.15.0
            if(MDMechKeys.Pressed(gi,MDMechAction.Switch)){m.Switch=true;var next=(MDMechWeapon)(((int)m.Weapon+1)%5);if(MDSettings.HudKeys||!MDSettings.HudShow)MDPlayer.Message(p,"武器："+MDMechState.Name(next)+(MDSettings.HudKeys?next switch{MDMechWeapon.Missile=>"（按住开火扫过目标锁定，松开发射）",MDMechWeapon.Cannon=>"（开火时展开）",MDMechWeapon.Laser=>"（照射 1 秒）",_=>""}:""));}   // the panel shows the weapon
            m.Trigger=gi.IsMouseButtonDown(MouseButton.Left)||input.Dig.HasValue||input.Hit.HasValue||MDMechKeys.Down(gi,MDMechAction.Fire);
            u.AimYaw-=input.Look.X*p.ComponentLocomotion.LookSpeed*dt;
            u.AimPitch=Math.Clamp(u.AimPitch+input.Look.Y*p.ComponentLocomotion.LookSpeed*dt*(SettingsManager.FlipVerticalAxis?-1:1),-.5f,.65f);
            if(MDMechKeys.Pressed(gi,MDMechAction.View))p.GameWidget.ActiveCamera=p.GameWidget.ActiveCamera is FppCamera?p.GameWidget.FindCamera<TppCamera>():p.GameWidget.FindCamera<FppCamera>();
            m.AimPoint=AimPoint(p,u);
            MDMechKeys.Suppress(gi,ref v,MDMechKeys.All);   // a mech key does only the mech's action (not also the game's on that key)
            MDMechKeys.ConsumePressed(gi,MDMechKeys.All);   // nor another mod's that reads the keyboard later in the frame
        }
        v.Move=Vector3.Zero;v.Look=Vector2.Zero;v.Dig=v.Hit=v.Aim=v.Interact=v.PickBlockType=null;v.ToggleMount=false;v.ToggleCreativeFly=false;v.Jump=false;v.ToggleCrouch=false;v.SwitchCameraMode=false;
    }
    // The crosshair's point: the camera ray, past the mech itself, against terrain and other bodies.
    static Vector3 AimPoint(ComponentPlayer p,ComponentMDUnit u) {
        var c=p.GameWidget.ActiveCamera;var from=c.ViewPosition;var dir=c.ViewDirection;var to=from+dir*90;u.Mech.AimFrom=from;u.Mech.AimDir=dir;
        var t=u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(from,to,false,true,Collidable);
        var b=u.Project.FindSubsystem<SubsystemBodies>(true).Raycast(from,to,0,(x,_)=>x!=u.Body&&x.ParentBody!=u.Body&&x!=p.ComponentBody);
        return from+dir*Math.Min(t?.Distance??90,b?.Distance??90);
    }
    // the status panel follows the driver; the setting "显示状态面板" hides it (checked every frame, so it applies during play)
    public static void Hud(ComponentMDUnit u) {
        var m=u.Mech;var driver=u.Destroyed||!MDSettings.HudShow?null:Driver(u);
        if(driver!=m.HudOwner){if(m.Hud?.ParentWidget is ContainerWidget c)c.Children.Remove(m.Hud);m.Hud=null;m.HudOwner=driver;if(driver!=null){m.Hud=new MDHudHost();driver.ComponentGui.ControlsContainerWidget.Children.Add(m.Hud);}}
        m.Hud?.Panel.Refresh(u);
    }
    public static void RemoveHud(ComponentMDUnit u){if(u.Mech?.Hud?.ParentWidget is ContainerWidget c)c.Children.Remove(u.Mech.Hud);if(u.Mech!=null){u.Mech.Hud=null;u.Mech.HudOwner=null;}}
}

// The mech's status panel: armour and speed, the weapon and its state, armour and energy bars, the key hints. Its place, size
// and turn come from MDSettings.Hud (MDHudPlacement), set in the mod's HUD editor (MDHudLayoutScreen; user, 2026-10-21:
// drag it on a game page, scale and turn it with fingers or the mouse); the editor shows this same widget.
public class MDStatusWidget:CanvasWidget {
    readonly LabelWidget status,weapon,keys;readonly RectangleWidget back,armor,energy;
    public const float Width=320,Height=112,ShortHeight=86;
    bool showKeys=true;
    public MDStatusWidget() {
        Size=new(Width,Height);HorizontalAlignment=VerticalAlignment=WidgetAlignment.Near;IsHitTestVisible=false;
        void Add(Widget w,float x,float y){Children.Add(w);SetWidgetPosition(w,new(x,y));}
        back=new RectangleWidget{Size=Size,FillColor=new Color(8,20,30,190),OutlineThickness=0};Add(back,0,0);
        status=new LabelWidget{Size=new(300,24),FontScale=.7f,Color=Color.White};Add(status,10,8);
        weapon=new LabelWidget{Size=new(300,24),FontScale=.7f,Color=new Color(90,220,255)};Add(weapon,10,34);
        armor=new RectangleWidget{Size=new(300,3),FillColor=new Color(90,220,255),OutlineThickness=0};Add(armor,10,62);
        energy=new RectangleWidget{Size=new(0,3),FillColor=new Color(120,170,255),OutlineThickness=0};Add(energy,10,68);
        keys=new LabelWidget{Text=MDMechPilot.KeyHints(false),Size=new(300,24),FontScale=.6f,Color=Color.White};Add(keys,10,80);
    }
    // without the key line the panel is 26 units shorter
    public bool ShowKeys{get=>showKeys;set{showKeys=value;keys.IsVisible=value;var size=new Vector2(Width,value?Height:ShortHeight);if(Size!=size){Size=size;back.Size=size;}}}
    void Show(string s,string w,float armour,float energyShare,bool locked) {
        if(status.Text!=s)status.Text=s;if(weapon.Text!=w)weapon.Text=w;
        armor.Size=new(300*Math.Clamp(armour,0,1),3);energy.Size=new(300*Math.Clamp(energyShare,0,1),3);energy.FillColor=locked?new Color(255,90,70):new Color(120,170,255);
    }
    // the speed is smoothed (about 0.3 s) and shown in whole km/h, so the number does not flicker with the physics steps
    public void Refresh(ComponentMDUnit u) {
        var m=u.Mech;float v=new Vector2(u.Body.Velocity.X,u.Body.Velocity.Z).Length()*3.6f;m.ShownSpeed+=(v-m.ShownSpeed)*Math.Min(1,Time.FrameDuration*3.5f);
        Show($"{m.Type.Name}  装甲 {u.Damage.Hitpoints*100:0}%   {MathF.Round(m.ShownSpeed/2)*2:0} km/h",$"{m.WeaponName}   {MDMechBrain.Status(u)}"+(m.Weapon==MDMechWeapon.Laser?"":"   "+MDMechState.AmmoName(m.Ammo)),u.Damage.Hitpoints,m.Energy/m.Type.EnergyMax,m.EnergyLock>0);
        if(ShowKeys!=MDSettings.HudKeys)ShowKeys=MDSettings.HudKeys;
        if(ShowKeys){var k=MDMechPilot.KeyHints(false);if(keys.Text!=k)keys.Text=k;}   // the bound keys, also right after a change on the settings page
    }
    // the editor's sample
    public void Sample()=>Show("水星  装甲 100%   0 km/h","步枪   就绪",1,.8f,false);
}

// The panel in play: a host over the whole controls area that places the panel while the layout is measured, from the area
// it is measured in, so a change of the screen size (a phone turned round, a screenshot at another size) places it right
// in the same frame.
public class MDHudHost:CanvasWidget {
    public readonly MDStatusWidget Panel=new();
    public Vector2 LastArea{get;private set;}
    public MDHudHost(){Size=new Vector2(float.PositiveInfinity);IsHitTestVisible=false;Children.Add(Panel);}
    public override void MeasureOverride(Vector2 available) {
        if(float.IsFinite(available.X)&&float.IsFinite(available.Y)&&available.X>0&&available.Y>0) {
            LastArea=available;var corner=MDSettings.Hud.Corner(available,Panel.Size,out var m);SetWidgetPosition(Panel,corner);Panel.RenderTransform=m;
        }
        base.MeasureOverride(available);
    }
    public Vector2 PanelCorner=>GetWidgetPosition(Panel)??Vector2.Zero;
}

[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class MDMechInputPatch {public static void Postfix(ComponentInput __instance,float dt)=>MDMechPilot.Input(__instance.m_componentPlayer,dt);}
[HarmonyPatch(typeof(ComponentRider),nameof(ComponentRider.StartMounting))]
public static class MDMechBoardGuard {public static bool Prefix(ComponentRider __instance,ComponentMount componentMount)=>componentMount.Entity.FindComponent<ComponentMDUnit>() is not {Mech:not null} u||(__instance.ComponentCreature is ComponentPlayer p&&MDMechPilot.CanBoard(p,u));}
[HarmonyPatch(typeof(ComponentRider),nameof(ComponentRider.StartDismounting))]
public static class MDMechExitGuard {public static bool Prefix(ComponentRider __instance){if(__instance.ComponentCreature is ComponentPlayer p&&MDMechPilot.Mounted(p) is {} u){MDMechPilot.Exit(p,u);return false;}return true;}}
// Cameras: first person at the head sensors, third person over the right shoulder; both look along the aim.
[HarmonyPatch(typeof(FppCamera),nameof(FppCamera.Update))]
public static class MDMechFpp {public static void Postfix(FppCamera __instance){if(MDMechPilot.Mounted(__instance.GameWidget?.PlayerData?.ComponentPlayer) is {} u)__instance.SetupPerspectiveCamera(MDMechPilot.FirstPersonEye(u,Time.FrameDuration),MDMechBrain.AimDirection(u),Vector3.UnitY);}}
[HarmonyPatch(typeof(TppCamera),nameof(TppCamera.Update))]
public static class MDMechTpp {
    public static void Postfix(TppCamera __instance) {
        if(MDMechPilot.Mounted(__instance.GameWidget?.PlayerData?.ComponentPlayer) is not {} u)return;
        var d=MDMechBrain.AimDirection(u);var right=Vector3.Normalize(Vector3.Cross(d,Vector3.UnitY));float k=Math.Max(1,u.Mech.Def.height/4.4f);   // a taller mech: the camera further back
        var focus=u.Body.Position+Vector3.UnitY*3.9f*k;var end=focus-d*10*k+Vector3.UnitY*1.4f*k+right*1.6f*k;
        var hit=u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(focus,end,false,true,(v,_)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        var pos=hit.HasValue?focus+Vector3.Normalize(end-focus)*Math.Max(.3f,hit.Value.Distance-.3f):end;
        __instance.SetupPerspectiveCamera(pos,d,Vector3.UnitY);
    }
}
// The pilot takes no damage while seated in a working mech (user, 2026-10-21: "在机甲里就不受伤害，不然有的时候机甲还能用，玩家先死了"):
// every injury (attacks, explosions, fire, falls, drowning, hunger) ends in ComponentHealth.Injure(Injury). A wrecked mech sets its
// pilot down first (ComponentMDUnit.MechUpdate), so from then on the player is hurt as usual.
[HarmonyPatch(typeof(ComponentHealth),nameof(ComponentHealth.Injure),new[]{typeof(Injury)})]
public static class MDMechPilotShield {
    public static int Blocked;   // tests
    public static bool Prefix(ComponentHealth __instance,Injury injury) {
        if(injury==null||injury.Amount<=0||MDMechPilot.Mounted(__instance.Entity.FindComponent<ComponentPlayer>()) is not {Destroyed:false})return true;
        Blocked++;return false;
    }
}
// 复仇女神's blast protection (the original's "对爆炸防御"; ours 20 %): robot missiles, shells and blasts and explosive tower hits.
public static class MDMechArmour {
    public static void Modify(Attackment a) {
        if(a.Target?.FindComponent<ComponentMDUnit>() is not {Mech:{Type.ExplosionTaken:<1f} m})return;
        if(a is MDRobotAttackment{Explosive:true}||a is MDTowerAttackment{Channel:"explosion"})a.AttackPower*=m.Type.ExplosionTaken;
    }
}
[HarmonyPatch(typeof(ComponentModel),nameof(ComponentModel.CalculateIsVisible))]
public static class MDMechHideRider {public static void Postfix(ComponentModel __instance){if(MDMechPilot.Mounted(__instance.Entity.FindComponent<ComponentPlayer>())!=null)__instance.IsVisibleForCamera=false;}}
[HarmonyPatch(typeof(ComponentFirstPersonModel),nameof(ComponentFirstPersonModel.Draw))]
public static class MDMechHideHands {public static bool Prefix(ComponentFirstPersonModel __instance)=>MDMechPilot.Mounted(__instance.m_componentPlayer)==null;}
