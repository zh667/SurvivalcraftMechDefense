using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

// 1.0.0 (11-13) the defence core's store (user, 2026-11-13: "既然防线核心点击不再显示附近炮台攻击范围了，核心增加吸收附近掉落物的功能，点击可以改成打开
// 内置储存空间，方便持续作战。如果太卡可以把附近掉落物直接传送到防线核心的内置储存空间"):
// - A right-click (a tap) on a core with anything but a repair material opens its store: the game's chest panel, Slots slots
//   (until 1.0.0 that click showed every tower's range: MDTowerTools.ToggleAll; the Z key still marks towers one by one).
// - Every AbsorbEvery s the live core takes the items lying within AbsorbRange m into its store, straight in (no flight: cheap on
//   phones), once they have lain AbsorbAge s (a player close by picks them up first); not experience (that is the player's),
//   not items flying to someone. A full store leaves the rest where it lies.
// - Saved with the core (Values "CoreSlots"/"SlotN", the layout MDSaveGuard reads for the block numbers of this mod's items).
//   The core destroyed or taken down with a pickaxe: everything in it is dropped where it stood.
public sealed class MDCoreStorage:IInventory {
    public const int Slots=16;   // the chest panel's grid (Widgets/ChestWidget: 4 x 4, the same in API 1.9.3.1 and 1.9.4)
    public const float AbsorbRange=16,AbsorbEvery=.5f; public const double AbsorbAge=1.5;
    public static bool AbsorbEnabled=true;   // lab switch
    public static int Absorbed,Opened;        // tests: items taken in, panels opened
    readonly ComponentMDUnit core; readonly int[] values=new int[Slots],counts=new int[Slots];
    public MDCoreStorage(ComponentMDUnit core){this.core=core;}
    public ComponentMDUnit Core=>core;
    public Project Project=>core.Project;
    public int SlotsCount=>Slots;
    public int VisibleSlotsCount{get=>Slots;set{}}
    public int ActiveSlotIndex{get=>-1;set{}}
    public int GetSlotValue(int i)=>i>=0&&i<Slots&&counts[i]>0?values[i]:0;
    public int GetSlotCount(int i)=>i>=0&&i<Slots?counts[i]:0;
    public int GetSlotCapacity(int i,int value)=>i>=0&&i<Slots?BlocksManager.Blocks[Terrain.ExtractContents(value)].GetMaxStacking(value):0;
    public int GetSlotProcessCapacity(int i,int value)=>0;
    public void AddSlotItems(int i,int value,int count) {
        if(count<=0||i<0||i>=Slots)return;
        if(counts[i]!=0&&values[i]!=value)throw new InvalidOperationException($"core store slot {i}: holds {values[i]}, not {value}");
        if(counts[i]+count>GetSlotCapacity(i,value))throw new InvalidOperationException($"core store slot {i}: over capacity");
        values[i]=value;counts[i]+=count;
    }
    public void ProcessSlotItems(int i,int value,int count,int processCount,out int processedValue,out int processedCount){processedValue=value;processedCount=count;}
    public int RemoveSlotItems(int i,int count){if(i<0||i>=Slots)return 0;count=Math.Min(count,counts[i]);counts[i]-=count;return count;}
    public void DropAllItems(Vector3 position) {
        var pk=Project.FindSubsystem<SubsystemPickables>(true);var rng=new System.Random();
        for(int i=0;i<Slots;i++) {
            if(counts[i]<=0)continue;int value=values[i],n=counts[i];counts[i]=0;
            pk.AddPickable(value,n,position,new Vector3((float)rng.NextDouble()*4-2,3+(float)rng.NextDouble()*2,(float)rng.NextDouble()*4-2),null,core.Entity);
        }
    }
    public int Items{get{int n=0;for(int i=0;i<Slots;i++)n+=counts[i];return n;}}
    public void Load(ValuesDictionary v) {
        var s=v.GetValue<ValuesDictionary>("CoreSlots",null);if(s==null)return;
        for(int i=0;i<Slots;i++)if(s.GetValue<ValuesDictionary>("Slot"+i,null) is {} slot){values[i]=slot.GetValue("Contents",0);counts[i]=Math.Max(0,slot.GetValue("Count",0));}
    }
    public void Save(ValuesDictionary v) {
        var s=new ValuesDictionary();v.SetValue("CoreSlots",s);
        for(int i=0;i<Slots;i++)if(counts[i]>0){var slot=new ValuesDictionary();slot.SetValue("Contents",values[i]);slot.SetValue("Count",counts[i]);s.SetValue("Slot"+i,slot);}
    }

