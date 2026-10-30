using Engine;
using Engine.Graphics;

namespace Game;

// 0.15.0 medic charge (user, 2026-10-30: "首领特殊掉落物品可以用来给医疗塔充能（可以设置医疗塔的最大回复量是多少，并显示剩余/总量，
// 没的可以用灰色槽，要可以在那个信息显示模组显示"). Each medic tower holds a store of healing points (mod setting 医疗塔储能,
// MDSettings.MedicCapacity; 0 = unlimited). Every heal takes what it restores from the store: a tower's or mech's health in its
// own points (tower hp, mech armour), a player's whole health counts PlayerPoints. Empty, it heals nothing. A medic charge cell
// (MDPartBlock.Kind.Charge, dropped by every boss) right-clicked on it fills it. A new medic is full; taken down it keeps its
// charge in eighths (MDDeploymentBlock bits 15-17: eighths used, 0 = full, so crafted and older items are full).
// Shown as a bar over the tower (green left, grey used up; MDTowerWorld.DrawElements) and in the Neorxna info panel.
public static class MDMedic {
    public const float PlayerPoints=100;
    public static bool Unlimited=>MDSettings.MedicCapacity<=0;
    public static float Max=>MDSettings.MedicCapacity;
    public static bool Is(ComponentMDUnit u)=>u?.Tower?.attack.type=="heal";
    public static float Left(ComponentMDUnit u)=>Unlimited?float.MaxValue:Math.Clamp(u.HealCharge<0?Max:u.HealCharge,0,Max);
    public static float Share(ComponentMDUnit u)=>Unlimited?1:Max<=0?0:Left(u)/Max;
    // takes up to `points` from the store; returns what it may heal
    public static float Take(ComponentMDUnit u,float points){if(Unlimited)return points;float left=Left(u),t=Math.Min(points,left);u.HealCharge=left-t;return t;}
    public static string Text(ComponentMDUnit u)=>Unlimited?"储能：不限":$"储能 {Left(u):0} / {Max:0}"+(Left(u)<=0?"（已耗尽，用医疗充能核心充满）":"");
    public static int UsedEighths(ComponentMDUnit u)=>Unlimited?0:Math.Clamp((int)MathF.Round((1-Share(u))*7),0,7);
    public static float FromEighths(int used)=>Max*(1-Math.Clamp(used,0,7)/7f);
    public static int Charges;   // tests: cells used
    // right-click with a charge cell (ModLoader OnPlayerInputInteract, before repairs)
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,System.Runtime.CompilerServices.StrongBox<double>> last=new();
    public static bool TryCharge(ComponentPlayer p) {
        var ray=p.ComponentInput.PlayerInput.Interact;var miner=p.ComponentMiner;
        if(!ray.HasValue||MDPartBlock.Of(miner.ActiveBlockValue)!=MDPartBlock.Kind.Charge)return false;
        var u=miner.Raycast<BodyRaycastResult>(ray.Value,RaycastMode.Interaction)?.ComponentBody.Entity.FindComponent<ComponentMDUnit>();
        if(u==null)return false;
        var held=last.GetValue(p,_=>new(double.MinValue));double now=u.Clock.GameTime;if(now-held.Value<.4&&now>=held.Value)return true;held.Value=now;
        MDPlayer.Message(p,Charge(miner,u));return true;
    }
    public static string Charge(ComponentMiner miner,ComponentMDUnit u) {
        if(!Is(u))return "医疗充能核心只能给医疗塔充能。";
        if(u.Destroyed)return "医疗塔已损毁，先修好再充能。";
        if(Unlimited)return "模组设置里医疗塔储能是“不限”，不需要充能。";
        if(Left(u)>=Max)return $"储能已满（{Max:0} / {Max:0}）。";
        u.HealCharge=Max;miner?.RemoveActiveTool(1);Charges++;
        var w=MDTowerWorld.For(u.Project);var top=u.Body.Position+Vector3.UnitY*2.4f;w.Flash(top,1.2f,new Color(120,255,200),.3f);w.Ring(u.Body.Position,2.5f,new Color(120,255,200),.7f);
        w.Sparks(top,Vector3.UnitY,16,new Color(140,255,210),4,1,.6f);
        return $"医疗塔已充满：{Max:0} / {Max:0}";
    }
}

public sealed partial class MDTowerWorld {
    public static int BarsDrawn;   // tests
    // a charge bar over a medic tower: green for what is left, grey for what is used up
    void ChargeBar(ComponentMDUnit u,Vector3 at,Vector3 right,Vector3 up,float fade,float dist) {
        float w=1.3f+.03f*dist,h=.15f+.004f*dist,share=MDMedic.Share(u);
        Bar(at,right,up,w+.06f,h+.06f,0,1,new Color((byte)12,(byte)14,(byte)18,(byte)(220*fade)));                       // frame
        Bar(at,right,up,w,h,0,1,new Color((byte)95,(byte)98,(byte)104,(byte)(235*fade)));                                  // grey: used up
        if(share>0)Bar(at,right,up,w,h,0,share,(share>.25f?new Color(110,232,150):new Color(255,190,90))*fade);           // left (amber when low)
        BarsDrawn++;
    }
    // a solid quad from x0 to x1 (0..1 of the width w) centred on at; the atlas's solid slot 15 (tools/build_status_icons.py)
    void Bar(Vector3 at,Vector3 right,Vector3 up,float w,float h,float x0,float x1,Color c) {
        const float u=3/4f+1/32f,v=3/4f+1/32f,q=1/4f-1/16f;var a=at-right*(w*.5f)+right*(w*x0);var b=at-right*(w*.5f)+right*(w*x1);var y=up*(h*.5f);
        iconBatch.QueueQuad(a-y,b-y,b+y,a+y,new Vector2(u,v+q),new Vector2(u+q,v+q),new Vector2(u+q,v),new Vector2(u,v),c);
    }
}
