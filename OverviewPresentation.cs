using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
namespace ConfidenceDesktop {
    // Only immutable display input is shared with the interactive DataStore.
    internal sealed class OverviewPresentation {
        readonly DataStore data;readonly List<Group> groups;readonly int heroId;readonly bool advancedMode;readonly ThemePalette palette;
        const double WinLevel=.99,GainLevel=.99;
        internal readonly HashSet<string> webIconAssets=new HashSet<string>();
        internal OverviewPresentation(DataStore data,List<Group> groups,int heroId,bool advanced,ThemePalette palette){this.data=data;this.groups=groups;this.heroId=heroId;advancedMode=advanced;this.palette=palette;}
        Color MainRateColor(double value){return double.IsNaN(value)?palette["muted"]:palette.MainColor(BadgePaint.RateColor(value));}
        object RangeColor(double value){return double.IsNaN(value)?null:ColorTranslator.ToHtml(MainRateColor(value));}
        static object WebNumber(double value){return double.IsNaN(value)||double.IsInfinity(value)?null:(object)value;}
        static string Percent(double value){return double.IsNaN(value)?"—":(value*100).ToString("F1")+"%";}
        static string ShortName(string name){int at=name.LastIndexOf(" · ",StringComparison.Ordinal);return at<0?name:name.Substring(at+3);}
        bool EquipmentLoaded(){return groups.Any(g=>g.Path==RouteRecommendations.GroupPath||g.Path.StartsWith(EquipmentRecommendations.Prefix,StringComparison.Ordinal));}
        sealed class DisplayCell {
            internal string Text,Kind,TooltipNames,IconId;
            internal Color Color;
            internal bool Bold;
        }
        sealed class DisplayRow {
            internal Row Source;
            internal int HeroId;
            internal int[] Stages;
            internal DisplayCell[] Cells;
        }
        static DisplayCell Cell(string text,string kind=null,Color color=default(Color),string tooltip=null,bool bold=false,string iconId=null){
            return new DisplayCell{Text=text??"",Kind=kind,Color=color,TooltipNames=tooltip,Bold=bold,IconId=iconId};
        }
        IEnumerable<DisplayRow> DataRows(Group group,bool home,bool routes,bool filtered=true){
            string kind=group.Path.Contains("augments")||group.Path.Contains("augmentCombinations")?"augments":group.Path.EndsWith("/summoners")?"summoner-spells":group.Path.EndsWith("/skills")?"skills:"+heroId:"items";
            bool single=group.Path=="champions/augments/all"||group.Path.StartsWith("champions/augments/stages/");
            var overall=single?(group.Path=="champions/augments/all"?group:groups.FirstOrDefault(g=>g.Path=="champions/augments/all")):null;
            var stages=overall==null?new Dictionary<string,int[]>():overall.Rows.ToDictionary(r=>Json.Str(Json.Get(r.Raw,"id")??Json.Get(r.Raw,"augmentId")),r=>Json.Arr(Json.Get(r.Raw,"availableStages")).Select(v=>(int)Json.Num(v)).Where(v=>v>=1&&v<=4).Distinct().OrderBy(v=>v).ToArray());
            foreach(var row in group.Rows){
                string id=Json.Str(Json.Get(row.Raw,"id")??Json.Get(row.Raw,"augmentId"));
                yield return new DisplayRow{Source=row,Stages=single?(stages.ContainsKey(id)?stages[id]:new int[0]):null,HeroId=home?data.Heroes.Where(h=>h.Id>0&&h.Name==row.Name).Select(h=>h.Id).FirstOrDefault():0,Cells=new[]{
                    Cell(routes?row.Name+"\n"+row.ShoeAdvice:row.Rank==0?"—":row.Rank.ToString()),
                    Cell(routes?row.StarterName:home?ShortName(row.Name):row.Name,kind,tooltip:routes?row.StarterName:row.Name,iconId:!home&&!routes?Json.Str(Json.Get(row.Raw,"id")??Json.Get(row.Raw,"augmentId")):null),
                    Cell(Percent(row.Lower),color:MainRateColor(row.Lower)),
                    Cell(Statistics.GainText(row.GainLower),color:MainRateColor(double.IsNaN(row.GainLower)?double.NaN:.5+row.GainLower))
                }};
            }
        }
        DisplayCell EquipmentGrade(Row row){return Cell(row.ShoeAdvice,color:palette.MainColor(EquipmentGradeColor(row)),bold:true);}
        Color EquipmentGradeColor(Row row){return row.ShoeAdvice=="X"?palette["source-text"]:row.ShoeAdvice=="S"?Color.Gold:row.ShoeAdvice=="A"?Color.FromArgb(192,112,255):row.ShoeAdvice=="B"?Color.FromArgb(75,156,255):Color.FromArgb(80,190,110);}
        IEnumerable<DisplayRow> EquipmentRows(Group group,bool plans,bool synergy){
            foreach(var row in group.Rows){
                Color color=synergy?MainRateColor(row.Lower):EquipmentGradeColor(row);
                yield return new DisplayRow{Source=row,Cells=new[]{
                    plans||synergy?Cell(row.Name,row.Name!="★常规"?"augments":null,tooltip:row.Name!="★常规"?row.Name:null):EquipmentGrade(row),
                    Cell(row.StarterName,"items",tooltip:row.StarterName),
                    Cell(plans?"":Percent(row.Lower),color:plans||synergy?palette.MainColor(color):MainRateColor(row.Lower)),Cell(plans||synergy?"":Statistics.GainText(row.GainLower),color:MainRateColor(.5+row.GainLower))
                }};
            }
        }
        Dictionary<string,object> StatsAt(Row row){
            if(row==null||row.Raw==null)return null;
            return StatsAt(row.Win,row.Count,row.Pick,Statistics.WinBounds(row.Raw,WinLevel),row.GainBounds(Statistics.GainAlpha(GainLevel)));
        }
        Dictionary<string,object> StatsAt(double win,double count,double pick,Statistics.Interval rate,Statistics.Interval gain){
            return new Dictionary<string,object>{{"win",WebNumber(win)},{"count",WebNumber(count)},{"pick",WebNumber(pick)},{"lower",WebNumber(rate.Lower)},{"upper",WebNumber(rate.Upper)},
                {"gain",WebNumber(gain.Lower)},{"gainUpper",WebNumber(gain.Upper)},{"gainPoint",WebNumber(gain.Point)},{"upperColor",RangeColor(rate.Upper)},{"gainUpperColor",RangeColor(.5+gain.Upper)}};
        }
        static double Stat(Dictionary<string,object> stats,string key){object value;return stats!=null&&stats.TryGetValue(key,out value)&&value is double?(double)value:double.NaN;}
        static double Ordered(double value){return double.IsNaN(value)?double.NegativeInfinity:value;}
        object RateCell(Dictionary<string,object> stats){double v=Stat(stats,"lower");return WebCell(Cell(Statistics.Pct(v),color:MainRateColor(v)));}
        object GainCell(Dictionary<string,object> stats){double v=Stat(stats,"gain");return WebCell(Cell(Statistics.GainText(v),color:MainRateColor(double.IsNaN(v)?double.NaN:.5+v)));}
        sealed class Placed<T>{internal T Item;internal Dictionary<string,object> Stats;internal string Place;}
        List<Placed<T>> ArrangeRows<T>(IEnumerable<T> items,Func<T,Dictionary<string,object>> stats,bool gainFirst){
            var list=items.Select(item=>new Placed<T>{Item=item,Stats=stats(item)}).ToList();
            string key=advancedMode?(gainFirst?"gain":"lower"):(gainFirst?"gainPoint":"win");
            var ordered=advancedMode&&!gainFirst?Statistics.OrderDisplayed(list,x=>Stat(x.Stats,"lower"),x=>Stat(x.Stats,"gain"),x=>Stat(x.Stats,"count")).ToList()
                :list.OrderByDescending(x=>Ordered(Stat(x.Stats,key))).ThenByDescending(x=>Ordered(Stat(x.Stats,advancedMode?"lower":"count"))).ThenByDescending(x=>Ordered(Stat(x.Stats,"count"))).ToList();
            int place=0;foreach(var x in ordered)x.Place=double.IsNaN(Stat(x.Stats,key))?"—":(++place).ToString();
            return ordered;
        }
        object WebCell(DisplayCell cell,DisplayCell grade=null) {
            string text=cell.Text,kind=cell.Kind??"";
   var parts=Regex.Split(text??"",@"( → | \+ |>|\n)").Where(p=>p.Length>0).Select(p=>{
                string name=Regex.Replace(p.Trim(),@"\s*×\s*\d+$","");
                var rich=kind.Length>0?EffectText.Rich(data,kind,name):null;
                foreach(var block in Json.Arr(Json.Get(Json.Get(EffectText.Find(data,kind,name),"tooltip"),"blocks")))foreach(var span in Json.Arr(Json.Get(block,"spans"))){string asset=Json.Str(Json.Get(span,"asset"));if(WebsiteData.IsTooltipAsset(asset))webIconAssets.Add(asset);}
                var part=new Dictionary<string,object>{{"text",p},{"tip",kind.Length>0?EffectText.Single(data,kind,name):""},{"rich",rich}};string icon=WebsiteData.ImageIcon(data,kind,name,cell.IconId);if(icon!=null&&!Regex.IsMatch(p,@"\A[ →+>\s]+\z"))part["icon"]=icon;return part;
            }).ToArray();
            Color color=cell.Color;
            return new {text=text,parts=parts,color=color.IsEmpty?null:ColorTranslator.ToHtml(color),bold=cell.Bold,grade=grade==null?null:WebCell(grade)};
        }
        object BestLater(int slot,string plan){var group=groups.FirstOrDefault(g=>g.Path==EquipmentRecommendations.Prefix+"later/"+slot);
            var candidates=group==null?new object[0]:group.Rows.Where(r=>r.Name==plan).Take(2).Select(r=>WebCell(Cell(r.StarterName,"items",tooltip:r.StarterName),EquipmentGrade(r))).ToArray();
            return new {text="",parts=new object[0],candidates=candidates};}

