using Engine.Input;
using Game;
using HarmonyLib;

namespace TowerLab;

// 0.16.2 (user 2026-10-31: 切换机甲的弹药类型（X键）会触发原版游戏的操作, it opened the recipe book: RecipaediaEX's mapping
// "Recipaedia" is X and it polls Keyboard.IsKeyDownOnce in its GuiUpdate hook): what reads keys after the mech in a frame. While On,
// counts the frames in which ComponentGui's update (where the GuiUpdate hooks run) still sees a watched key pressed, straight from
// the keyboard and through a keyboard mapping another mod would register (Mapping, put in by lab --mode mech). Used by Mech.cs.
[HarmonyPatch(typeof(ComponentGui),nameof(ComponentGui.Update))]
public static class LabGuiKeys {
    public static bool On;public static readonly Dictionary<Key,int> Seen=[];public static int Mapped;
    public const string Mapping="LabRecipeBook",Package="lab.keys16";
    static readonly Key[] watched=[Key.X,Key.R,Key.B,Key.F,Key.V,Key.Number3];
    public static void Postfix(ComponentGui __instance) {
        if(!On)return;
        foreach(var k in watched)if(Keyboard.IsKeyDownOnce(k))Seen[k]=Seen.GetValueOrDefault(k)+1;
        if(__instance.m_componentPlayer.GameWidget.Input.IsKeyOrMouseDownOnce(Mapping))Mapped++;
    }
    public static void Reset(){Seen.Clear();Mapped=0;}
    public static string Text()=>$"keyboard {(Seen.Count==0?"none":string.Join(" ",Seen.Select(x=>$"{x.Key}x{x.Value}")))}, mapping {Mapped}";
}
