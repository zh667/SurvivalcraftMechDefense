using Engine;
using Engine.Input;

namespace Game;

// 1.1.0 the top-down view for placing and moving towers (players via the user, 2026-11-20: "点击炮台后进入俯瞰视角，方便布置炮台和移动
// 位置（或者给个开俯瞰视角的按键，并且模组设置可设置）"). The user's choices (11-20): picking a tower up does not go up by itself (a
// setting, off by default: small moves, stray pick-ups and towers moved in a fight would keep throwing the camera up); a panel
// with a "俯瞰" button shows while a tower is picked up or the view is on, placed, sized and turned in the settings like the mech
// panel; a key (MDMechAction.TopDown, O; the plan said G, but G is the game's 编辑物品) turns the view on and off at any time (on phones through a touch mapper).
// - The camera (MDTopDownCamera) is the game's own kind, added through ModLoader.ManageCameras, never in the game's view cycle. It
//   looks down at Pitch over a point (the pivot) on the ground, Height m above it, along the player's heading when it started.
//   The game hands it the movement keys / stick (UsesMovementControls) and the mouse look: both slide the pivot. The player stands.
// - Clicks: the game casts them from the active camera (the mouse from the screen centre, a tap through the tapped point), so the
//   tower tools work as on foot with a longer reach (MDTowerTools.Reach). Off a move, a click or tap on a tower picks it up (the
//   game's own reach is a few metres from the camera); block digging and placing are off while the view is on.
// - The view ends with the key, the panel's button, a mech boarded, a dialog, death, an injury ("受到攻击"), the game's camera key,
//   or (entered for one move) the move's end.
public static class MDTopDown {
    public const float Pitch=65*MathF.PI/180,MinHeight=12,MaxHeight=48,DefaultHeight=24,MaxOffset=48,ZoomStep=1.25f;
    public static bool Active,Auto;            // on; entered for one tower move (ends with it)
    public static Camera Previous;              // the camera before
    public static ComponentPlayer Player;       // whose view it is
    public static Vector3? StartAt;             // where the pivot starts (the tower picked up), else at the player
    public static string LastExit="";           // why it last ended (lab)
    public static int Entered,Exited;           // lab
    static float lastHealth=-1;

    public static MDTopDownCamera Camera(ComponentPlayer p)=>p?.GameWidget?.FindCamera<MDTopDownCamera>(false);
    public static bool On(ComponentPlayer p)=>Active&&p==Player&&p?.GameWidget?.ActiveCamera is MDTopDownCamera;

    public static bool Enter(ComponentPlayer p,bool auto,Vector3? at=null) {
        if(p?.GameWidget==null||Camera(p) is not {} cam)return false;
        if(On(p)){if(!auto)Auto=false;return true;}
        Previous=p.GameWidget.ActiveCamera is MDTopDownCamera?null:p.GameWidget.ActiveCamera;
        Player=p;Auto=auto;StartAt=at;Active=true;lastHealth=p.ComponentHealth.Health;
        p.GameWidget.ActiveCamera=cam;Entered++;
        if(p.GameWidget.ActiveCamera!=cam){Active=false;Player=null;return false;}
        return true;
    }
    public static void Exit(ComponentPlayer p,string why,string say=null) {
        if(!Active)return;
        Active=Auto=false;LastExit=why;Exited++;var gw=p?.GameWidget;
        if(gw!=null&&gw.ActiveCamera is MDTopDownCamera)gw.ActiveCamera=Previous!=null&&gw.Cameras.Contains(Previous)?Previous:gw.FindCamera<FppCamera>();
        Previous=null;Player=null;StartAt=null;
        if(p!=null&&!string.IsNullOrEmpty(say))MDPlayer.Message(p,say);
    }
    public static void Toggle(ComponentPlayer p){if(On(p))Exit(p,"key");else if(MDMechPilot.Mounted(p)==null)Enter(p,false,MDTowerTools.Moving?.Body.Position);}
    public static void Zoom(float factor){if(Camera(Player) is {} c)c.Height=Math.Clamp(c.Height*factor,MinHeight,MaxHeight);}

