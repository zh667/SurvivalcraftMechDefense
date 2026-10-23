using System.Reflection;
using Engine;
using Engine.Input;

namespace Game;

// The mech's actions a player can put on other keys.
public enum MDMechAction { Hover, Jump, Melee, Switch, View, Fire, Board }

// Rebindable mech keys (user, 2026-10-24: "机甲绑定按键编辑（类似于CS枪械适配铃兰触控那样）"). Keyboard keys only, kept per device in
// MDSettings (ModSettings.xml, never in a world); the game's own key mapping is not touched. On phones a touch mapper such as
// 铃兰触控 puts screen buttons that press keys, so a button mapped to one of these keys works the mech, as for the CS weapons
// mod (its ScGunBindings). Fire and board keep their game inputs (mouse button or screen tap, the game's mount key); their
// keys here are extra and unset by default. The page that edits them is MDSettingsScreen ("机甲按键").
public static class MDMechKeys {
    public static readonly MDMechAction[] All=Enum.GetValues<MDMechAction>();
    static readonly Dictionary<MDMechAction,Key> keys=[];
    public static Key Default(MDMechAction a)=>a switch{MDMechAction.Hover=>Key.Shift,MDMechAction.Jump=>Key.Space,MDMechAction.Melee=>Key.F,MDMechAction.Switch=>Key.B,MDMechAction.View=>Key.V,_=>Key.Null};
    public static Key Get(MDMechAction a)=>keys.TryGetValue(a,out var k)?k:Default(a);
    public static Dictionary<MDMechAction,Key> Current()=>All.ToDictionary(a=>a,Get);
    public static Dictionary<MDMechAction,Key> Defaults()=>All.ToDictionary(a=>a,Default);
    // a whole set at once (settings loaded, the page's Save), cleaned up as Normalize says
    public static void Apply(IReadOnlyDictionary<MDMechAction,Key> set){var n=Normalize(set);keys.Clear();foreach(var (a,k) in n)keys[a]=k;}
    // any keyboard key but Escape (cancels a binding on the page) and Delete (clears one)
    public static bool Bindable(Key k)=>k!=Key.Null&&Enum.IsDefined(k)&&k is not (Key.Escape or Key.Delete);
    // every action exactly once; an unusable key goes back to the default; a key used twice stays with the first action and the
    // later one gets its default when that is still free, else nothing
    public static Dictionary<MDMechAction,Key> Normalize(IReadOnlyDictionary<MDMechAction,Key> set) {
        var r=new Dictionary<MDMechAction,Key>();var used=new HashSet<Key>();
        foreach(var a in All) {
            var k=set!=null&&set.TryGetValue(a,out var v)?v:Default(a);
            if(k!=Key.Null&&!Bindable(k))k=Default(a);
            if(k!=Key.Null&&used.Contains(k))k=Default(a)!=Key.Null&&!used.Contains(Default(a))?Default(a):Key.Null;
            r[a]=k;if(k!=Key.Null)used.Add(k);
        }
        return r;
    }
    // the other action that already has this key in a set (the page refuses the key then)
    public static MDMechAction? Holder(IReadOnlyDictionary<MDMechAction,Key> set,MDMechAction a,Key k)=>
        k==Key.Null?null:All.Where(x=>x!=a&&set.TryGetValue(x,out var v)&&v==k).Select(x=>(MDMechAction?)x).FirstOrDefault();
    public static bool Down(WidgetInput input,MDMechAction a){var k=Get(a);return k!=Key.Null&&input.IsKeyDown(k);}
    public static bool Pressed(WidgetInput input,MDMechAction a){var k=Get(a);return k!=Key.Null&&input.IsKeyDownOnce(k);}