        internal object Header(){
            var hero=data.Heroes.First(h=>h.Id==heroId);
            var baseline=groups.FirstOrDefault(g=>g.Path=="champions/champion/stats");var row=baseline==null?null:baseline.Rows.FirstOrDefault();
            var board=data.CachedBoardGroups("high").First(g=>g.Path=="champions/rows");
            var placed=ArrangeRows(board.Rows,r=>StatsAt(r.Win,r.Count,r.Pick,Statistics.WinBounds(r.Raw,WinLevel),Statistics.Interval.Empty),false).FirstOrDefault(x=>(int)Json.Num(Json.Get(x.Item.Raw,"id"))==heroId);
            double rate=row==null?double.NaN:Statistics.WinBounds(row.Raw,WinLevel).Lower;
            return new{heroId=heroId,hero=ShortName(hero.Name),title=HeroNames.Title(hero.Name),name=HeroNames.Name(hero.Name),heroIcon=WebsiteData.ImageIcon(data,"champions",id:heroId.ToString()),
                rank=placed==null||placed.Place=="—"?"#—":"#"+placed.Place,rate=row==null?"":Statistics.Pct(rate),heroWin=row==null?null:WebNumber(row.Win),heroCount=row==null?null:WebNumber(row.Count),rateColor=ColorTranslator.ToHtml(MainRateColor(rate))};
        }
        internal object Build(){Func<string,object[]> all=path=>{var g=groups.FirstOrDefault(x=>x.Path==path);return g==null?new object[0]:ArrangeRows(DataRows(g,false,false,false),r=>StatsAt(r.Source),false).Where(x=>x.Place!="—").Select(x=>{var r=x.Item;
                var entry=new Dictionary<string,object>{{"cells",new[]{WebCell(Cell(x.Place)),WebCell(r.Cells[1]),RateCell(x.Stats),GainCell(x.Stats)}},{"stats",x.Stats}};if(r.Stages!=null)entry["stages"]=r.Stages;return (object)entry;}).ToArray();};
            var plans=groups.FirstOrDefault(g=>g.Path==EquipmentRecommendations.Prefix+"plans");
            // Whole lists: each overview card scrolls in place.
            var result=new {augments=all("champions/augments/all"),combinations=all("champions/augmentCombinations"),summoners=all("champions/summoners"),skills=all("champions/skills"),
                plans=plans==null?new object[0]:EquipmentRows(plans,true,false).Select(r=>(object)new {cells=r.Cells.Take(2).Select(c=>WebCell(c)).Concat(new[]{BestLater(5,r.Source.Name),BestLater(6,r.Source.Name)}).ToArray()}).ToArray(),plansPending=!EquipmentLoaded()};
            return result;}
    }
}
