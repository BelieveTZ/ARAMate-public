using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
namespace ConfidenceDesktop {
    // This palette is also the source of the generated web CSS variables.
    public sealed class ThemePalette {
        public readonly string Id, Name;
        readonly Dictionary<string,Color> colors;
        ThemePalette overlay;
        public ThemePalette Overlay {get{return overlay??this;}}
        public bool IsLight {get{return Id=="demacia";}}
        public Color MainColor(Color color){if(!IsLight)return color;double luminance=.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);while(luminance>.25){color=Color.FromArgb((int)(color.R*.95),(int)(color.G*.95),(int)(color.B*.95));luminance=.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);}return color;}
        static double Linear(byte n){double v=n/255.0;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
        ThemePalette(string id,string name,Dictionary<string,Color> values){Id=id;Name=name;colors=values;}
        public IEnumerable<string> ColorKeys {get{return colors.Keys;}}
        public Color this[string key]{get{return colors[key];}}
        static Color Hex(string value){return ColorTranslator.FromHtml(value);}
        public Dictionary<string,string> Css(){var result=colors.ToDictionary(p=>p.Key,p=>"#"+p.Value.R.ToString("x2")+p.Value.G.ToString("x2")+p.Value.B.ToString("x2"));var muted=colors["muted"];result["scheme"]=IsLight?"light":"dark";result["crumb"]="rgba("+muted.R+","+muted.G+","+muted.B+",.45)";if(IsLight)result["crumb"]=result["muted"];result["shadow-menu"]=IsLight?"#20345b26":"#0008";result["shadow-dialog"]=IsLight?"#20345b38":"#000a";result["shadow-tooltip"]=IsLight?"#20345b30":"#0009";
            result["semantic-physicalDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#ff8c34")));result["semantic-attackDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#ff8c34")));result["semantic-magicDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#6fa8ff")));result["semantic-abilityPower"] = ColorTranslator.ToHtml(MainColor(Hex("#6fa8ff")));result["semantic-trueDamage"] = ColorTranslator.ToHtml(MainColor(Hex("#ffffff")));result["semantic-heal"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-health"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-lifeSteal"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-omnivamp"] = ColorTranslator.ToHtml(MainColor(Hex("#5fd35f")));result["semantic-shield"] = ColorTranslator.ToHtml(MainColor(Hex("#8fd8e8")));result["semantic-armor"] = ColorTranslator.ToHtml(MainColor(Hex("#f2c46d")));result["semantic-magicResist"] = ColorTranslator.ToHtml(MainColor(Hex("#b88cff")));result["semantic-movementSpeed"] = ColorTranslator.ToHtml(MainColor(Hex("#f3e9a4")));result["semantic-attackSpeed"] = ColorTranslator.ToHtml(MainColor(Hex("#f7d44a")));result["semantic-criticalStrike"] = ColorTranslator.ToHtml(MainColor(Hex("#ff6a4d")));result["semantic-abilityHaste"] = ColorTranslator.ToHtml(MainColor(Hex("#e8e0c8")));result["semantic-mana"] = ColorTranslator.ToHtml(MainColor(Hex("#3fb6ff")));result["semantic-gold"] = ColorTranslator.ToHtml(MainColor(Hex("#ffd23f")));result["semantic-armorPenetration"] = ColorTranslator.ToHtml(MainColor(Hex("#ff8c34")));result["semantic-magicPenetration"] = ColorTranslator.ToHtml(MainColor(Hex("#6fa8ff")));result["semantic-onHit"] = ColorTranslator.ToHtml(MainColor(Hex("#f0a060")));result["semantic-passive"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-active"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-keywordMajor"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-spellName"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-spellPassive"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-unique"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));result["semantic-keyword"] = ColorTranslator.ToHtml(MainColor(Hex("#e9d7a6")));result["semantic-status"] = ColorTranslator.ToHtml(MainColor(Hex("#e9d7a6")));result["semantic-recast"] = ColorTranslator.ToHtml(MainColor(Hex("#e9d7a6")));result["semantic-flavorText"] = ColorTranslator.ToHtml(MainColor(Hex("#9fb3b8")));result["semantic-rarityPrismatic"] = ColorTranslator.ToHtml(MainColor(Hex("#e87cff")));result["semantic-rarityLegendary"] = ColorTranslator.ToHtml(MainColor(Hex("#f0a030")));result["semantic-ornnBonus"] = ColorTranslator.ToHtml(MainColor(Hex("#f0c75e")));return result;}
        static readonly Dictionary<string,string> defaults=new Dictionary<string,string>{
            {"bg","#0a1720"},{"panel","#10232e"},{"field","#0b1c27"},{"line","#29414e"},{"text","#e5edf0"},{"muted","#91a7b6"},{"gold","#d9bb73"},{"active","#283332"},{"hover","#183440"},
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
        static ThemePalette CreateNoxus(){var p=Create("noxus","诺克萨斯",new[]{"#000000","#161315","#110f12","#3b2a2c","#eee6e6","#ab9b9c","#e82623","#3a1517","#241a1c"});p.colors["accent-text"]=Hex("#080304");return p;}
        public static readonly ThemePalette Demacia=CreateDemacia();
        static ThemePalette CreateDemacia(){var values=new Dictionary<string,Color>{
            {"bg",Hex("#f4f6fb")},
            {"panel",Hex("#ffffff")},
            {"field",Hex("#f0f3fa")},
            {"line",Hex("#aab5cc")},
            {"text",Hex("#1a2138")},
            {"muted",Hex("#52617e")},
            {"gold",Hex("#232d45")},
            {"active",Hex("#e2e6ef")},
            {"hover",Hex("#edf0f5")},
            {"button-hover",Hex("#e5ebf7")},
            {"button-border-hover",Hex("#6475ad")},
            {"field-border",Hex("#98a7c4")},
            {"accent-text",Hex("#ffffff")},
            {"accent-hover",Hex("#34405e")},
            {"placeholder",Hex("#61708b")},
            {"suggestion",Hex("#ffffff")},
            {"suggestion-border",Hex("#9baac5")},
            {"active-border",Hex("#232d45")},
            {"header",Hex("#e9eef8")},
            {"separator",Hex("#c1cbde")},
            {"arrow",Hex("#52617e")},
            {"dialog",Hex("#ffffff")},
            {"dialog-border",Hex("#9baac5")},
            {"tooltip",Hex("#f0f3fa")},
            {"tooltip-border",Hex("#8e9dbb")},
            {"tooltip-text",Hex("#1a2138")},
            {"toast-border",Hex("#687bb1")},
            {"toast",Hex("#e9eefb")},
            {"scroll-thumb",Hex("#91a1c2")},
            {"tooltip-muted",Hex("#52617e")},
            {"select-arrow",Hex("#38496d")},
            {"desktop-text",Hex("#1a2138")},
            {"desktop-muted",Hex("#52617e")},
            {"desktop-field",Hex("#f0f3fa")},
            {"choice-text",Hex("#1a2138")},
            {"grid-alternate",Hex("#f2f5fb")},
            {"grid-text",Hex("#1a2138")},
            {"grid-selection-text",Hex("#1a2138")},
            {"source-text",Hex("#52617e")},
            {"outline",Hex("#ffffff")},
            {"replay",Hex("#f4f6fb")}
        };if(values.Count!=defaults.Count||defaults.Keys.Any(k=>!values.ContainsKey(k)))throw new InvalidOperationException("Incomplete light palette");
            var p=new ThemePalette("demacia","德玛西亚",values);
            p.overlay=Create("demacia-overlay","德玛西亚浮窗",new[]{"#161c2c","#232d45","#1b2337","#4a5878","#ffffff","#c3cbdc","#ffffff","#313d5c","#2b3652"});p.overlay.colors["outline"]=Hex("#060a0e");p.overlay.colors["desktop-text"]=Color.White;p.overlay.colors["desktop-muted"]=Hex("#c3cbdc");return p;
        }
        public static readonly ThemePalette[] All={Piltover,Void,Shadow,Noxus,Demacia};
        public static ThemePalette Find(string id){return All.FirstOrDefault(p=>p.Id==id)??Piltover;}
    }
    public static class ThemeManager {
        static ThemePalette current=ThemePalette.Piltover;
        public static ThemePalette Current {get{return current;}}
        public static event EventHandler Changed;
        public static void Select(string id){var next=ThemePalette.Find(id);if(ReferenceEquals(next,Current))return;current=next;var handler=Changed;if(handler!=null)handler(null,EventArgs.Empty);}
    }
}