    public static string ActionName(MDMechAction a)=>a switch{MDMechAction.Hover=>"悬浮（按住）",MDMechAction.Jump=>"跳跃",MDMechAction.Melee=>"近战",
        MDMechAction.Switch=>"切换武器",MDMechAction.View=>"切换视角",MDMechAction.Fire=>"开火（额外键）",_=>"上／下车（额外键）"};
    public static string KeyName(Key k)=>k switch{Key.Null=>"未设置",Key.Space=>"空格",Key.Control=>"Ctrl",Key.Enter=>"回车",Key.BackSpace=>"退格",
        Key.LeftArrow=>"←",Key.RightArrow=>"→",Key.UpArrow=>"↑",Key.DownArrow=>"↓",Key.Tilde=>"~",Key.Minus=>"-",Key.Plus=>"=",Key.LeftBracket=>"[",
        Key.RightBracket=>"]",Key.Semicolon=>";",Key.Quote=>"'",Key.Comma=>",",Key.Period=>".",Key.Slash=>"/",Key.BackSlash=>"\\",
        >=Key.Number0 and <=Key.Number9=>((int)k-(int)Key.Number0).ToString(),_=>k.ToString()};
    // a key of the game's own keyboard mapping (Key.Null if unset, a mouse button or unreadable), by reflection: the mapping API
    // differs between versions
    static MethodInfo mapping;static bool mappingLooked;
    public static Key GameKey(string name) {
        try {
            if(!mappingLooked){mappingLooked=true;mapping=typeof(SettingsManager).GetMethod("GetKeyboardMapping",[typeof(string),typeof(bool)]);}
            if(mapping?.Invoke(null,[name,false]) is Key k)return k;
        } catch {}
        return name=="ToggleMount"?Key.R:Key.Null;
    }
    // the game's mount key (R unless the player changed it): it always boards and leaves, so no mech action may take it
    public static string MountKey(){var k=GameKey("ToggleMount");return KeyName(k==Key.Null?Key.R:k);}

    // The game's keyboard actions a mech key can share (mapping name, PlayerInput field, shown name). While a player drives a
    // mech, a pressed mech key does only the mech's action: Suppress clears the game's actions on the same key for that frame
    // (0.11.0: hover on C also opened the clothing panel, which then blocked the mech's input). The mount key is not here.
    static readonly (string Mapping,string Field,string Name)[] gameActions=[("ToggleInventory","ToggleInventory","物品栏"),
        ("ToggleClothing","ToggleClothing","衣服"),("TakeScreenshot","TakeScreenshot","截图"),("TimeOfDay","TimeOfDay","时间"),
        ("Lightning","Lighting","闪电"),("Precipitation","Precipitation","降水"),("Fog","Fog","雾"),("Drop","Drop","丢弃物品"),
        ("EditItem","EditItem","编辑物品"),("KeyboardHelp","KeyboardHelp","键盘帮助"),("ToggleFly","ToggleCreativeFly","飞行"),
        ("ToggleCrouch","ToggleCrouch","潜行"),("SwitchCameraMode","SwitchCameraMode","切换视角"),("Jump","Jump","跳跃")];
    static readonly Dictionary<string,FieldInfo> fields=[];
    static FieldInfo Field(string name){if(!fields.TryGetValue(name,out var f))fields[name]=f=typeof(PlayerInput).GetField(name) is {} x&&x.FieldType==typeof(bool)?x:null;return f;}
    // the game's actions that use this key ("衣服、降水"), for the settings page
    public static string SharedWith(Key k)=>k==Key.Null?"":string.Join("、",gameActions.Where(g=>GameKey(g.Mapping)==k).Select(g=>g.Name));
    public static void Suppress(WidgetInput input,ref PlayerInput v,params MDMechAction[] actions) {
        HashSet<Key> down=null;
        foreach(var a in actions){var k=Get(a);if(k!=Key.Null&&(input.IsKeyDown(k)||input.IsKeyDownOnce(k)))(down??=[]).Add(k);}
        if(down==null)return;
        object boxed=v;bool changed=false;
        foreach(var g in gameActions)
            if(down.Contains(GameKey(g.Mapping))&&Field(g.Field) is {} f&&(bool)f.GetValue(boxed)){f.SetValue(boxed,false);changed=true;}
        if(changed)v=(PlayerInput)boxed;
    }
    // hint texts: "Shift", or the key plus the extra one for fire / board, or "未设置"
    public static string Hint(MDMechAction a) {
        var k=Get(a);
        return a switch{MDMechAction.Board=>k==Key.Null?MountKey():MountKey()+"／"+KeyName(k),MDMechAction.Fire=>k==Key.Null?"左键":"左键／"+KeyName(k),_=>KeyName(k)};
    }
    public static string ToText(IReadOnlyDictionary<MDMechAction,Key> set)=>string.Join(";",All.Select(a=>$"{a}={(set.TryGetValue(a,out var k)?k:Default(a))}"));
    // "Hover=Shift;Jump=Space;…"; anything unreadable keeps the default
    public static Dictionary<MDMechAction,Key> FromText(string text) {
        var r=Defaults();
        foreach(var part in (text??"").Split(';',StringSplitOptions.RemoveEmptyEntries)) {
            var kv=part.Split('=');
            if(kv.Length==2&&Enum.TryParse<MDMechAction>(kv[0].Trim(),out var a)&&Enum.TryParse<Key>(kv[1].Trim(),out var k))r[a]=k;
        }
        return r;
    }
}