    // every frame from MDTowerTools.Input (after the game's input): the key, the ways out, and the game's clicks while it is on.
    // True when the view is on (the caller then leaves the block clicks alone).
    public static bool Input(ComponentPlayer p,WidgetInput gi,ref PlayerInput v) {
        if(Active&&Player!=null&&Player!=p)return false;
        if(MDMechKeys.Pressed(gi,MDMechAction.TopDown)){MDMechKeys.ConsumePressed(gi,MDMechAction.TopDown);Toggle(p);}
        if(!Active)return false;
        if(p.GameWidget.ActiveCamera is not MDTopDownCamera){Active=Auto=false;LastExit="camera";Exited++;Previous=null;Player=null;return false;}   // the game's camera key
        if(p.ComponentHealth.Health<=0){Exit(p,"dead");return false;}
        if(MDMechPilot.Mounted(p)!=null){Exit(p,"mech");return false;}
        if(DialogsManager.HasDialogs(p.GuiWidget)||p.ComponentGui.ModalPanelWidget!=null){Exit(p,"dialog");return false;}
        if(p.ComponentHealth.Health<lastHealth-1e-4f){Exit(p,"hurt","受到攻击，已退出俯瞰。");return false;}
        lastHealth=p.ComponentHealth.Health;
        if(gi.Scroll is Vector3 wheel&&wheel.Z!=0){Zoom(MathF.Pow(ZoomStep,-wheel.Z));v.ScrollInventory=0;}
        return true;
    }
}

public class MDTopDownCamera:BasePerspectiveCamera {
    public Vector3 Pivot;public float Height=MDTopDown.DefaultHeight;public Vector3 Forward=-Vector3.UnitZ;float groundY;bool hasGround;
    public MDTopDownCamera(GameWidget gameWidget):base(gameWidget){}
    public override bool UsesMovementControls=>true;
    public override bool IsEntityControlEnabled=>true;
    ComponentPlayer Player=>GameWidget.PlayerData?.ComponentPlayer;
    public override void Activate(Camera previousCamera) {
        var p=Player;var d=previousCamera?.ViewDirection??-Vector3.UnitZ;var flat=new Vector3(d.X,0,d.Z);
        Forward=flat.LengthSquared()>1e-4f?Vector3.Normalize(flat):-Vector3.UnitZ;
        Pivot=MDTopDown.StartAt??p?.ComponentBody.Position??previousCamera?.ViewPosition??Vector3.Zero;Height=MDTopDown.DefaultHeight;hasGround=false;
        Place(0);
    }
    public override void Update(float dt) {
        dt=Math.Min(dt,.1f);var p=Player;
        if(p!=null) {
            var pi=p.ComponentInput.PlayerInput;var right=Vector3.Normalize(Vector3.Cross(Forward,Vector3.UnitY));float speed=Height*.9f;
            Pivot+=(right*pi.CameraMove.X+Forward*pi.CameraMove.Z)*speed*dt;
            Pivot+=(right*pi.CameraLook.X+Forward*pi.CameraLook.Y)*speed*1.5f*dt;
            var at=p.ComponentBody.Position;var off=new Vector2(Pivot.X-at.X,Pivot.Z-at.Z);
            if(off.Length()>MDTopDown.MaxOffset){off*=MDTopDown.MaxOffset/off.Length();Pivot=new Vector3(at.X+off.X,Pivot.Y,at.Z+off.Y);}
        }
        Place(dt);
    }
    // the ground under the pivot (its top block), eased so steps do not jolt; kept where no chunk is loaded
    void Place(float dt) {
        var terrain=Player?.Project.FindSubsystem<SubsystemTerrain>(true);
        int x=Terrain.ToCell(Pivot.X),z=Terrain.ToCell(Pivot.Z);
        if(terrain?.Terrain.GetChunkAtCell(x,z) is {} chunk&&chunk.State>=TerrainChunkState.InvalidLight) {
            float y=terrain.Terrain.CalculateTopmostCellHeight(x,z)+1;
            groundY=hasGround&&dt>0?groundY+(y-groundY)*Math.Min(1,dt*6):y;hasGround=true;
        }
        else if(!hasGround){groundY=Pivot.Y;hasGround=true;}
        Pivot.Y=groundY;
        var dir=Forward*MathF.Cos(MDTopDown.Pitch)-Vector3.UnitY*MathF.Sin(MDTopDown.Pitch);
        SetupPerspectiveCamera(Pivot-dir*(Height/MathF.Sin(MDTopDown.Pitch)),dir,Vector3.UnitY);
    }
}

