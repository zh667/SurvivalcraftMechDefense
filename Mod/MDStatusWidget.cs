using Engine;
namespace Game;
public class MDStatusWidget:CanvasWidget {
    readonly LabelWidget status,heat;readonly RectangleWidget armor;
    public MDStatusWidget(){
        Size=new(290,106);HorizontalAlignment=VerticalAlignment=WidgetAlignment.Far;Margin=new(20,85);IsHitTestVisible=false;
        void Add(Widget w,float x,float y){Children.Add(w);SetWidgetPosition(w,new(x,y));}
        Add(new RectangleWidget{Size=Size,FillColor=new Color(8,20,30,190),OutlineThickness=0},0,0);
        status=new LabelWidget{Size=new(270,24),FontScale=.7f,Color=Color.White};Add(status,10,10);
        heat=new LabelWidget{Size=new(270,24),FontScale=.7f,Color=new Color(90,220,255)};Add(heat,10,38);
        armor=new RectangleWidget{Size=new(270,3),FillColor=new Color(90,220,255),OutlineThickness=0};Add(armor,10,68);
        Add(new LabelWidget{Text="E 下车    V 视角    B 弹药    R 换弹",Size=new(270,24),FontScale=.65f,Color=Color.White},10,76);
    }
    public void Refresh(ComponentMDUnit u){status.Text=$"机甲  装甲 {u.Damage.Hitpoints*100:0}%   {u.Body.Velocity.Length()*3.6f:0} km/h";heat.Text=$"弹药 {u.AmmoName}   {u.Animation}   {(u.Heat>.9f?"冷却中":"就绪")}";armor.Size=new(270*Math.Clamp(u.Damage.Hitpoints,0,1),3);}
    public override void MeasureOverride(Vector2 available){LayoutTransform=Matrix.CreateScale(Math.Clamp(Math.Min(available.X/700,available.Y/450),.5f,1));base.MeasureOverride(available);}
}
