using HarmonyLib;

namespace Game;

// 0.18.0 (players via the user, 2026-11-04: "给这些防御塔和机兵新开一个背包栏目"): the towers, the mechs (MDDeploymentBlock) and the
// robot summons (MDSummonBlock) have their own creative inventory category. API 1.9.3.1 and 1.9.4 build the category list the same
// way: BlocksManager.InitializeCategories adds the 14 vanilla ones, then PostProcessBlocksLoad adds every other name a block's
// GetCategory returns for its creative values (BlocksManager.AddCategory), after them in block order. CreativeInventoryWidget shows
// LanguageControl.Get("BlocksManager", name), so the shown name comes from this mod's Assets/Lang/<language>.json (zh-CN and en-US,
// both 机甲防线; other languages fall back to en-US). A category has no icon in either version, only a colour (the widget's
// constructor sets Minerals / Electrics / Plants / Weapons); ours gets the mod's accent colour. Until 0.18.0 the towers and mechs were
// under 武器 and the summons under an untranslated "Spawn".
public static class MDCategory {
    public const string Name="MechDefense";
    public static readonly Engine.Color Color=new(90,210,225);
}

[HarmonyPatch(typeof(CreativeInventoryWidget),MethodType.Constructor,new[]{typeof(GameEntitySystem.Entity)})]
public static class MDCategoryColour {
    public static void Postfix(CreativeInventoryWidget __instance){foreach(var c in __instance.m_categories)if(c.Name==MDCategory.Name)c.Color=MDCategory.Color;}
}