// The top-down view's panel: "俯瞰" (on a keyboard with its key) while a tower is picked up, "返回", "拉近", "拉远" while the view is on.
// Placed by MDSettings.TopDownHud (MDHudPlacement, at the right edge half way down unless moved) in MDHudLayoutScreen like the mech
// panel; MDSettings.TopDownHudShow hides it. A click or tap on it is not a click in the world (MDTowerTools: Touching).
public sealed class MDTopDownPanel:CanvasWidget {
    public const float Width=150,ButtonHeight=56,Gap=6;
    public readonly ButtonWidget Toggle=MDUi.Button("俯瞰",Width),Closer=MDUi.Button("拉近",Width),Farther=MDUi.Button("拉远",Width);
    readonly StackPanelWidget stack=new(){Direction=LayoutDirection.Vertical};
    readonly RectangleWidget back=new(){FillColor=new Color(8,20,30,150),OutlineColor=new Color(120,200,255,120),OutlineThickness=1};
    public MDTopDownPanel() {
        HorizontalAlignment=VerticalAlignment=WidgetAlignment.Near;
        foreach(var b in new[]{Toggle,Closer,Farther}){((CanvasWidget)b).Size=new Vector2(Width,ButtonHeight);b.Margin=new Vector2(0,Gap/2);stack.Children.Add(b);}
        Children.Add(back);Children.Add(stack);SetWidgetPosition(stack,new Vector2(Gap,Gap/2));Layout(true);
    }
    public void Layout(bool zoom) {
        Closer.IsVisible=Farther.IsVisible=zoom;int n=zoom?3:1;
        var size=new Vector2(Width+2*Gap,n*(ButtonHeight+Gap)+Gap);if(Size!=size){Size=size;back.Size=size;}
    }
    // the editor's sample: all three buttons
    public void Sample(){Toggle.Text="俯瞰";Layout(true);}
    public void Refresh(bool on,bool keyboard) {
        var k=MDMechKeys.Get(MDMechAction.TopDown);
        Toggle.Text=(on?"返回":"俯瞰")+(keyboard&&k!=Key.Null?$"（{MDMechKeys.KeyName(k)}）":"");Layout(on);
    }
}

// The panel in play: a host over the controls area that places it as the mech panel's host does (MDHudHost).
public sealed class MDTopDownHost:CanvasWidget {
    public readonly MDTopDownPanel Panel=new();
    public static MDTopDownHost Current;   // the one on screen (MDTowerTools.Touching, the lab)
    public MDTopDownHost(){Size=new Vector2(float.PositiveInfinity);IsHitTestVisible=false;Children.Add(Panel);}
    public override void MeasureOverride(Vector2 available) {
        if(float.IsFinite(available.X)&&float.IsFinite(available.Y)&&available.X>0&&available.Y>0){var corner=MDSettings.TopDownHud.Corner(available,Panel.Size,out var m);SetWidgetPosition(Panel,corner);Panel.RenderTransform=m;}
        base.MeasureOverride(available);
    }
    // shows the panel while a tower is picked up or the view is on, and handles its buttons
    public static void Update(ComponentPlayer p) {
        bool want=MDSettings.TopDownHudShow&&p?.ComponentGui!=null&&(MDTowerTools.Moving!=null&&MDTowerTools.Mover==p||MDTopDown.On(p));
        if(!want){if(Current?.ParentWidget is ContainerWidget c&&(Current.Owner==p||Current.Owner==null))c.Children.Remove(Current);if(Current?.Owner==p||Current?.ParentWidget==null)Current=null;return;}
        if(Current==null||Current.Owner!=p){if(Current?.ParentWidget is ContainerWidget old)old.Children.Remove(Current);Current=new MDTopDownHost{Owner=p};p.ComponentGui.ControlsContainerWidget.Children.Add(Current);}
        var panel=Current.Panel;bool on=MDTopDown.On(p);
        string lab=LabPress;LabPress=null;   // the lab presses the buttons through this (it cannot touch the screen)
        if(panel.Toggle.IsClicked||lab=="toggle"){if(on)MDTopDown.Exit(p,"button");else MDTopDown.Enter(p,MDTowerTools.Moving!=null,MDTowerTools.Moving?.Body.Position);on=MDTopDown.On(p);}
        if(panel.Closer.IsClicked||lab=="closer")MDTopDown.Zoom(1/MDTopDown.ZoomStep);
        if(panel.Farther.IsClicked||lab=="farther")MDTopDown.Zoom(MDTopDown.ZoomStep);
        panel.Refresh(on,(p.GameWidget.Input.Devices&WidgetInputDevice.Keyboard)!=0);
    }
    public ComponentPlayer Owner;public static string LabPress;
    // a screen point on the panel
    public static bool Covers(Vector2 screen)=>Current?.ParentWidget!=null&&MDUi.Inside(Current.Panel,screen);
    // a finger or the visible mouse cursor on the panel this frame
    public static bool Touching(WidgetInput gi) {
        var panel=Current?.Panel;if(panel==null||Current.ParentWidget==null)return false;
        foreach(var t in gi.TouchLocations)if(Covers(t.Position))return true;
        return gi.IsMouseCursorVisible&&gi.MousePosition is Vector2 m&&Covers(m);
    }
}

// the camera joins every game view (never in the game's camera cycle: no GetCameraList entry), usable only while the view is on
public static class MDTopDownCameras {
    public static void Add(GameWidget gameWidget){if(gameWidget.FindCamera<MDTopDownCamera>(false)==null)gameWidget.AddCamera(new MDTopDownCamera(gameWidget),_=>MDTopDown.Active);}
}
