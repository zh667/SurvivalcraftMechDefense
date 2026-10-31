using Engine;
using Game;

namespace TowerLab;

// lab --mode stacking (0.16.2, user 2026-10-31: 配件最高只能40个, a stack-size mod's setting did not reach them): such a mod sets
// every block's MaxStacking when a world loads. The inventories ask GetMaxStacking(value) (ComponentInventoryBase.GetSlotCapacity),
// so the parts above 4 must follow a raised MaxStacking and the others keep their own. Results under "stacking".
public partial class Lab {
    static IEnumerable<bool> Stacking() {
        var r=new Dictionary<string,object>();results["stacking"]=r;var bad=new List<string>();
        var b=BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDPartBlock>()];var kinds=Enum.GetValues<MDPartBlock.Kind>();
        var own=new[]{40,40,40,40,4,4,8,8,8,16,1};
        string Row()=>string.Join(" ",kinds.Select(k=>$"{k}={b.GetMaxStacking(MDPartBlock.Value(k))}"));
        void Check(string step,Func<int,int> want){foreach(var k in kinds){int got=b.GetMaxStacking(MDPartBlock.Value(k)),w=want(own[(int)k]);
            if(got!=w)bad.Add($"{step}: {k} {got}, want {w}");}}
        int m0=b.MaxStacking;r["maxStacking"]=m0;if(m0!=40)bad.Add($"MaxStacking {m0}, want 40");
        r["default"]=Row();Check("default",s=>s);
        foreach(var raised in new[]{99,999,10000000}) {
            b.MaxStacking=raised;yield return true;
            r[$"raised{raised}"]=Row();Check($"raised {raised}",s=>s>4?raised:s);
        }
        b.MaxStacking=m0;r["restored"]=Row();Check("restored",s=>s);
        if(bad.Count>0)throw new Exception("stacking: "+string.Join(" || ",bad));
    }
}
