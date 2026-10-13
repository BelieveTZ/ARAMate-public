using System;using System.Linq;using System.Collections.Generic;using System.Text.RegularExpressions;using System.Drawing;using System.Windows.Forms;
namespace ConfidenceDesktop {
 public sealed class EffectRun {public string Text,Kind,Name;public Rectangle Bounds;public int Index;}
 public static class EffectText {
  static readonly Dictionary<string,object> equipmentDescriptions=LoadEquipmentDescriptions();
  static Dictionary<string,object> LoadEquipmentDescriptions(){using(var stream=typeof(EffectText).Assembly.GetManifestResourceStream("EquipmentDescriptions.json"))using(var reader=new System.IO.StreamReader(stream))return Json.Obj(Json.Parse(reader.ReadToEnd()));}

  public static string PlainBlock(object block){
   var spans=Json.Arr(Json.Get(block,"spans")).ToArray();var result=new System.Text.StringBuilder();
   for(int i=0;i<spans.Length;i++){
    var span=spans[i];if(Json.Str(Json.Get(span,"type"))=="text"){result.Append(Json.Str(Json.Get(span,"text")));continue;}
    string label=Json.Str(Json.Get(span,"label"));if(label.Length==0)continue;
    if(label=="goldCoins"){
     label="金币";
     if(i+1<spans.Length&&Json.Str(Json.Get(spans[i+1],"type"))=="text"&&Regex.IsMatch(Json.Str(Json.Get(spans[i+1],"text")),@"^\d+(?:\.\d+)?$")){
      result.Append(Json.Str(Json.Get(spans[++i],"text"))).Append(label);continue;
     }
    }
    if(label=="KiwiAugmentBurstingTeeth")label="牙齿数量";
    string following=string.Concat(spans.Skip(i+1).TakeWhile(t=>Json.Str(Json.Get(t,"type"))=="text").Select(t=>Json.Str(Json.Get(t,"text"))));
    // Icons preceding their written stat name are decorative; scaling icons without a written name retain their meaning.
    if(Regex.IsMatch(following,@"^\s*(?:[+＋-]?\d+(?:\.\d+)?\s*[%％]?\s*)?"+Regex.Escape(label)))continue;
    result.Append(label);
   }
   return result.ToString();
  }
  public static object Find(DataStore data,string kind,string name){
   Dictionary<string,object> resources;
   if(kind.StartsWith("skills:")){object hero;return data.Resources.TryGetValue("champion-skills",out resources)&&resources.TryGetValue(kind.Substring(7),out hero)?Json.Get(hero,name):null;}
   if(!data.Resources.TryGetValue(kind,out resources))return null;
   object match=resources.Values.FirstOrDefault(m=>Json.Str(Json.Get(m,"name"))==name);
   if(match!=null||kind!="augments")return match;
   foreach(string suffix in new[]{"（组合）","（组合参考）","（参考）"}){
    if(!name.EndsWith(suffix,StringComparison.Ordinal))continue;
    string original=name.Substring(0,name.Length-suffix.Length);
    return resources.Values.FirstOrDefault(m=>Json.Str(Json.Get(m,"name"))==original);
   }
   return null;
  }
  public static object Rich(DataStore data,string kind,string name){
   var entry=Find(data,kind,name);var blocks=Json.Arr(Json.Get(Json.Get(entry,"tooltip"),"blocks")).ToArray();if(entry==null||blocks.Length==0)return null;
   if(kind=="augments")name=Json.Str(Json.Get(entry,"name"));
   var meta=new List<string>();if(kind=="items"){double value=Json.Num(Json.Get(entry,"priceTotal"));meta.Add("价格："+(double.IsNaN(value)?"暂无数据":value.ToString("0")+" 金币"));}
   if(kind.StartsWith("skills:")){string skillName=Json.Str(Json.Get(entry,"name"));if(skillName.Length>0)name+=" · "+skillName;foreach(var field in new[]{"cooldown","cost","range"}){var values=Json.Arr(Json.Get(entry,field)).Select(v=>Json.Str(v)).ToArray();if(values.Length>0)meta.Add((field=="cooldown"?"冷却":field=="cost"?"消耗":"范围")+"："+(values.Distinct().Count()==1?values[0]:string.Join(" / ",values)));}}
   return new {title=name,meta=meta,blocks=blocks.Select(block=>{
    var spans=Json.Arr(Json.Get(block,"spans")).ToArray();var runs=new List<object>();
    for(int i=0;i<spans.Length;i++){var span=spans[i];if(Json.Str(Json.Get(span,"type"))=="text"){runs.Add(new {t=Json.Str(Json.Get(span,"text")),s=Json.Str(Json.Get(span,"semantic")),b=Json.Get(span,"bold"),i=Json.Get(span,"italic")});continue;}
     string fallback=IconFallback(spans,i);runs.Add(new {icon=Json.Str(Json.Get(span,"asset")),label=Json.Str(Json.Get(span,"label")),fallback=fallback});
    }
    return new {role=Json.Str(Json.Get(block,"role")),spans=runs};
   }).ToArray()};
  }
  public static string IconFallback(object[] spans,int index){
   string label=Json.Str(Json.Get(spans[index],"label"));if(label=="goldCoins")return "金币";if(label=="KiwiAugmentBurstingTeeth")label="牙齿数量";
   string following=string.Concat(spans.Skip(index+1).TakeWhile(t=>Json.Str(Json.Get(t,"type"))=="text").Select(t=>Json.Str(Json.Get(t,"text"))));
   return Regex.IsMatch(following,@"^\s*(?:[+＋-]?\d+(?:\.\d+)?\s*[%％]?\s*)?"+Regex.Escape(label))?"":label;
  }
  public static string Single(DataStore data,string kind,string name){
   var meta=Find(data,kind,name);if(meta==null)return "";
   if(kind=="augments")name=Json.Str(Json.Get(meta,"name"));
   var lines=Json.Arr(Json.Get(Json.Get(meta,"tooltip"),"blocks")).Select(b=>PlainBlock(b)).Where(t=>!string.IsNullOrWhiteSpace(t));
   string price="";if(kind=="items"){double value=Json.Num(Json.Get(meta,"priceTotal"));price="\n价格："+(double.IsNaN(value)?"暂无数据":value.ToString("0")+" 金币");}
   if(kind.StartsWith("skills:")){price=" · "+Json.Str(Json.Get(meta,"name"));foreach(var field in new[]{"cooldown","cost","range"}){var values=Json.Arr(Json.Get(meta,field)).Select(v=>Json.Str(v)).ToArray();if(values.Length>0)price+="\n"+(field=="cooldown"?"冷却":field=="cost"?"消耗":"范围")+"："+(values.Distinct().Count()==1?values[0]:string.Join(" / ",values));}}
   string text=string.Join("\n",lines);object fixedText;if(kind=="items"&&equipmentDescriptions.TryGetValue(name,out fixedText))text=Json.Str(fixedText);return name+price+(text.Length==0?"":"\n"+text);
  }
  public static string ForNames(DataStore data,string kind,string names){return string.Join("\n\n",(names??"").Split(new[]{" → "," + ",">","\n"},StringSplitOptions.RemoveEmptyEntries).Select(s=>Regex.Replace(s.Trim(),@"\s*×\s*\d+$","")).Distinct().Select(n=>Single(data,kind,n)).Where(s=>s.Length>0));}
  public static List<EffectRun> Layout(DataStore data,string text,Font font,Rectangle bounds,string preferredKind=null){
   var runs=new List<EffectRun>();int x=4,y=0;int lineHeight=TextRenderer.MeasureText("装备",font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Height;int available=Math.Max(1,bounds.Width-8),index=0;
   foreach(string part in Regex.Split(text??"",@"( → | \+ |>|\n)")){
    if(part.Length==0)continue;if(part=="\n"){x=4;y+=lineHeight;continue;}
    int width=TextRenderer.MeasureText(part,font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix).Width;
    if(x>4&&x+width>available+4){x=4;y+=lineHeight;}
    string name=Regex.Replace(part.Trim(),@"\s*×\s*\d+$","");string kind=!string.IsNullOrEmpty(preferredKind)?(Find(data,preferredKind,name)!=null?preferredKind:""):Find(data,"items",name)!=null?"items":Find(data,"augments",name)!=null?"augments":Find(data,"summoner-spells",name)!=null?"summoner-spells":"";
    runs.Add(new EffectRun{Text=part,Name=name,Kind=kind,Index=index++,Bounds=new Rectangle(x,y,Math.Min(width,available),lineHeight)});x+=width;
   }
   int top=Math.Max(2,(bounds.Height-(y+lineHeight))/2);foreach(var run in runs){var r=run.Bounds;r.Offset(bounds.X,bounds.Y+top);run.Bounds=r;}return runs;
  }
 }
}
