using Engine;
using Game;

namespace TowerLab;

// lab --mode mech-view: Mercury seen the way view-<id> sees a tower, so the same shots from two packages make a before /
// after with identical cameras (0.9.0 round 13: our polygon Mercury against the shipped model). Deployed from its item,
// then held at one spot facing -Z (its right is +X) in a forced pose, no clouds, the player moved out of the picture:
// around at mech height, top, player height, close-ups of the head, joints and guns, key frames of the clips that move the
// most (front and back), night, tiny camera steps near and far (flicker), fog and a torch-lit cave. Screenshots v_*.
public partial class Lab {
    static IEnumerable<bool> MechView() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(5))yield return true;   // the gallery's shells land first
        SettingsManager.SkyRenderingMode=SkyRenderingMode.NoClouds;
        var r=new Dictionary<string,object>();results["mechView"]=r;var views=new List<string>();
        var pl=player.ComponentPlayer;var inv=pl.ComponentMiner.Inventory;var d=MDMechLibrary.Def;
        pl.ComponentBody.Position=Home;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.5))yield return true;
        var block=(MDDeploymentBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()];
        int item=Terrain.MakeBlockValue(block.BlockIndex,0,1);inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,item,1);inv.ActiveSlotIndex=0;
        var foot=pl.ComponentBody.Position;pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
        var u=GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).FirstOrDefault(x=>x?.Mech!=null);
        if(u==null)throw new Exception("Mercury did not deploy");
        var m=u.Mech;var mm=u.Entity.FindComponent<ComponentMDMechModel>(true);
        while(Wait(1.5))yield return true;
        var at=u.Body.Position;pl.ComponentBody.Position=Home+new Vector3(0,0,40);pl.ComponentBody.Velocity=Vector3.Zero;
        r["model"]=$"{mm.Model?.Meshes.Count} meshes, {mm.Model?.Bones.Count} bones";r["at"]=at.ToString();
        string clip="idle";float frac=0;
        void Freeze(){u.Body.Position=at;u.Body.Velocity=Vector3.Zero;u.Yaw=u.AimYaw=0;u.Body.Rotation=Quaternion.Identity;m.ForceClip=clip;m.ForceTime=d.Seconds(d.Clip(clip))*frac;}
        IEnumerable<bool> View(string name,Vector3 eye,Vector3 look,int frames=4){for(int k=0;k<frames;k++){Freeze();Cam(eye,look);yield return true;}Freeze();Shot("v_"+name);views.Add(name);yield return true;}
        Vector3 P(float x,float y,float z)=>at+new Vector3(x,y,z);   // metres from the mech's feet: +X its right, -Z its front
        for(double s0=Time.RealTime;Time.RealTime-s0<1.5;){Freeze();yield return true;}
        var focus=P(0,2.2f,0);
        for(int k=0;k<8;k++){float a=k*MathF.PI/4;foreach(var x in View($"around_{k*45:000}",focus+new Vector3(-MathF.Sin(a),.25f,-MathF.Cos(a))*5.5f,focus))yield return x;}
        foreach(var x in View("top",focus+new Vector3(.02f,6.5f,-.3f),focus))yield return x;
        foreach(var x in View("player_front",P(1.5f,1.7f,-8),focus))yield return x;
        foreach(var x in View("player_back",P(-1.5f,1.7f,8),focus))yield return x;
        foreach(var x in View("close_head_front",P(.6f,4.3f,-2.4f),P(0,3.9f,-.2f)))yield return x;
        foreach(var x in View("close_cockpit_right",P(2.1f,4.1f,-1.8f),P(.1f,3.5f,-.3f)))yield return x;
        foreach(var x in View("close_shoulder_left",P(-2.6f,4.1f,-1.1f),P(-1.2f,3.6f,0)))yield return x;
        foreach(var x in View("close_rifle_right",P(2.8f,2.9f,-2.4f),P(1.3f,2.6f,-.6f)))yield return x;
        foreach(var x in View("close_back",P(-.9f,3.8f,3),P(0,3.2f,.4f)))yield return x;
        foreach(var x in View("close_hip_knee_left",P(-2.6f,1.7f,-1.9f),P(-.6f,1.4f,0)))yield return x;
        foreach(var x in View("close_feet_front",P(.9f,.8f,-2.8f),P(0,.3f,0)))yield return x;
        foreach(var (name,f) in new[]{("walk_forward",.25f),("walk_forward",.75f),("dash_forward",.5f),("jump",.5f),("hit",.5f),("melee",.5f),("death",1f),("cannon_deploy_fire",1f),("cannon_fire_loop",.5f),("cannon_retract",.5f)}) {
            if(d.Clip(name)==null)continue;clip=name;frac=f;
            foreach(var x in View($"clip_{name}_{(int)(f*100):000}_front",focus+new Vector3(.7f,.15f,-1)*4.6f,focus))yield return x;
            foreach(var x in View($"clip_{name}_{(int)(f*100):000}_back",focus+new Vector3(.8f,.35f,1)*4.6f,focus))yield return x;
        }
        clip="idle";frac=0;
        var gi=GameManager.Project.FindSubsystem<SubsystemGameInfo>(true);gi.WorldSettings.TimeOfDayMode=TimeOfDayMode.Night;
        foreach(var x in View("night_front",P(1.5f,1.7f,-8),focus,20))yield return x;
        gi.WorldSettings.TimeOfDayMode=TimeOfDayMode.Day;foreach(var x in View("day_again",P(1.5f,1.7f,-8),focus,20))yield return x;
        var near=P(0,3,0);
        for(int k=0;k<6;k++)foreach(var x in View($"flicker_near_{k}",near+new Vector3(.9f+k*.004f,.35f,-1.1f)*3.2f,near,2))yield return x;
        for(int k=0;k<6;k++)foreach(var x in View($"flicker_far_{k}",focus+new Vector3(.9f+k*.002f,.25f,-1.1f)*22f,focus,2))yield return x;
        {   // fog: as view-<id>; the fog layer's height comes from a seed taken from the fog start time
            var wx=GameManager.Project.FindSubsystem<SubsystemWeather>(true);var sky=GameManager.Project.FindSubsystem<SubsystemSky>(true);
            gi.WorldSettings.AreWeatherEffectsEnabled=true;wx.m_precipitationStartTime=wx.m_precipitationEndTime=double.MaxValue;
            double fogNow=Math.Floor(gi.TotalElapsedGameTime);string fogInfo="none";
            for(int k=1;k<=400&&fogInfo=="none";k++) {
                for(int n=0;n<2;n++){Freeze();wx.m_fogStartTime=fogNow-k;wx.m_fogEndTime=double.PositiveInfinity;wx.FogIntensity=1;Cam(P(1.5f,1.7f,-8),focus);yield return true;}
                if(sky.ViewFogBottom<=at.Y+.5f&&sky.ViewFogTop>=at.Y+6.5f&&sky.ViewFogDensity>=.05f)fogInfo=$"start -{k} s: bottom {sky.ViewFogBottom:0.0}, top {sky.ViewFogTop:0.0}, density {sky.ViewFogDensity:0.000}";
            }
            r["fog"]=fogInfo;
            foreach(var x in View("fog_front",P(1.5f,1.7f,-8),focus,10))yield return x;
            foreach(var x in View("fog_far",focus+new Vector3(.9f,.25f,-1.1f)*22f,focus,6))yield return x;
            wx.FogIntensity=0;gi.WorldSettings.AreWeatherEffectsEnabled=false;for(int n=0;n<3;n++){Freeze();yield return true;}
        }
        {   // cave: a closed granite room around the mech, four torches on the floor
            var ter=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);int ax=(int)MathF.Floor(at.X),ay=(int)MathF.Floor(at.Y),az=(int)MathF.Floor(at.Z);
            for(int cx=ax-8;cx<=ax+8;cx++)for(int cz=az-10;cz<=az+8;cz++)for(int cy=ay-1;cy<=ay+8;cy++)
                if(cx==ax-8||cx==ax+8||cz==az-10||cz==az+8||cy==ay-1||cy==ay+8)ter.ChangeCell(cx,cy,cz,3);
            foreach(var (cx,cz) in new[]{(ax-3,az-4),(ax+3,az-4),(ax-3,az+3),(ax+3,az+3)})ter.ChangeCell(cx,ay,cz,Terrain.MakeBlockValue(31,0,4));
            for(double s0=Time.RealTime;Time.RealTime-s0<3;){Freeze();yield return true;}
            r["caveLight"]=Terrain.ExtractLight(ter.Terrain.GetCellValue(ax,ay+2,az-5));
            foreach(var x in View("cave_front",P(1.5f,1.7f,-7.5f),focus,10))yield return x;
            foreach(var x in View("cave_right",P(6.5f,1.7f,-2.5f),focus,6))yield return x;
            foreach(var x in View("cave_back",P(-1.5f,1.7f,6.5f),focus,6))yield return x;
        }
        m.ForceClip=null;SettingsManager.SkyRenderingMode=SkyRenderingMode.Full;results["views"]=views;
    }
}
