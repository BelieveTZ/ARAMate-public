using System;using System.Collections.Generic;using System.Linq;
namespace ConfidenceDesktop {
 public static class EquipmentRecommendations {
  public const string Prefix="champions/equipment/";
  public static bool Buyable(DataStore d,object item){object meta;return d.Resources["items"].TryGetValue(Json.Str(Json.Get(item,"id")),out meta)&&object.Equals(Json.Get(meta,"purchasable"),true)&&object.Equals(Json.Get(meta,"isFinal"),true);}
  public static bool RequiresSpecialAccess(DataStore d,object item){
   object meta;if(!d.Resources["items"].TryGetValue(Json.Str(Json.Get(item,"id")),out meta))return false;
   string name=Json.Str(Json.Get(meta,"name"));
   return Json.Str(Json.Get(meta,"acquisition"))=="conditional"||new[]{"终极九头蛇","金铲铲","虚空献祭","沃格勒特的巫师帽","嘉文一世之靴"}.Contains(name);
  }
  static List<RatedItem> RateAvailable(DataStore d,IEnumerable<RatedItem> candidates){
   var rows=candidates.OrderBy(r=>RequiresSpecialAccess(d,r.Item)).ThenByDescending(r=>r.Lower).ThenByDescending(r=>Json.Num(Json.Get(r.Item,"sampleCount"))).ToList();
   var best=rows.FirstOrDefault(r=>!RequiresSpecialAccess(d,r.Item));
   foreach(var row in rows)row.Grade=RequiresSpecialAccess(d,row.Item)?"X":Grade(best.Lower-row.Lower);
   return rows;
  }
  // Later uses Lower for the predicted rate and Gain for estimated improvement;
  // legacy Ratings retains the statistical lower bound in Lower.
  public sealed class RatedItem {public object Item;public string Grade;public double Lower,Gain,EvidenceCount;public bool UsesOverallData,UsesHeroData,HasRouteEvidence;}
  public static string Grade(double gap){return gap<=.02?"S":gap<=.06?"A":gap<=.10?"B":"C";}
  public static List<RatedItem> Ratings(DataStore d,object raw,int slot=0){
   var filtered=Json.Get(Json.Get(raw,"items"),"filtered");var items=Json.Arr(slot==0?Json.Get(filtered,"all"):Json.Get(Json.Get(filtered,"slots"),slot.ToString())).Where(i=>Buyable(d,i)&&!double.IsNaN(Statistics.Calculate(i).Lower)).OrderByDescending(i=>Statistics.Calculate(i).Lower).ThenByDescending(i=>Json.Num(Json.Get(i,"sampleCount"))).ToList();
   return RateAvailable(d,items.Select(i=>new RatedItem{Item=i,Lower=Statistics.Calculate(i).Lower}));
  }
  public static HashSet<string> Existing(RouteChoice c){return c==null?new HashSet<string>():new HashSet<string>(c.PreFusionRecipe.Concat(Json.Arr(Json.Get(c.Route,"purchaseOrder"))).Concat(c.FollowupCore).Concat(Json.Arr(Json.Get(c.Starter,"items"))).Concat(c.Boot==null?new object[0]:new[]{c.Boot}).Select(i=>Json.Str(Json.Get(i,"id"))));}
  public static bool IsShoe(DataStore d,string id){object meta;return d.Resources["items"].TryGetValue(id,out meta)&&Json.Arr(Json.Get(meta,"categories")).Any(x=>Json.Str(x)=="Boots");}
  public static List<RatedItem> Later(DataStore d,object raw,RouteChoice c,int slot=0,IEnumerable<string> additionalExisting=null){
   var existing=Existing(c);if(additionalExisting!=null)existing.UnionWith(additionalExisting);bool hasShoes=existing.Any(id=>IsShoe(d,id));
   Func<RatedItem,bool> allowed=r=>{string id=Json.Str(Json.Get(r.Item,"id"));return d.Lookup("items",id)!=FusionProduct(c)&&!existing.Contains(id)&&(!hasShoes||!IsShoe(d,id));};
   object sourceRaw=raw;int sourceSlot=slot;bool usesOverallData=false,usesHeroData=false;
   var available=Ratings(d,raw,slot).Where(allowed).ToList();
   if(slot>0&&c!=null&&(c.IsAugmentRoute||InferredRoutes.TaskRecipe(c.FusionAugment))&&!available.Any(r=>!RequiresSpecialAccess(d,r.Item))){
    var overall=Ratings(d,raw).Where(allowed).ToList();
    if(overall.Any(r=>!RequiresSpecialAccess(d,r.Item))){foreach(var row in overall)row.UsesOverallData=true;available=overall;sourceSlot=0;usesOverallData=true;}
    else if(Json.Get(raw,"_fallbackItems")!=null){
     var heroContext=new Dictionary<string,object>{{"items",new Dictionary<string,object>{{"filtered",Json.Get(raw,"_fallbackItems")}}}};
     var heroItems=Ratings(d,heroContext,slot).Where(allowed).ToList();bool usesOverall=false;
     if(!heroItems.Any(r=>!RequiresSpecialAccess(d,r.Item))){heroItems=Ratings(d,heroContext).Where(allowed).ToList();usesOverall=true;}
     if(heroItems.Any(r=>!RequiresSpecialAccess(d,r.Item))){foreach(var row in heroItems){row.UsesHeroData=true;row.UsesOverallData=usesOverall;}available=heroItems;sourceRaw=heroContext;sourceSlot=usesOverall?0:slot;usesOverallData=usesOverall;usesHeroData=true;}
    }
   }
   var filtered=Json.Get(Json.Get(sourceRaw,"items"),"filtered");
   var source=Json.Arr(sourceSlot==0?Json.Get(filtered,"all"):Json.Get(Json.Get(filtered,"slots"),sourceSlot.ToString()));
   return LaterScoring.Score(d,raw,c,slot,source,item=>allowed(new RatedItem{Item=item}),usesOverallData,usesHeroData);
  }
  public static int FirstLaterSlot(DataStore d,RouteChoice c){
   if(c==null)return 4;
   var sequence=CoreSequence(c);bool shoes=Existing(c).Any(id=>IsShoe(d,id));
   var names=sequence.Select(i=>d.Lookup("items",Json.Get(i,"id"))).ToArray();
   var recipe=InferredRoutes.TaskRecipe(c.FusionAugment)?SpecialRoutes.RequiredItems(c.FusionAugment):new string[0];
   int occupied=sequence.Count(i=>!IsShoe(d,Json.Str(Json.Get(i,"id"))))+(shoes?1:0);
   if(recipe.Length>0&&recipe.All(names.Contains))occupied-=recipe.Length-1;
   return Math.Min(7,occupied+1);
  }
  public static List<RatedItem> RecommendedLater(DataStore d,object raw,RouteChoice c,int slot){
   int first=FirstLaterSlot(d,c);if(slot<first)return new List<RatedItem>();
   var selected=new HashSet<string>();
   for(int position=first;position<=slot;position++){
    var candidates=Later(d,raw,c,position,selected);
    var best=candidates.FirstOrDefault(r=>r.Grade!="X");
    if(best==null)return new List<RatedItem>();
    if(position==slot)return candidates.Where(r=>r.Grade=="S"||r.Grade=="A"||r.Grade=="B").ToList();
    selected.Add(Json.Str(Json.Get(best.Item,"id")));
   }
   return new List<RatedItem>();
  }
  public static string FusionProduct(RouteChoice c){
   if(c==null)return null;
   switch(c.FusionAugment){case "终极九头蛇":return "终极九头蛇";case "沃格勒特的巫师帽":return "沃格勒特的巫师帽";case "艾卡西亚的陷落":return "虚空献祭";default:return null;}
  }
  public static void CompleteFusionCore(DataStore d,object raw,RouteChoice c){
   if(c==null||string.IsNullOrEmpty(c.FusionAugment))return;
   CompleteCore(d,raw,c);
  }
  public static void CompleteCore(DataStore d,object raw,RouteChoice c){
   if(c==null)return;
   while(FirstLaterSlot(d,c)<5){
    int slot=FirstLaterSlot(d,c);var best=Later(d,raw,c,slot).FirstOrDefault(r=>r.Grade!="X");
    if(best==null){c.ShoeNote+=" 后续第"+slot+"件暂无有效候选，未自动补齐。";break;}
    c.FollowupCore=c.FollowupCore.Concat(new[]{best.Item}).ToArray();
    if(best.UsesOverallData||best.UsesHeroData)c.ShoeNote+=" 第"+slot+"件使用"+(best.UsesHeroData?"同英雄": "同海克斯")+(best.UsesOverallData?"总体单件":"该位置单件")+"数据评分补齐。";
   }
   c.ShoeNote+=" 核心出装按各位置最高有效装备评分补足至四个成装位，排除合成品及已列装备；不代表完整路线联合胜率。";
  }
  public static string Flow(DataStore d,string key){return d.Lookup("archetypes",key);}
  public static List<RouteChoice> Choices(DataStore d,object raw,double maxGap=.03){
   var choices=RouteRecommendations.Build(d,raw).GroupBy(c=>c.Flow).Select(g=>g.OrderByDescending(c=>c.RouteLower).ThenByDescending(c=>Json.Num(Json.Get(c.Route,"sampleCount"))).First()).ToList();
   if(choices.Count==0){var fallback=RouteRecommendations.Build(d,raw,false).OrderByDescending(c=>c.RouteLower).ThenByDescending(c=>Json.Num(Json.Get(c.Route,"sampleCount"))).FirstOrDefault();if(fallback!=null){fallback.IsFallback=true;CompleteCore(d,raw,fallback);return new List<RouteChoice>{fallback};}}
   var flows=Json.Arr(Json.Get(Json.Get(Json.Get(raw,"builds"),"filtered"),"archetypes")).Where(f=>choices.Any(c=>c.Flow==Json.Str(Json.Get(f,"key")))).OrderByDescending(f=>Statistics.Calculate(f).Lower).ToList();
   if(flows.Count==0)return new List<RouteChoice>();var best=flows[0];
   var allowed=flows.Where(f=>ReferenceEquals(f,best)||Statistics.GainBound(best,true)-Statistics.GainBound(f,false)<=maxGap).Take(1);
   var result=new List<RouteChoice>();var seen=new HashSet<string>();foreach(var f in allowed){var c=choices.First(x=>x.Flow==Json.Str(Json.Get(f,"key")));string key=string.Join(",",Json.Arr(Json.Get(c.Route,"purchaseOrder")).Select(i=>Json.Str(Json.Get(i,"id"))).OrderBy(i=>i));if(seen.Add(key)){CompleteCore(d,raw,c);result.Add(c);}}return result;
  }
  public static List<object> CoreSequence(RouteChoice c){
   var core=Json.Arr(Json.Get(c.Route,"purchaseOrder")).ToList();string boot=Json.Str(Json.Get(c.Boot,"id"));
   bool already=Json.Arr(Json.Get(c.Starter,"items")).Concat(core).Any(i=>Json.Str(Json.Get(i,"id"))==boot);
   if(c.Boot!=null&&!already){var match=System.Text.RegularExpressions.Regex.Match(c.ShoeAdvice??"",@"建议第\s*(\d)");int position=match.Success?int.Parse(match.Groups[1].Value):2;core.Insert(Math.Min(core.Count,Math.Max(0,position-1)),c.Boot);}
   core.InsertRange(0,c.PreFusionRecipe);
   core.AddRange(c.FollowupCore);
   return core;
  }
  public static List<object> CorePurchaseSequence(DataStore d,RouteChoice c){
   return c==null?new List<object>():CoreSequence(c);
  }
  public static string PurchaseLine(DataStore d,RouteChoice c){return c.StarterName+" → "+string.Join(" → ",CorePurchaseSequence(d,c).Select(i=>d.Lookup("items",Json.Get(i,"id"))));}
  static Row Make(string name,string details,object raw,object baseline,string note){var score=Statistics.Calculate(raw);return new Row{Name=name,StarterName=details,Raw=Json.Obj(raw),Lower=score.Lower,Count=score.N,Win=score.P,GainLower=Statistics.ComparativeGain(raw,baseline),Note=note};}
  public static List<Group> Build(DataStore d,object raw){
   var hero=Json.Get(Json.Get(raw,"champion"),"stats");var choices=Choices(d,raw);var groups=new List<Group>();
   groups.Add(new Group{Path=Prefix+"plans",Title="核心出装",Rows=choices.Select((c,i)=>Make("★常规",PurchaseLine(d,c),c.Starter,hero,(c.IsFallback?"严格筛选无合格路线，改用有效路线与对应出门装中统计学胜率最高的参考方案；不代表相对英雄总体有正收益。\n":"")+"该序列结合核心路线、出门装与鞋子位置建议，没有真实的完整序列胜率。\n"+c.ShoeAdvice+"\n"+c.ShoeNote)).ToList()});
   var specialRoutes=SpecialRoutes.Build(d,raw,choices.FirstOrDefault());
   var synergy=specialRoutes.Select(s=>{
    string note;
    if(!string.IsNullOrEmpty(s.Choice.ReferenceLabel))note=s.Choice.ShoeNote;
    else if(double.IsNaN(s.Improvement)||s.Improvement<=0)note="仅在已选该海克斯时参考：该海克斯条件下的最佳有效路线，不代表已证明优于常规路线。";
    else note="仅在已选该海克斯时参考。海克斯统计学胜率提升非负；在相同海克斯条件下，此核心路线的统计学胜率高于默认购买顺序。\n默认路线下界："+Statistics.Pct(Statistics.Calculate(s.Baseline).Lower)+"；专用核心路线下界："+Statistics.Pct(s.Choice.RouteLower)+"；路线样本："+Json.Str(Json.Get(s.Choice.Route,"sampleCount"))+"。\n这是两个路线评分的比较，不是换装收益的置信下界。";
    if(string.IsNullOrEmpty(s.Choice.ReferenceLabel)&&!string.IsNullOrEmpty(s.Choice.ShoeNote))note+="\n"+s.Choice.ShoeNote;
    return new Row{Name=SpecialRoutes.Label(d,s),StarterName=PurchaseLine(d,s.Choice),Raw=Json.Obj(s.Choice.Route),Count=Json.Num(Json.Get(s.Choice.Route,"sampleCount")),Lower=s.Choice.RouteLower,Note=note};
   }).ToList();
   groups[0].Rows.AddRange(synergy);
   var comboPlans=CombinationShopBuilds.Recommendations(d,(int)Json.Num(Json.Get(Json.Get(raw,"champion"),"id"),0),raw,choices.FirstOrDefault(),specialRoutes);
   const string comboNote="这组海克斯的胜率来自组合对局；装备顺序由两项单海克斯数据合成，未有两项同时持有时的完整出装胜率。任务材料按合成后占用的背包格数计算。";
   groups[0].Rows.AddRange(comboPlans.Select(combo=>{
    var score=Statistics.Calculate(combo.Combination);
    return new Row{Name=combo.Label,StarterName=RouteRecommendations.Items(d,combo.Starter)+" → "+string.Join(" → ",combo.PurchaseOrder.Select(item=>d.Lookup("items",Json.Get(item,"id")))),Raw=Json.Obj(combo.Combination),Count=score.N,Win=score.P,Lower=score.Lower,Note=combo.Note+comboNote};
   }));
   var contexts=choices.Count==0?new List<RouteChoice>{null}:choices.ToList();contexts.AddRange(specialRoutes.Select(s=>s.Choice));
   for(int slot=5;slot<=6;slot++){
    var laterRows=new List<Row>();
    foreach(var c in contexts){
     var special=specialRoutes.FirstOrDefault(s=>ReferenceEquals(s.Choice,c));
     string label=special!=null?SpecialRoutes.Label(d,special):"★常规";
     laterRows.AddRange(RecommendedLater(d,special==null?raw:special.ConditionalData,c,slot).Select(r=>new Row{
      Name=label,StarterName=d.Lookup("items",Json.Get(r.Item,"id")),ShoeAdvice=r.Grade,Raw=Json.Obj(r.Item),Lower=r.Lower,GainLower=r.Gain,Count=r.EvidenceCount,
      Note=(r.HasRouteEvidence?"按位置榜与本核心后续成装综合估计":"仅位置榜估计")+"（经验收缩）；评级为与第一名预计胜率差距 ≤1/2/3 个百分点的 S/A/B。第六件已排除第五件的最佳候选；已排除已有装备、鞋类冲突与合成奖励，不代表整套出装的联合胜率。"
     }));
    }
    foreach(var combo in comboPlans){
     var candidates=slot==5?combo.Fifth:combo.Sixth;
     laterRows.AddRange(candidates.Select(r=>new Row{
      Name=combo.Label,StarterName=d.Lookup("items",Json.Get(r.Item,"id")),ShoeAdvice=r.Grade,Raw=Json.Obj(r.Item),Lower=r.Lower,GainLower=r.Gain,Count=r.EvidenceCount,
      Note=combo.MainAugmentId==null?"按两个海克斯的数据综合评级，无组合预计胜率。":"按"+d.Lookup("augments",combo.MainAugmentId)+"的数据评级。"
     }));
    }
    groups.Add(new Group{Path=Prefix+"later/"+slot,Title="第"+slot+"件",RateTitle="预计胜率",GainTitle="预计提升",Rows=laterRows});
   }
   var augmentRows=Json.Arr(Json.Get(Json.Get(raw,"augments"),"all")).ToDictionary(r=>Json.Str(Json.Get(r,"id")));
   var labels=choices.Select(c=>"★常规")
    .Concat(ShopBuilds.OrderPlans(specialRoutes,s=>augmentRows[s.AugmentId],s=>"augment-"+s.AugmentId).Select(s=>SpecialRoutes.Label(d,s)))
    .Concat(ShopBuilds.OrderPlans(comboPlans,c=>c.Combination,c=>c.Shop.Key).Select(c=>c.Label))
    .Distinct().Select((name,index)=>new {name,index}).ToDictionary(p=>p.name,p=>p.index);
   // Reorder only the display rows after all recommendation calculations are complete.
   foreach(var group in groups)group.Rows=group.Rows.OrderBy(r=>labels.ContainsKey(r.Name)?labels[r.Name]:int.MaxValue).ToList();
   return groups;
  }
 }
}



