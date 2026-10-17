using Engine;
using Engine.Input;
using HarmonyLib;

namespace Game;

// Piloting Mercury: the game's mount key (ToggleMount, R by default) boards the nearest mech within reach and leaves it; with no
// mech near it mounts horses as usual. E stays the inventory. The mouse turns the body and aims, WASD walks and strafes, Shift
// dashes, Space jumps, the left button fires, B cycles rifle / missiles / cannon, F strikes, V switches the camera. Read right
// after ComponentInput turns keys into PlayerInput (the earliest update), and what the mech uses is taken out before the GUI
// (inventory, creative fly, camera mode) or the locomotion see it. The rider is hidden and set down on free ground when leaving.
public static class MDMechPilot {
    public static bool ExternalControls;   // tests drive MDMechState directly; the player's input then only boards and leaves
    public static ComponentPlayer Driver(ComponentMDUnit u)=>u.Mount?.Rider?.Entity.FindComponent<ComponentPlayer>();
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
        MDPlayer.Message(p,"已登上水星：鼠标转向瞄准，WASD 移动，Shift 冲刺，空格跳，左键开火，B 换武器，F 近战，V 视角，R 下车");return true;
    }
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
    public static void Idle(MDMechState m){m.Move=Vector2.Zero;m.Dash=m.Trigger=false;m.AimPoint=null;}
    static ComponentMDUnit Nearest(ComponentPlayer p) {
        var near=new DynamicArray<ComponentBody>();p.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.ComponentBody.Position.X,p.ComponentBody.Position.Z),5,near);
        return near.Select(b=>b.Entity.FindComponent<ComponentMDUnit>()).Where(x=>x?.Mech!=null&&CanBoard(p,x)).OrderBy(x=>Vector3.DistanceSquared(x.Body.Position,p.ComponentBody.Position)).FirstOrDefault();
    }
    public static void Input(ComponentPlayer p,float dt) {
        if(p?.ComponentRider==null)return;
        var u=Mounted(p);var gi=p.GameWidget.Input;ref var v=ref p.ComponentInput.m_playerInput;
        bool blocked=p.ComponentHealth.Health<=0||DialogsManager.HasDialogs(p.GuiWidget)||p.ComponentGui.ModalPanelWidget!=null;
        if(v.ToggleMount&&!blocked) {
            if(u!=null){Exit(p,u);v.ToggleMount=false;}
            else if(p.ComponentRider.Mount==null&&Nearest(p) is {} found){Board(p,found);v.ToggleMount=false;}
        }
        u=Mounted(p);if(u==null||ExternalControls)return;var m=u.Mech;var input=v;
        if(blocked)Idle(m);
        else {
            m.Move=new Vector2(input.Move.X,input.Move.Z);m.Dash=gi.IsKeyDown(Key.Shift);
            if(input.Jump||gi.IsKeyDownOnce(Key.Space))m.Jump=true;
            if(gi.IsKeyDownOnce(Key.F))m.Melee=true;
            if(gi.IsKeyDownOnce(Key.B)){m.Switch=true;MDPlayer.Message(p,"武器："+(((int)m.Weapon+1)%3) switch{1=>"导弹",2=>"火炮（展开后才能开火）",_=>"步枪"});}
            m.Trigger=gi.IsMouseButtonDown(MouseButton.Left)||input.Dig.HasValue||input.Hit.HasValue;
            u.AimYaw-=input.Look.X*p.ComponentLocomotion.LookSpeed*dt;
            u.AimPitch=Math.Clamp(u.AimPitch+input.Look.Y*p.ComponentLocomotion.LookSpeed*dt*(SettingsManager.FlipVerticalAxis?-1:1),-.5f,.65f);
            if(gi.IsKeyDownOnce(Key.V))p.GameWidget.ActiveCamera=p.GameWidget.ActiveCamera is FppCamera?p.GameWidget.FindCamera<TppCamera>():p.GameWidget.FindCamera<FppCamera>();
            m.AimPoint=AimPoint(p,u);
        }
        v.Move=Vector3.Zero;v.Look=Vector2.Zero;v.Dig=v.Hit=v.Aim=v.Interact=null;v.ToggleMount=false;v.ToggleCreativeFly=false;v.Jump=false;v.ToggleCrouch=false;v.SwitchCameraMode=false;
    }
    // The crosshair's point: the camera ray, past the mech itself, against terrain and other bodies.
    static Vector3 AimPoint(ComponentPlayer p,ComponentMDUnit u) {
        var c=p.GameWidget.ActiveCamera;var from=c.ViewPosition;var dir=c.ViewDirection;var to=from+dir*90;
        var t=u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(from,to,false,true,Collidable);
        var b=u.Project.FindSubsystem<SubsystemBodies>(true).Raycast(from,to,0,(x,_)=>x!=u.Body&&x.ParentBody!=u.Body&&x!=p.ComponentBody);
        return from+dir*Math.Min(t?.Distance??90,b?.Distance??90);
    }
    // the status panel follows the driver
    public static void Hud(ComponentMDUnit u) {
        var m=u.Mech;var driver=u.Destroyed?null:Driver(u);
        if(driver!=m.HudOwner){if(m.Hud?.ParentWidget is ContainerWidget c)c.Children.Remove(m.Hud);m.Hud=null;m.HudOwner=driver;if(driver!=null){m.Hud=new MDStatusWidget();driver.ComponentGui.ControlsContainerWidget.Children.Add(m.Hud);}}
        m.Hud?.Refresh(u);
    }
    public static void RemoveHud(ComponentMDUnit u){if(u.Mech?.Hud?.ParentWidget is ContainerWidget c)c.Children.Remove(u.Mech.Hud);if(u.Mech!=null){u.Mech.Hud=null;u.Mech.HudOwner=null;}}
}

