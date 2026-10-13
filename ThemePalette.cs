using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
namespace ConfidenceDesktop {
    // This palette is also the source of the generated web CSS variables.
    public sealed class ThemePalette {
        public readonly string Id, Name;
        readonly Dictionary<string,Color> colors;
        ThemePalette(string id,string name,Dictionary<string,Color> values){Id=id;Name=name;colors=values;}
        public Color this[string key]{get{return colors[key];}}
        static Color Hex(string value){return ColorTranslator.FromHtml(value);}
        public Dictionary<string,string> Css(){var result=colors.ToDictionary(p=>p.Key,p=>"#"+p.Value.R.ToString("x2")+p.Value.G.ToString("x2")+p.Value.B.ToString("x2"));var muted=colors["muted"];result["crumb"]="rgba("+muted.R+","+muted.G+","+muted.B+",.45)";return result;}
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
        public static readonly ThemePalette Piltover=Create("piltover","皮尔特沃夫蓝",null);
        public static readonly ThemePalette Void=Create("void","虚空紫",new[]{"#120e1c","#1b1529","#150f22","#3a2f52","#ece6f5","#a69bbd","#e48fe8","#2e1f38","#251c32"});
        public static readonly ThemePalette Shadow=Create("shadow","暗影岛黑",new[]{"#111314","#1a1d1e","#141617","#303638","#e6ebea","#9aa5a3","#3ddc97","#133027","#1f2725"});
        public static readonly ThemePalette[] All={Piltover,Void,Shadow};
        public static ThemePalette Find(string id){return All.FirstOrDefault(p=>p.Id==id)??Piltover;}
    }
    public static class ThemeManager {
        static ThemePalette current=ThemePalette.Piltover;
        public static ThemePalette Current {get{return current;}}
        public static event EventHandler Changed;
        public static void Select(string id){var next=ThemePalette.Find(id);if(ReferenceEquals(next,Current))return;current=next;var handler=Changed;if(handler!=null)handler(null,EventArgs.Empty);}
    }
}
