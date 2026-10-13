using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ConfidenceDesktop {
    partial class MainForm {
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
        List<DisplayRow> displayedRows=new List<DisplayRow>();
        object cachedWebRows;

        static DisplayCell Cell(string text,string kind=null,Color color=default(Color),string tooltip=null,bool bold=false,string iconId=null){
            return new DisplayCell{Text=text??"",Kind=kind,Color=color,TooltipNames=tooltip,Bold=bold,IconId=iconId};
        }
        void PresentRows(IEnumerable<DisplayRow> source){
            displayedRows=source.ToList();cachedWebRows=null;
            if(!classicActive)return;
            var classicRows=new List<DataGridViewRow>();
            foreach(var item in displayedRows){
                var row=new DataGridViewRow{Height=grid.RowTemplate.Height};row.CreateCells(grid,item.Cells.Select(c=>(object)c.Text).ToArray());row.Tag=item.Source;
                for(int i=0;i<item.Cells.Length;i++){
                    var cell=item.Cells[i];row.Cells[i].Tag=cell.Kind;
                    if(cell.TooltipNames!=null)row.Cells[i].ToolTipText=EffectText.ForNames(data,cell.Kind,cell.TooltipNames);
                    if(!cell.Color.IsEmpty){row.Cells[i].Style.ForeColor=cell.Color;row.Cells[i].Style.SelectionForeColor=cell.Color;}
                    if(cell.Bold)row.Cells[i].Style.Font=DesktopTheme.Strong;
                }
                classicRows.Add(row);
            }
            grid.Rows.AddRange(classicRows.ToArray());
        }
        IEnumerable<DisplayRow> DataRows(Group group,bool home,bool routes){
            string kind=group.Path.Contains("augments")||group.Path.Contains("augmentCombinations")?"augments":group.Path.EndsWith("/summoners")?"summoner-spells":group.Path.EndsWith("/skills")?"skills:"+SelectedHero.Id:"items";
            bool single=group.Path=="champions/augments/all"||group.Path.StartsWith("champions/augments/stages/");
            var overall=single?(group.Path=="champions/augments/all"?group:groups.FirstOrDefault(g=>g.Path=="champions/augments/all")):null;
            var stages=overall==null?new Dictionary<string,int[]>():overall.Rows.ToDictionary(r=>Json.Str(Json.Get(r.Raw,"id")??Json.Get(r.Raw,"augmentId")),r=>Json.Arr(Json.Get(r.Raw,"availableStages")).Select(v=>(int)Json.Num(v)).Where(v=>v>=1&&v<=4).Distinct().OrderBy(v=>v).ToArray());
            foreach(var row in group.Rows.Where(r=>MatchesRarity(group,r))){
                string id=Json.Str(Json.Get(row.Raw,"id")??Json.Get(row.Raw,"augmentId"));
                yield return new DisplayRow{Source=row,Stages=single?(stages.ContainsKey(id)?stages[id]:new int[0]):null,HeroId=home?data.Heroes.Where(h=>h.Id>0&&h.Name==row.Name).Select(h=>h.Id).FirstOrDefault():0,Cells=new[]{
                    Cell(routes?row.Name+"\n"+row.ShoeAdvice:row.Rank==0?"—":row.Rank.ToString()),
                    Cell(routes?row.StarterName:home?ShortName(row.Name):row.Name,kind,tooltip:routes?row.StarterName:row.Name,iconId:!home&&!routes?Json.Str(Json.Get(row.Raw,"id")??Json.Get(row.Raw,"augmentId")):null),
                    Cell(Percent(row.Lower),color:BadgePaint.RateColor(row.Lower)),
                    Cell(Statistics.GainText(row.GainLower),color:BadgePaint.RateColor(double.IsNaN(row.GainLower)?double.NaN:.5+row.GainLower))
                }};
            }
        }
        IEnumerable<DisplayRow> EquipmentRows(Group group,bool plans,bool synergy){
            foreach(var row in group.Rows.Where(r=>plans||synergy||r.Name==(laterFlow.SelectedItem as string))){
                Color color=synergy?BadgePaint.RateColor(row.Lower):row.ShoeAdvice=="X"?Color.FromArgb(137,156,174):row.ShoeAdvice=="S"?Color.Gold:row.ShoeAdvice=="A"?Color.FromArgb(192,112,255):row.ShoeAdvice=="B"?Color.FromArgb(75,156,255):Color.FromArgb(80,190,110);
                yield return new DisplayRow{Source=row,Cells=new[]{
                    Cell(plans||synergy?row.Name:row.ShoeAdvice,(plans||synergy)&&row.Name!="★常规"?"augments":null,color:plans||synergy?default(Color):color,tooltip:(plans||synergy)&&row.Name!="★常规"?row.Name:null,bold:!plans&&!synergy),
                    Cell(row.StarterName,"items",tooltip:row.StarterName),
                    Cell(plans?"":Percent(row.Lower),color:plans||synergy?color:BadgePaint.RateColor(row.Lower)),Cell(plans||synergy?"":Statistics.GainText(row.GainLower),color:BadgePaint.RateColor(.5+row.GainLower))
                }};
            }
        }
    }
}