public class MDStatusWidget:CanvasWidget {
    readonly LabelWidget status,weapon;readonly RectangleWidget armor,heat;
    public MDStatusWidget() {
        Size=new(320,112);HorizontalAlignment=VerticalAlignment=WidgetAlignment.Far;Margin=new(20,85);IsHitTestVisible=false;
        void Add(Widget w,float x,float y){Children.Add(w);SetWidgetPosition(w,new(x,y));}
        Add(new RectangleWidget{Size=Size,FillColor=new Color(8,20,30,190),OutlineThickness=0},0,0);
        status=new LabelWidget{Size=new(300,24),FontScale=.7f,Color=Color.White};Add(status,10,8);
        weapon=new LabelWidget{Size=new(300,24),FontScale=.7f,Color=new Color(90,220,255)};Add(weapon,10,34);
        armor=new RectangleWidget{Size=new(300,3),FillColor=new Color(90,220,255),OutlineThickness=0};Add(armor,10,62);
        heat=new RectangleWidget{Size=new(0,3),FillColor=new Color(255,150,60),OutlineThickness=0};Add(heat,10,68);
        Add(new LabelWidget{Text="R 下车  V 视角  B 武器  F 近战  Shift 冲刺  空格 跳",Size=new(300,24),FontScale=.6f,Color=Color.White},10,80);
    }
    public void Refresh(ComponentMDUnit u) {
        var m=u.Mech;status.Text=$"水星  装甲 {u.Damage.Hitpoints*100:0}%   {new Vector2(u.Body.Velocity.X,u.Body.Velocity.Z).Length()*3.6f:0} km/h";
        weapon.Text=$"{m.WeaponName}   {MDMechBrain.Status(u)}";armor.Size=new(300*Math.Clamp(u.Damage.Hitpoints,0,1),3);heat.Size=new(300*m.Heat,3);
    }
    public override void MeasureOverride(Vector2 available){LayoutTransform=Matrix.CreateScale(Math.Clamp(Math.Min(available.X/700,available.Y/450),.5f,1));base.MeasureOverride(available);}
}

[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class MDMechInputPatch {public static void Postfix(ComponentInput __instance,float dt)=>MDMechPilot.Input(__instance.m_componentPlayer,dt);}
[HarmonyPatch(typeof(ComponentRider),nameof(ComponentRider.StartMounting))]
public static class MDMechBoardGuard {public static bool Prefix(ComponentRider __instance,ComponentMount componentMount)=>componentMount.Entity.FindComponent<ComponentMDUnit>() is not {Mech:not null} u||(__instance.ComponentCreature is ComponentPlayer p&&MDMechPilot.CanBoard(p,u));}
[HarmonyPatch(typeof(ComponentRider),nameof(ComponentRider.StartDismounting))]
public static class MDMechExitGuard {public static bool Prefix(ComponentRider __instance){if(__instance.ComponentCreature is ComponentPlayer p&&MDMechPilot.Mounted(p) is {} u){MDMechPilot.Exit(p,u);return false;}return true;}}
// Cameras: first person at the head sensors, third person over the right shoulder; both look along the aim.
[HarmonyPatch(typeof(FppCamera),nameof(FppCamera.Update))]
public static class MDMechFpp {public static void Postfix(FppCamera __instance){if(MDMechPilot.Mounted(__instance.GameWidget?.PlayerData?.ComponentPlayer) is {} u){var d=MDMechBrain.AimDirection(u);__instance.SetupPerspectiveCamera(MDMechBrain.Eye(u)+d*.35f,d,Vector3.UnitY);}}}
[HarmonyPatch(typeof(TppCamera),nameof(TppCamera.Update))]
public static class MDMechTpp {
    public static void Postfix(TppCamera __instance) {
        if(MDMechPilot.Mounted(__instance.GameWidget?.PlayerData?.ComponentPlayer) is not {} u)return;
        var d=MDMechBrain.AimDirection(u);var right=Vector3.Normalize(Vector3.Cross(d,Vector3.UnitY));var focus=u.Body.Position+Vector3.UnitY*3.9f;
        var end=focus-d*10+Vector3.UnitY*1.4f+right*1.6f;
        var hit=u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(focus,end,false,true,(v,_)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        var pos=hit.HasValue?focus+Vector3.Normalize(end-focus)*Math.Max(.3f,hit.Value.Distance-.3f):end;
        __instance.SetupPerspectiveCamera(pos,d,Vector3.UnitY);
    }
}
[HarmonyPatch(typeof(ComponentModel),nameof(ComponentModel.CalculateIsVisible))]
public static class MDMechHideRider {public static void Postfix(ComponentModel __instance){if(MDMechPilot.Mounted(__instance.Entity.FindComponent<ComponentPlayer>())!=null)__instance.IsVisibleForCamera=false;}}
[HarmonyPatch(typeof(ComponentFirstPersonModel),nameof(ComponentFirstPersonModel.Draw))]
public static class MDMechHideHands {public static bool Prefix(ComponentFirstPersonModel __instance)=>MDMechPilot.Mounted(__instance.m_componentPlayer)==null;}
