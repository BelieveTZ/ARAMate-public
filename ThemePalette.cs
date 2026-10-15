using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
namespace ConfidenceDesktop {
    // This palette is also the source of the generated web CSS variables.
    public sealed class ThemePalette {
        public readonly string Id, Name;
        readonly Dictionary<string,Color> colors;
        public ThemePalette Overlay {get{return Piltover;}}
        public bool IsLight {get{var bg=colors["bg"];return .2126*Linear(bg.R)+.7152*Linear(bg.G)+.0722*Linear(bg.B)>.5;}}
        public Color MainColor(Color color){if(!IsLight)return color;double luminance=.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);while(luminance>.25){color=Color.FromArgb((int)(color.R*.95),(int)(color.G*.95),(int)(color.B*.95));luminance=.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);}return color;}
        static double Linear(byte n){double v=n/255.0;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
        ThemePalette(string id,string name,Dictionary<string,Color> values){Id=id;Name=name;colors=values;}
        public IEnumerable<string> ColorKeys {get{return colors.Keys;}}
        public Color this[string key]{get{return colors[key];}}
        static Color Hex(string value){return ColorTranslator.FromHtml(value);}
        public Dictionary<string,string> Css(){var result=colors.ToDictionary(p=>p.Key,p=>"#"+p.Value.R.ToString("x2")+p.Value.G.ToString("x2")+p.Value.B.ToString("x2"));var muted=colors["muted"];result["scheme"]=IsLight?"light":"dark";result["crumb"]="rgba("+muted.R+","+muted.G+","+muted.B+",.45)";if(IsLight)result["crumb"]=result["muted"];result["shadow-menu"]=IsLight?"#20345b26":"#0008";result["shadow-dialog"]=IsLight?"#20345b38":"#000a";result["shadow-tooltip"]=IsLight?"#20345b30":"#0009";
            result["semantic-physicalDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#ff8c34")));result["semantic-attackDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#ff8c34")));result["semantic-magicDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#6fa8ff")));result["semantic-abilityPower"] = ColorTranslator.ToHtml(MainColor(Hex("#6fa8ff")));result["semantic-trueDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#ffffff")));result["semantic-heal"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-health"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-lifeSteal"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-omnivamp"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-shield"] = ColorTranslator.ToHtml(MainColor(Hex("#8fd8e8")));result["semantic-armor"] = ColorTranslator.ToHtml(MainColor(Hex("#f2c46d")));result["semantic-magicResist"] = ColorTranslator.ToHtml(MainColor(Hex("#b88cff")));result["semantic-movementSpeed"] = ColorTranslator.ToHtml(MainColor(Hex("#f3e9a4")));result["semantic-attackSpeed"] = ColorTranslator.ToHtml(MainColor(Hex("#f7d44a")));result["semantic-criticalStrike"] = ColorTranslator.ToHtml(MainColor(Hex("#ff6a4d")));result["semantic-abilityHaste"] = ColorTranslator.ToHtml(MainColor(Hex("#e8e0c8")));result["semantic-mana"] = ColorTranslator.ToHtml(MainColor(Hex("#3fb6ff")));result["semantic-gold"] = ColorTranslator.ToHtml(MainColor(Hex("#ffd23f")));result["semantic-armorPenetration"] = ColorTranslator.ToHtml(MainColor(Hex("#ff8c34")));result["semantic-magicPenetration"] = ColorTranslator.ToHtml(MainColor(Hex("#6fa8ff")));result["semantic-onHit"] = ColorTranslator.ToHtml(MainColor(Hex("#f0a060")));result["semantic-passive"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-active"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-keywordMajor"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-spellName"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-spellPassive"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-unique"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-keyword"] = ColorTranslator.ToHtml(MainColor(Hex("#e9d7a6")));result["semantic-status"] = ColorTranslator.ToHtml(MainColor(Hex("#e9d7a6")));result["semantic-recast"] = ColorTranslator.ToHtml(MainColor(Hex("#e9d7a6")));result["semantic-flavorText"] = ColorTranslator.ToHtml(MainColor(Hex("#9fb3b8")));result["semantic-rarityPrismatic"] = ColorTranslator.ToHtml(MainColor(Hex("#e87cff")));result["semantic-rarityLegendary"] = ColorTranslator.ToHtml(MainColor(Hex("#f0a030")));result["semantic-ornnBonus"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));return result;}
        static readonly Dictionary<string,string> defaults=new Dictionary<string,string>{
            {"bg","#0a1720"},{"panel","#10232e"},{"field","#0b1c27"},{"line","#29414e"},{"text","#e5edf0"},{"muted","#91a7b6"},{"gold","#d9bb73"},{"selected","#d9bb73"},{"selected-text","#17242a"},{"active","#283332"},{"hover","#183440"},
            {"button-hover","#1a3340"},{"button-border-hover","#64716a"},{"field-border","#38515f"},{"accent-text","#17242a"},{"accent-hover","#e6cb8d"},{"placeholder","#78909f"},{"suggestion","#142c39"},{"suggestion-border","#49616d"},{"active-border","#716343"},{"header","#132a36"},{"separator","#233a46"},{"arrow","#768e9a"},{"dialog","#102632"},{"dialog-border","#4a6069"},{"tooltip","#19333e"},{"tooltip-border","#68776e"},{"tooltip-text","#e2ecef"},{"toast-border","#9d7547"},{"toast","#352c23"},{"scroll-thumb","#38505d"},{"tooltip-muted","#a9bbc1"},{"select-arrow","#c9d8df"},
            {"desktop-text","#e7edf5"},{"desktop-muted","#94a5b9"},{"desktop-field","#17242f"},{"choice-text","#dbe4eb"},{"grid-alternate","#141c29"},{"grid-text","#dde4e9"},{"grid-selection-text","#ffffff"},{"source-text","#899cae"},{"outline","#060a0e"},{"replay","#1e2328"}
        };
        // Preserve the legacy RGB offset from the nearest semantic base. Clamp only at gamut limits.
        static ThemePalette Create(string id,string name,string[] bases){
            string[] keys={"bg","panel","field","line","text","muted","gold","active","hover"};
            var values=defaults.ToDictionary(p=>p.Key,p=>Hex(p.Value));
            if(bases!=null){var originals=keys.ToDictionary(k=>k,k=>values[k]);var next=keys.Select((k,i)=>new{Key=k,Value=Hex(bases[i])}).ToDictionary(p=>p.Key,p=>p.Value);
                foreach(string key in defaults.Keys){string anchor=keys.Contains(key)?key:Anchor(key);Color old=values[key],basis=originals[anchor],target=next[anchor];values[key]=Color.FromArgb(Clamp(target.R+old.R-basis.R),Clamp(target.G+old.G-basis.G),Clamp(target.B+old.B-basis.B));}
            }
            return new ThemePalette(id,name,values);
        }
        static int Clamp(int n){return Math.Max(0,Math.Min(255,n));}
        static string Anchor(string key){
            if(key=="selected")return "gold";
            if(key=="selected-text")return "bg";
            if(new[]{"accent-hover","active-border","toast-border","tooltip-border","button-border-hover"}.Contains(key))return "gold";
            if(new[]{"desktop-text","choice-text","grid-text","grid-selection-text","tooltip-text","select-arrow"}.Contains(key))return "text";
            if(new[]{"desktop-muted","source-text","placeholder","arrow","tooltip-muted"}.Contains(key))return "muted";
            if(new[]{"field-border","suggestion-border","dialog-border","separator","scroll-thumb"}.Contains(key))return "line";
            if(new[]{"outline","accent-text","replay"}.Contains(key))return "bg";
            if(key=="toast")return "active";
            if(key=="desktop-field")return "field";
            if(key=="button-hover")return "hover";
            return "panel";
        }
        public static readonly ThemePalette Piltover=Create("piltover","皮尔特沃夫",null);
        public static readonly ThemePalette Void=Create("void","虚空",new[]{"#120e1c","#1b1529","#150f22","#3a2f52","#ece6f5","#a69bbd","#e48fe8","#2e1f38","#251c32"});
        public static readonly ThemePalette Shadow=Create("shadow","暗影岛",new[]{"#111314","#1a1d1e","#141617","#303638","#e6ebea","#9aa5a3","#3ddc97","#133027","#1f2725"});
                public static readonly ThemePalette Noxus=CreateNoxus();
        static ThemePalette CreateNoxus(){var p=Create("noxus","诺克萨斯",new[]{"#000000","#161616","#101010","#333333","#eeeeee","#a3a3a3","#e82623","#2a2a2a","#1f1f1f"});foreach(string key in p.colors.Keys.ToArray()){
                if(new[]{"gold","accent-hover","accent-text","selected","selected-text"}.Contains(key))continue;
                var color=p.colors[key];int gray=(color.R+color.G+color.B)/3;p.colors[key]=Color.FromArgb(gray,gray,gray);
            }
            // Share Shadow's background surfaces after neutralization, keeping Noxus interactions and text.
            string[] bases={"bg","panel","field","line","text","muted","gold","active","hover"};
            foreach(string key in p.colors.Keys.ToArray()){
                string anchor=bases.Contains(key)?key:Anchor(key);
                if(new[]{"bg","panel","field"}.Contains(anchor)&&key!="accent-text"&&key!="selected-text")p.colors[key]=Shadow[key];
            }
            p.colors["accent-text"]=Hex("#080304");p.colors["selected-text"]=p.colors["accent-text"];return p;}
        public static readonly ThemePalette Demacia=CreateDemacia();
        // Demacia main-window navy, preserving the original relative palette offsets.
        static ThemePalette CreateDemacia(){Color basis=Hex("#0b1533");Func<int,int,int,string> blue=(r,g,b)=>ColorTranslator.ToHtml(Color.FromArgb(Clamp(basis.R+r),Clamp(basis.G+g),Clamp(basis.B+b)));var p=Create("demacia","德玛西亚",new[]{blue(-13,-17,-25),blue(0,0,0),blue(-8,-10,-14),blue(39,43,51),"#ffffff","#c3cbdc","#ffffff",blue(14,16,23),blue(8,9,13)});p.colors["outline"]=Hex("#060a0e");p.colors["desktop-text"]=Color.White;p.colors["desktop-muted"]=Hex("#c3cbdc");p.colors["accent-text"]=basis;p.colors["selected-text"]=basis;return p;}

        public static readonly ThemePalette[] All={Piltover,Shadow,Noxus};
        public static ThemePalette Find(string id){return All.FirstOrDefault(p=>p.Id==id)??Piltover;}
    }
    public static class ThemeManager {
        static ThemePalette current=ThemePalette.Piltover;
        public static ThemePalette Current {get{return current;}}
        public static event EventHandler Changed;
        public static void Select(string id){var next=ThemePalette.Find(id);if(ReferenceEquals(next,Current))return;current=next;var handler=Changed;if(handler!=null)handler(null,EventArgs.Empty);}
    }
}
