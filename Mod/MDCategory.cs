using HarmonyLib;

namespace Game;

// 0.18.0 (players via the user, 2026-11-04: "给这些防御塔和机兵新开一个背包栏目"): the towers, the mechs (MDDeploymentBlock) and the
// robot summons (MDSummonBlock) have their own creative inventory category. API 1.9.3.1 and 1.9.4 build the category list the same
// way: BlocksManager.InitializeCategories adds the 14 vanilla ones, then PostProcessBlocksLoad adds every other name a block's
// GetCategory returns for its creative values (BlocksManager.AddCategory), after them in block order. CreativeInventoryWidget shows
// LanguageControl.Get("BlocksManager", name), so the shown name comes from this mod's Assets/Lang/<language>.json (zh-CN and en-US,
// both 机甲防线; other languages fall back to en-US). A category has no icon in either version, only a colour (the widget's
// constructor sets Minerals / Electrics / Plants / Weapons); ours gets the mod's accent colour. Until 0.18.0 the towers and mechs were
// under 武器 and the summons under an untranslated "Spawn". The user then (11-04): every block and item of this mod goes here, so the
// materials, beacon, weapon kits, medic cell and handbook (MDPartBlock) too.
// 0.18.1 (players via the user, 2026-11-04: "机甲防线那个栏目太靠后了，放到武器栏目之后就可以"): the category came after all 14 vanilla ones
// (and before other mods' later ones). Once every block's category is in (ModLoader.BlocksInitalized, the last step of
// BlocksManager.PostProcessBlocksLoad in 1.9.3.1 and 1.9.4) it moves to right after Weapons; the others keep their order. The creative
// inventory widget and the Recipaedia copy BlocksManager.Categories when they are made, after that, so both show the new order. A
// player's remembered page (ComponentCreativeInventory CategoryIndex) is a position: one saved on Clothes or later opens one page
// earlier once.
public static class MDCategory {
    public const string Name="MechDefense";
    public static readonly Engine.Color Color=new(90,210,225);
    public static void Place(List<string> list) {
        int i=list.IndexOf(Name);if(i<0)return;list.RemoveAt(i);
        int w=list.IndexOf("Weapons");list.Insert(w>=0?w+1:Math.Min(i,list.Count),Name);
    }
}

[HarmonyPatch(typeof(CreativeInventoryWidget),MethodType.Constructor,new[]{typeof(GameEntitySystem.Entity)})]
public static class MDCategoryColour {
    public static void Postfix(CreativeInventoryWidget __instance){foreach(var c in __instance.m_categories)if(c.Name==MDCategory.Name)c.Color=MDCategory.Color;}
}
