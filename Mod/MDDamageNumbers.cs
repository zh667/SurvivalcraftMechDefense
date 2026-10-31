using Engine;
using Engine.Graphics;

namespace Game;

// 0.15.0 damage numbers (user, 2026-10-30: "伤害数字显示……但是海量敌兵（不止我们这个模组有很多小兵）要处理得当，不要整得满屏数字，
// 也要注意手机设备性能问题，包括之前的元素反应"). What the player's side (towers, mechs, players) really took off a creature — read
// after every multiplier (armour phases, resistances, other mods), from the ModLoader hook CalculateCreatureInjuryAmount — in its
// element's colour, rising and fading over the target.
// Against clutter and for phones:
// - one number per target per MergeTime s: every hit in that time is added up (a gatling's 8 rounds a second make one number);
// - within NumberRange m of the camera only;
// - at most MaxShown numbers at once, fewer at a lower 特效数量 (精简 8, 极简 4): a new one replaces the faintest, smaller ones
//   give way to bigger ones and to reactions; reaction names are capped the same way (MaxLabels);
// - a reaction's number is bigger and in both colours' blend; numbers can be turned off (模组设置“伤害数字”).
public sealed partial class MDTowerWorld {
    public const float MergeTime=.35f,NumberRange=28,NumberLife=.9f;
    public static readonly int[] MaxShown=[14,8,4],MaxLabels=[6,4,2];
    sealed class Pending { public float amount,age; public MDElement element; public bool reaction; public Vector3 at; }
    sealed class Number { public Vector3 pos,drift; public string text; public Color c; public float age,size; public bool big; }
    readonly Dictionary<ComponentBody,Pending> pending=[];
    readonly List<Number> numbers=[];
    public static int NumbersShown,NumbersMerged;   // tests: numbers made, hits merged into them
    public int NumbersAlive=>numbers.Count;
    // set by MDTowerBrain.Hit around its AttackBody (synchronous): the element and whether the hit reacted
    public static MDElement HitElement; public static bool HitReacted;
    // ModLoader CalculateCreatureInjuryAmount (after MDBoss.CapInjury)
    public static void Injured(Injury injury) {
        if(!MDSettings.DamageNumbers||injury?.ComponentHealth is not {} h||injury.Amount<=0||injury.Attackment?.Attacker is not {} attacker)return;
        if(h.Entity.FindComponent<ComponentPlayer>()!=null||h.Entity.FindComponent<ComponentBody>() is not {} b)return;
        bool ours=attacker.FindComponent<ComponentMDUnit>()!=null||attacker.FindComponent<ComponentPlayer>()!=null;
        if(!ours||!worlds.TryGetValue(h.Project,out var w))return;
        float amount=injury.Amount*h.AttackResilience;if(amount<.5f)return;
        var e=attacker.FindComponent<ComponentMDUnit>()!=null?HitElement:MDElement.Kinetic;
        if(w.pending.TryGetValue(b,out var p)){p.amount+=amount;p.reaction|=HitReacted;if(HitReacted||p.element==MDElement.None)p.element=e;NumbersMerged++;}
        else w.pending[b]=new Pending{amount=amount,element=e,reaction=HitReacted,at=b.Position+Vector3.UnitY*(b.BoxSize.Y+NumbersAt)};
    }
    void SimulateNumbers(float dt) {
        for(int i=numbers.Count-1;i>=0;i--){var n=numbers[i];n.age+=dt;n.pos+=(Vector3.UnitY*1.1f+n.drift)*dt*(1-n.age/NumberLife);if(n.age>=NumberLife)numbers.RemoveAt(i);}
        if(pending.Count==0)return;
        List<ComponentBody> done=null;var eye=lastEye;
        foreach(var (b,p) in pending) {
            p.age+=dt;if(p.age<MergeTime)continue;(done??=[]).Add(b);
            if(lastEyeSet&&Vector3.DistanceSquared(p.at,eye)>NumberRange*NumberRange)continue;
            Show(p,b);
        }
        if(done!=null)foreach(var b in done)pending.Remove(b);
    }
    void Show(Pending p,ComponentBody b) {
        int lv=Math.Clamp(MDSettings.FxLevel,0,2),cap=MaxShown[lv];float size=1+.18f*MathF.Log10(Math.Max(1,p.amount))+(p.reaction?.45f:0);
        if(numbers.Count>=cap) {   // full: the faintest smaller one goes, or this one is dropped
            int weakest=-1;float score=float.MaxValue;
            for(int i=0;i<numbers.Count;i++){float s=numbers[i].size*(1-numbers[i].age/NumberLife);if(s<score){score=s;weakest=i;}}
            if(weakest<0||score>size)return;
            numbers.RemoveAt(weakest);
        }
        var c=p.element==MDElement.None?Color.White:Color.Lerp(MDElements.Colors[(int)p.element],Color.White,.25f);
        if(p.reaction)c=Color.Lerp(c,new Color(255,236,120),.5f);
        string text=p.amount>=100000?$"{p.amount/10000:0.#}万":$"{p.amount:0}";
        numbers.Add(new Number{pos=p.at+new Vector3(R(-.4f,.4f),R(0,.3f),R(-.4f,.4f)),drift=new Vector3(R(-.25f,.25f),0,R(-.25f,.25f)),text=text,c=c,size=size,big=p.reaction});
        NumbersShown++;
    }
    Vector3 lastEye;bool lastEyeSet;
    void DrawNumbers(Vector3 eye,Vector3 right,Vector3 up) {
        lastEye=eye;lastEyeSet=true;if(numbers.Count==0)return;
        labelBatch??=SubsystemParticles.PrimitivesRenderer.FontBatch(LabelWidget.BitmapFont,3,DepthStencilState.None,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.LinearClamp);
        var anchor=TextAnchor.HorizontalCenter|TextAnchor.VerticalCenter;
        foreach(var n in numbers) {
            float d=Vector3.Distance(n.pos,eye);if(d>NumberRange+4)continue;
            float t=n.age/NumberLife,a=t<.6f?1:1-(t-.6f)/.4f,pop=t<.1f?.7f+3*t:t<.2f?1.3f-(t-.1f)*3:1;
            float sc=(.018f+.0008f*d)*n.size*pop;
            var outline=new Color((byte)8,(byte)10,(byte)14,(byte)(200*a));
            foreach(var (ox,oy) in new[]{(-1,0),(1,0),(0,-1),(0,1)})labelBatch.QueueText(n.text,n.pos+(right*ox-up*oy)*sc*2f,right*sc,-up*sc,outline,anchor);   // four-way outline (cheap)
            labelBatch.QueueText(n.text,n.pos,right*sc,-up*sc,n.c*a,anchor);
        }
    }
}
