using Engine;
using Engine.Audio;
using Engine.Graphics;
using Game;
using HarmonyLib;
using System.Text.Json;

namespace EndfieldVerification;

public class Smoke:ModLoader {
    static int stage,shot;static double began,step;static WorldInfo world;static PlayerData player;
    static Vector3 origin;static ComponentMDUnit[] towers,targets;static readonly List<string> checks=[];
    static Vector3[] anchors;static float savedWreck;
    static readonly string runId=Guid.NewGuid().ToString();static readonly PrimitivesRenderer3D renderer=new();
    static string Output=>Path.Combine(AppContext.BaseDirectory,"endfield-result.json");
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
    public override void __ModInitialize(){new Harmony("local.endfield.smoke").PatchAll(typeof(Smoke).Assembly);SettingsManager.FileAssociationEnabled=false;}
    // The clean test profile needs the optional mod's console binding before it
    // processes any input. This affects only the isolated test runtime.
    public override IEnumerable<KeyValuePair<string,object>> GetKeyboardMappings(){yield return new("NUIConsle",(Engine.Input.Key)(-1));}
    public override void OnLoadingFinished(List<Action> actions){actions.Add(()=>{began=Time.RealTime;Window.Frame+=Frame;});}
    static ComponentMDUnit Spawn(string template,Vector3 p){var e=DatabaseManager.CreateEntity(GameManager.Project,"MD"+template,true);var u=e.FindComponent<ComponentMDUnit>();u.Body.Position=p;u.Body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);u.Yaw=u.AimYaw=MathF.PI;u.Cooldown=1000;e.FindComponent<ComponentSpawn>().SpawnDuration=0;GameManager.Project.AddEntity(e);return u;}
    static void AimCamera(int variant=0) {
        var p=player.ComponentPlayer;var c=p.GameWidget.FindCamera<FixedCamera>();p.GameWidget.ActiveCamera=c;
        var eye=origin+new Vector3(11,6,-6);var focus=origin+new Vector3(0,2,6);
        if(variant>0){float x=(variant-2)*5;eye=origin+new Vector3(x+3.3f,4.1f,.2f);focus=origin+new Vector3(x,2.1f,4);}
        c.SetupPerspectiveCamera(eye,Vector3.Normalize(focus-eye),Vector3.UnitY);
    }
    static void Frame() {
        try {
            if(Time.RealTime-began>180)throw new TimeoutException("stage "+stage);
            if(stage==0) {
                if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
                SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=.2f;SettingsManager.MusicVolume=0;
                SettingsManager.ShowLogoInScreenshots=false;
                world=WorldsManager.CreateWorld(new WorldSettings{Name="Endfield_Three_Verification",Seed="42",GameMode=GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
                GameManager.LoadProject(world,ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget"));
                player=new PlayerData(GameManager.Project){Name="Endfield test",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>().AddPlayerData(player);ScreensManager.SwitchScreen("Game");stage=1;return;
            }
            if(stage==1) {
                if(!player.IsReadyForPlaying)return;
                origin=new(Terrain.ToCell(player.ComponentPlayer.ComponentBody.Position.X)+.5f,70.05f,Terrain.ToCell(player.ComponentPlayer.ComponentBody.Position.Z)+.5f);
                var terrain=GameManager.Project.FindSubsystem<SubsystemTerrain>();
                for(int x=-24;x<25;x++)for(int z=-20;z<35;z++)for(int y=68;y<81;y++)terrain.ChangeCell((int)origin.X+x,y,(int)origin.Z+z,y<70?1:0);
                player.ComponentPlayer.ComponentBody.Position=origin-new Vector3(0,0,6);
                towers=new ComponentMDUnit[3];targets=new ComponentMDUnit[3];
                for(int i=0;i<3;i++){
                    towers[i]=Spawn(i==0?"Turret":"Turret"+(i+1),origin+new Vector3((i-1)*5,0,4));
                    var fixedBody=towers[i].Body as ComponentMDTowerBody;
                    Check(fixedBody!=null,$"variant {i+1} fixed structure body");fixedBody.FixAt(fixedBody.Position,fixedBody.Rotation);
                    var fixedPosition=fixedBody.Position;
                    fixedBody.ApplyImpulse(new Vector3(50,20,30));fixedBody.ApplyDirectMove(new Vector3(3,0,2));fixedBody.Velocity=new(30,20,10);fixedBody.Update(.1f);
                    Check(fixedBody.Position==fixedPosition&&fixedBody.Velocity==Vector3.Zero&&!fixedBody.CanBePushedByOtherBodies,$"variant {i+1} rejects pushes impulses and gravity");
                    targets[i]=Spawn("Enemy",origin+new Vector3((i-1)*5,0,14));targets[i].Damage.AttackResilience=100000;
                    var mesh=MDAssets.Load(towers[i].AssetName);Check(mesh.vertices.Length>20000&&mesh.muzzles.Length==2&&mesh.modelOrigin=="local-authored-bedrock",$"variant {i+1} authored mesh and sockets");
                    Check(mesh.pointSampling&&mesh.animation?.tracks.Length==3,$"variant {i+1} pixel atlas and three original VAT tracks");
                    var idle=MDEndfieldAnimation.Pose(mesh,0,0,"Idle",0);
                    var firing=MDEndfieldAnimation.Pose(mesh,.4f,.2f,"Fire",.12f);
                    var basePoint=MDAssets.V(mesh.vertices.First(v=>v.bone==0).p);
                    Check(Vector3.Distance(Vector3.Transform(basePoint,idle[0]),Vector3.Transform(basePoint,firing[0]))<.00001f,$"variant {i+1} base remains fixed under aim and VAT");
                    var barrelPoint=MDAssets.V(mesh.vertices.First(v=>v.bone==4).p);
                    var fireOnly=MDEndfieldAnimation.Pose(mesh,0,0,"Fire",.12f);
                    Check(Vector3.Distance(Vector3.Transform(barrelPoint,idle[4]),Vector3.Transform(barrelPoint,fireOnly[4]))>.01f,$"variant {i+1} original barrel VAT moves authored geometry");
                    for(int barrel=0;barrel<2;barrel++) {
                        var muzzle=MDAssets.V(mesh.muzzles[barrel]);float radialMax=0;
                        for(int frame=0;frame<=80;frame++) {
                            var poses=MDEndfieldAnimation.Pose(mesh,0,0,"Fire",frame/300f);
                            var point=Vector3.Transform(muzzle,poses[mesh.muzzleBones[barrel]]);
                            radialMax=Math.Max(radialMax,new Vector2(point.X-muzzle.X,point.Y-muzzle.Y).Length());
                        }
                        Check(radialMax<.06f,$"variant {i+1} barrel {barrel+1} stays centered for complete firing cycle");
                    }
                    Check(MDEndfieldAnimation.Frame(mesh.animation,"Death",3)==30,$"variant {i+1} death holds source damaged clip");
                    Check(MDEndfieldAnimation.Frame(mesh.animation,"Hit",2)==28,$"variant {i+1} hit returns through source recovery clip");
                    Check(towers[i].TurretVariant==i+1,$"variant {i+1} template identity");
                    foreach(int take in Enumerable.Range(0,3))Check(ContentManager.Get<SoundBuffer>($"Imported/Endfield/Audio/au_int_fac_battle_turret_{i+1}_skill_{take}")!=null,$"variant {i+1} native audio mix {take} loads");
                }
                anchors=towers.Select(t=>t.Body.Position).ToArray();
                var bodies=GameManager.Project.FindSubsystem<SubsystemBodies>();
                var face=new CellFace((int)origin.X+12,69,(int)origin.Z+2,4);
                var snap=MDTowerPlacement.Snap(face,2);
                Check(snap==new Vector3(face.X+1,70,face.Z+1),"two-cell base snaps to integer grid intersection");
                Check(MDTowerPlacement.Validate(terrain,bodies,snap,2,out _),"clear supported grid footprint is accepted");
                terrain.ChangeCell(face.X,69,face.Z,0);
                Check(!MDTowerPlacement.Validate(terrain,bodies,snap,2,out _),"missing support tile rejects footprint");terrain.ChangeCell(face.X,69,face.Z,1);
                terrain.ChangeCell(face.X+1,72,face.Z+1,1);
                Check(!MDTowerPlacement.Validate(terrain,bodies,snap,2,out _),"occupied height rejects deployment");terrain.ChangeCell(face.X+1,72,face.Z+1,0);
                Check(MDTowerPlacement.IsReserved(bodies,(int)towers[1].Body.Position.X,71,(int)towers[1].Body.Position.Z),"placed tower reserves cells against block placement");
                MDNeorxnaCompatibility.Install();
                var panelType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)).FirstOrDefault(t=>t!=null);
                Check(panelType!=null&&MDNeorxnaCompatibility.Installed,"actual Neorxna info mod compatibility installed");
                var panel=Activator.CreateInstance(panelType);
                foreach(var tower in towers) {
                    panelType.GetMethod("SetInfo_Entity").Invoke(panel,[tower.Entity]);
                    Check(((LabelWidget)panelType.GetField("m_name").GetValue(panel)).Text==MDTowerPlacement.Name(tower.TurretVariant),$"Neorxna displays Chinese name for variant {tower.TurretVariant}");
                    Check(((LabelWidget)panelType.GetField("m_details").GetValue(panel)).Text.Contains("机甲防线"),"Neorxna displays friendly mod name");
                }
                Check(BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()].GetCreativeValues().Count()==5,"three turret items plus retained mech/enemy items");
                var fx=MDEndfieldEffects.Load();Check(fx.effects.Count==14,"fourteen source FX clips load");
                Check(fx.meshes.Length>0&&fx.effects["p_interactive_machineguntower_bullet_801"].meshes.Length>0,"projectile uses recovered mesh not a line");
                AimCamera();step=Time.RealTime;stage=2;return;
            }
            if(stage==2) {
                foreach(var t in targets){t.Cooldown=1000;t.Body.Velocity=Vector3.Zero;}
                foreach(var t in towers)t.Cooldown=1000;
                if(Time.RealTime-step<.8)return;
                for(int i=0;i<3;i++) {
                    towers[i].Animation="Idle";towers[i].AimPitch=0;towers[i].AimYaw=towers[i].Yaw;
                    Check(Vector3.Dot(towers[i].Muzzle-towers[i].Body.Position,towers[i].Direction)>0,$"variant {i+1} muzzle is in front");
                    towers[i].Endfield.Fire(targets[i].Body);
                    Check(towers[i].Endfield.LastSound.StartsWith("Imported/Endfield/Audio/"),$"variant {i+1} Endfield sound routing");
                }
                shot++;step=Time.RealTime;stage=3;return;
            }
            if(stage==3) {
                foreach(var t in targets){t.Cooldown=1000;t.Body.Velocity=Vector3.Zero;}
                foreach(var t in towers)t.Cooldown=1000;
                // Capture mid-cycle: the former muzzle-swap bug peaks here,
                // whereas the first .02 seconds looked correct even when broken.
                if(Time.RealTime-step<.12)return;
                AimCamera(shot==1?1:shot==4?2:shot==6?3:0);var c=player.ComponentPlayer.GameWidget.ActiveCamera;
                // Direct draw ensures exceptions are not swallowed by the production drawing guard.
                foreach(var t in towers){MDAssets.Draw(t,c,renderer);t.Endfield.Draw(c,renderer);}renderer.Flush(c.ViewProjectionMatrix);
                if(shot==1||shot==4||shot==6)ScreenCaptureManager.Capture(1280,720,$"endfield-shot-{shot}.png");
                if(shot==4)Check(towers[1].Endfield.LastMuzzleEffect=="p_fxbat_machineguntower_upgrade_attack_801","fourth expanded-tower shot uses source heavy FX");
                if(shot<6){stage=2;return;}
                Check(towers[2].Endfield.LastMuzzleEffect.EndsWith("2502"),"sixth Rainstorm shot uses source heavy FX");
                foreach(var t in towers)t.Cooldown=0;
                step=Time.RealTime;stage=4;return;
            }
            if(stage==4) {
                if(Time.RealTime-step<1)return;
                foreach(var t in towers){t.Cooldown=1000;Check(t.Endfield.Impacts>=1,$"variant {t.TurretVariant} shot impacts in game");}
                Check(towers.All(t=>t.Endfield.Shots>6),"all three acquire and fire autonomously");
                Check(targets.All(t=>t.Damage.Hitpoints<1),"all three damage hostile targets");
                var t0=towers[0];var terrain=GameManager.Project.FindSubsystem<SubsystemTerrain>();
                int wz=Terrain.ToCell(t0.Body.Position.Z+4),wx=Terrain.ToCell(t0.Body.Position.X);
                for(int x=wx-1;x<=wx+1;x++)for(int y=70;y<76;y++)terrain.ChangeCell(x,y,wz,1);
                Check(!MDCombat.ClearShot(t0,targets[0].Body),"source projectile ray is blocked by wall");
                Check(!MDCombat.IsEnemy(t0,towers[1].Body)&&!MDCombat.IsEnemy(t0,player.ComponentPlayer.ComponentBody),"turrets and player stay friendly");
                Check(towers.Select((t,i)=>t.Body.Position==anchors[i]).All(x=>x),"fixed tower positions stable through real world simulation");
                foreach(var unit in GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null))unit.Damage.Damage(2);
                step=Time.RealTime;stage=5;return;
            }
            if(stage==5) {
                if(!player.IsReadyForPlaying||Time.RealTime-step<2)return;
                foreach(var unit in GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null)) {
                    Check(unit.Destroyed&&unit.Animation=="Death"&&unit.AnimationTime>unit.AnimationDuration,"destroyed unit holds death without replay: "+unit.Entity.ValuesDictionary.DatabaseObject.Name);
                    unit.PlayEvent("Hit",.2f);unit.PlayEvent("Fire",.2f);Check(unit.Animation=="Death","death rejects subsequent hit/fire transitions");
                }
                AimCamera(3);ScreenCaptureManager.Capture(1280,720,"endfield-custom-damaged.png");
                step=Time.RealTime;stage=6;return;
            }
            if(stage==6&&Time.RealTime-step>.5) {
                savedWreck=towers[2].WreckTime;
                GameManager.SaveProject(true,false);GameManager.LoadProject(world,ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget"));
                var restored=GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u?.Kind=="Turret").ToArray();
                Check(restored.Length==3&&restored.Select(u=>u.TurretVariant).Distinct().Count()==3,"all turret variants survive save/reload");
                Check(restored.All(u=>u.TurretShotCount>=6),"enhanced-round counters survive reload");
                Check(restored.All(u=>u.Destroyed&&u.Animation=="Death"&&u.WreckTime>=savedWreck-.1f),"terminal death and elapsed wreck time survive reload");
                Check(restored.OrderBy(u=>u.TurretVariant).Select((t,i)=>t.Body.Position==anchors[i]).All(x=>x),"fixed anchors survive save reload");
                towers=restored;step=Time.RealTime;stage=7;return;
            }
            if(stage==7&&Time.RealTime-step>9&&towers.All(u=>!u.Entity.IsAddedToProject)) {
                Check(towers.All(u=>!u.Entity.IsAddedToProject),"all wrecks removed once after original lifetime across reload");
                File.WriteAllText(Output,JsonSerializer.Serialize(new{passed=true,runId,checks,actualGameLaunched=true,api=ModsManager.APIVersionString,world=world.DirectoryName},new JsonSerializerOptions{WriteIndented=true}));Window.Frame-=Frame;Window.Close();
            }
        } catch(Exception e){File.WriteAllText(Output,JsonSerializer.Serialize(new{passed=false,runId,stage,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));Window.Frame-=Frame;Window.Close();}
    }
}
[HarmonyPatch(typeof(FileAssociationManager),nameof(FileAssociationManager.Initialize))]
public static class NoFileAssociation { public static bool Prefix()=>false; }