    // ---- taking in what lies round the live core (MDThreat.Update, every frame; works every AbsorbEvery s)
    static double nextAbsorb;
    public static void Absorb(Project p,ComponentMDUnit core) {
        if(!AbsorbEnabled||core?.Storage==null)return;
        double now=p.FindSubsystem<SubsystemGameInfo>(true).TotalElapsedGameTime;if(now<nextAbsorb&&nextAbsorb-now<=AbsorbEvery)return;nextAbsorb=now+AbsorbEvery;
        var pk=p.FindSubsystem<SubsystemPickables>(true);var at=core.Body.Position;int xp=BlocksManager.GetBlockIndex<ExperienceBlock>();int took=0;
        foreach(var item in pk.m_pickables) {
            if(item.ToRemove||item.Count<=0||item.FlyToPosition.HasValue||now-item.CreationTime<AbsorbAge||Terrain.ExtractContents(item.Value)==xp)continue;
            if(Vector3.DistanceSquared(item.Position,at)>AbsorbRange*AbsorbRange)continue;
            int left=ComponentInventoryBase.AcquireItems(core.Storage,item.Value,item.Count);if(left==item.Count)continue;
            took+=item.Count-left;if(left==0)item.ToRemove=true;else item.Count=left;
        }
        if(took>0) {
            Absorbed+=took;var w=MDTowerWorld.For(p);var top=at+Vector3.UnitY*(core.Tower.bounds[1][1]*.6f);
            w.Sparks(top,Vector3.UnitY,6,new Color(120,210,255),3,.8f,.3f,.04f);
            p.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/PickableCollected",.5f,0,at,4,true);
        }
    }
    // a right-click on a core (MDTowerTools.CoreClick)
    public static void Open(ComponentPlayer p,ComponentMDUnit core) {
        if(core.Storage==null)return;Opened++;
        p.ComponentGui.ModalPanelWidget=new MDCoreStorageWidget(p,core);
    }
    // destroyed or taken down: what it held lies where it stood
    public static void Spill(ComponentMDUnit core) {
        if(core?.Storage==null||core.Storage.Items==0)return;
        core.Storage.DropAllItems(core.Body.Position+Vector3.UnitY*1.2f);
    }
}

// The game's chest panel over the core's store and the player's inventory (the chest widget wants a ComponentChest; this one
// fills the same layout itself).
public sealed class MDCoreStorageWidget:CanvasWidget {
    public const float Reach=10;   // walking further off closes it
    readonly ComponentPlayer player; readonly ComponentMDUnit core;
    public MDCoreStorageWidget(ComponentPlayer p,ComponentMDUnit core) {
        player=p;this.core=core;
        LoadContents(this,ContentManager.Get<System.Xml.Linq.XElement>("Widgets/ChestWidget"));
        if(Children.Find<LabelWidget>("ChestLabel",false) is {} label)label.Text=core.CoreLive?"防线核心储存":"防线核心储存（已失效的核心）";
        var store=Children.Find<GridPanelWidget>("ChestGrid");var inv=Children.Find<GridPanelWidget>("InventoryGrid");int n=0;
        for(int i=0;i<store.RowsCount;i++)for(int j=0;j<store.ColumnsCount;j++){var s=new InventorySlotWidget();s.AssignInventorySlot(core.Storage,n++);store.Children.Add(s);store.SetWidgetCell(s,new Point2(j,i));}
        n=10;
        for(int i=0;i<inv.RowsCount;i++)for(int j=0;j<inv.ColumnsCount;j++){var s=new InventorySlotWidget();s.AssignInventorySlot(p.ComponentMiner.Inventory,n++);inv.Children.Add(s);inv.SetWidgetCell(s,new Point2(j,i));}
    }
    public override void Update() {
        if(!core.Entity.IsAddedToProject||core.Destroyed||Vector3.Distance(core.Body.Position,player.ComponentBody.Position)>Reach)ParentWidget?.Children.Remove(this);
    }
}
