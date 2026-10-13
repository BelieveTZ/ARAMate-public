using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConfidenceDesktop {
 public sealed class CombinationRecommendation {
  public ShopPlan Shop;
  public object Combination;
  public object[] Starter,PurchaseOrder,FinalItems;
  public string[] AugmentIds;
  public string Label,MainAugmentId,Note,FallbackReason,FallbackPlan;
  public double[] Lifts;
  public bool SecondaryChanged;
  public List<EquipmentRecommendations.RatedItem> Fifth,Sixth;
  public string FifthSecondaryPick,SixthSecondaryPick;
  public int MissingPrimaryRoutePicks,MissingNormalRoutePicks;
 }
 public static class CombinationShopBuilds {
  public const int MinItemSamples=100;
  public const double MinSecondaryGain=.01;
  public const double MainGap=.01,Tolerance=.01,MissingLoss=.08;
  sealed class Evidence {
   public double Weight;public Evidence Fallback;
   public readonly string AugmentId;
   readonly Dictionary<string,object> all=new Dictionary<string,object>();
   readonly Dictionary<int,Dictionary<string,object>> slots=new Dictionary<int,Dictionary<string,object>>();
   public Evidence(string id,object raw,int minSamples=MinItemSamples){
    AugmentId=id;
    foreach(var row in Json.Arr(Json.Get(raw,"all")))Add(all,row,minSamples);
    for(int slot=1;slot<=6;slot++){
     var entries=new Dictionary<string,object>();
     foreach(var row in Json.Arr(Json.Get(Json.Get(raw,"slots"),slot.ToString())))Add(entries,row,minSamples);
     slots[slot]=entries;
    }
   }
   static void Add(Dictionary<string,object> target,object row,int minSamples){
    string id=Json.Str(Json.Get(row,"id"));
    if(id.Length==0||Json.Num(Json.Get(row,"sampleCount"),0)<minSamples||double.IsNaN(Statistics.Calculate(row).Lower))return;
    object old;
    if(!target.TryGetValue(id,out old)||Json.Num(Json.Get(row,"sampleCount"))>Json.Num(Json.Get(old,"sampleCount")))target[id]=row;
   }
   public IEnumerable<string> Items(int slot){return slot==0?all.Keys:slots[slot].Keys;}
   public bool TryScore(string id,int slot,out double score){
    object row;score=double.NaN;
    if(slot>0&&slots[slot].TryGetValue(id,out row)){score=Statistics.Calculate(row).Lower;return true;}
    if(all.TryGetValue(id,out row)){score=Statistics.Calculate(row).Lower;return true;}
    return false;
   }
   public double Best(DataStore data,int slot,Func<string,bool> permitted){
    var ids=slot==0?all.Keys:slots[slot].Keys;
    double best=double.NegativeInfinity;
    foreach(string id in ids){double score;if(permitted(id)&&TryScore(id,slot,out score))best=Math.Max(best,score);}
    return best;
   }
  }
  static object Item(string id){return new Dictionary<string,object>{{"id",id}};}
  static string Id(object item){return Json.Str(Json.Get(item,"id"));}
  static bool Ordinary(DataStore data,string id){var item=Item(id);return EquipmentRecommendations.Buyable(data,item)&&!EquipmentRecommendations.RequiresSpecialAccess(data,item);}
  static bool Shoe(DataStore data,string id){return Ordinary(data,id)&&EquipmentRecommendations.IsShoe(data,id);}
  static bool Gear(DataStore data,string id){return Ordinary(data,id)&&!EquipmentRecommendations.IsShoe(data,id);}
  static double Regret(DataStore data,string id,int slot,Evidence[] evidence,Func<string,bool> permitted){
   double sum=0;
   foreach(var source in evidence){
    double best=source.Best(data,slot,permitted),value;
    bool found=source.TryScore(id,slot,out value)&&!double.IsNegativeInfinity(best);
    if(!found&&source.Fallback!=null){int fallbackSlot=slot>0&&source.Fallback.Items(slot).Contains(id)?slot:0;best=source.Fallback.Best(data,fallbackSlot,permitted);found=source.Fallback.TryScore(id,fallbackSlot,out value)&&!double.IsNegativeInfinity(best);}
    sum+=source.Weight*(found?Math.Max(0,best-value):MissingLoss);
   }
   return sum;
  }
  static List<string> Candidates(int slot,Evidence[] evidence,Func<string,bool> permitted,HashSet<string> exclude){
   var candidates=evidence.SelectMany(e=>e.Items(slot)).Distinct().Where(id=>permitted(id)&&!exclude.Contains(id)).ToList();
   if(candidates.Count==0&&slot>0)candidates=evidence.SelectMany(e=>e.Items(0)).Distinct().Where(id=>permitted(id)&&!exclude.Contains(id)).ToList();
   return candidates;
  }
  static string Pick(DataStore data,int slot,Evidence[] evidence,Func<string,bool> permitted,HashSet<string> exclude){
   return Candidates(slot,evidence,permitted,exclude).OrderBy(id=>Regret(data,id,slot,evidence,permitted)).ThenBy(id=>id,StringComparer.Ordinal).FirstOrDefault();
  }
  static double RouteRegret(DataStore data,RouteChoice route,Evidence[] evidence){
   var items=Json.Arr(Json.Get(route.Route,"purchaseOrder"));double score=0;
   for(int i=0;i<items.Length;i++){
    string id=Id(items[i]);score+=Ordinary(data,id)?Regret(data,id,i+1,evidence,x=>Gear(data,x)):MissingLoss;
   }
   return score;
  }
  static RouteChoice ChooseRoute(DataStore data,RouteChoice normal,Dictionary<string,SpecialRoute> specials,string[] pair,Evidence[] evidence){
   var candidates=new List<RouteChoice>();if(normal!=null)candidates.Add(normal);
   foreach(string id in pair){SpecialRoute route;if(specials.TryGetValue(id,out route)&&route.Choice!=null)candidates.Add(route.Choice);}
   var tasks=pair.Where(id=>InferredRoutes.TaskRecipe(data.Lookup("augments",id))).ToArray();
   if(tasks.Length>0){var taskChoices=tasks.Where(specials.ContainsKey).Select(id=>specials[id].Choice).ToList();if(taskChoices.Count>0)candidates=taskChoices;}
   return candidates.Distinct().OrderBy(c=>RouteRegret(data,c,evidence)).ThenByDescending(c=>c.RouteLower).FirstOrDefault();
  }
  static string ItemNamed(DataStore data,string name){
   return data.Resources["items"].Keys.FirstOrDefault(id=>Gear(data,id)&&data.Lookup("items",id)==name);
  }
  static int Occupied(DataStore data,List<object> planned,string[] pair){
   int occupied=planned.Count;
   foreach(string id in pair){string name=data.Lookup("augments",id);if(!InferredRoutes.TaskRecipe(name))continue;
    var required=SpecialRoutes.RequiredItems(name);
    if(required.All(n=>planned.Any(i=>data.Lookup("items",Id(i))==n)))occupied-=required.Length-1;
   }
   return occupied;
  }
  static object[] FinalItems(DataStore data,List<object> planned,string[] pair){
   var keep=Enumerable.Repeat(true,planned.Count).ToArray();
   foreach(string augment in pair){
    string name=data.Lookup("augments",augment);if(!InferredRoutes.TaskRecipe(name))continue;
    var indices=SpecialRoutes.RequiredItems(name).Select(need=>planned.FindIndex(item=>data.Lookup("items",Id(item))==need)).ToArray();
    if(indices.Any(index=>index<0))continue;
    int completion=indices.Max();foreach(int index in indices)if(index!=completion)keep[index]=false;
   }
   return planned.Where((item,index)=>keep[index]).ToArray();
  }
  static List<object> PurchaseOrder(DataStore data,object raw,RouteChoice choice,string[] pair,Evidence[] evidence,SpecialRoute main=null,bool secondary=true){
   var scoringEvidence=secondary?evidence:evidence.Take(1).ToArray();
   var planned=new List<object>();var mandatory=new HashSet<string>();
   foreach(var item in choice.PreFusionRecipe.Concat(Json.Arr(Json.Get(choice.Route,"purchaseOrder"))).Concat((main==null?choice.FollowupCore:new object[0]))){
    string id=Id(item);if(Ordinary(data,id)&&!planned.Any(x=>Id(x)==id))planned.Add(Item(id));
   }
   foreach(string augment in pair){
    string name=data.Lookup("augments",augment);var required=SpecialRoutes.RequiredItems(name);if(required.Length==0)continue;
    if(InferredRoutes.TaskRecipe(name)){
     foreach(string need in required){string id=ItemNamed(data,need);if(id==null)continue;mandatory.Add(id);if(!planned.Any(x=>Id(x)==id))planned.Insert(0,Item(id));}
    }else{
     string existing=planned.Select(Id).FirstOrDefault(id=>required.Contains(data.Lookup("items",id)));
     if(existing!=null){mandatory.Add(existing);continue;}
     var possible=required.Select(need=>ItemNamed(data,need)).Where(id=>id!=null).Distinct().ToArray();
     string selected=possible.OrderBy(id=>Regret(data,id,0,scoringEvidence,x=>Gear(data,x))).FirstOrDefault();
     if(selected!=null){mandatory.Add(selected);planned.Insert(0,Item(selected));}
    }
   }
   while(Occupied(data,planned,pair)>5){int index=planned.FindLastIndex(item=>!mandatory.Contains(Id(item)));if(index<0)break;planned.RemoveAt(index);}
   var existingIds=new HashSet<string>(planned.Select(Id));
   string boot=main==null?Pick(data,0,evidence,id=>Shoe(data,id),existingIds):Id(choice.Boot);
   if(string.IsNullOrEmpty(boot)){string fallback=Id(choice.Boot);if(Shoe(data,fallback))boot=fallback;}
   if(string.IsNullOrEmpty(boot)){var fallback=Json.Arr(Json.Get(Json.Get(Json.Get(raw,"items"),"filtered"),"all")).Where(i=>Shoe(data,Id(i))&&!double.IsNaN(Statistics.Calculate(i).Lower)).OrderByDescending(i=>Statistics.Calculate(i).Lower).FirstOrDefault();boot=Id(fallback);}
   if(boot.Length>0&&!existingIds.Contains(boot)){var shell=Copy(choice);shell.Boot=Item(boot);shell.FollowupCore=new object[0];int position=EquipmentRecommendations.CorePurchaseSequence(data,shell).FindIndex(i=>Id(i)==boot);planned.Insert(Math.Min(planned.Count,Math.Max(0,position)),Item(boot));existingIds.Add(boot);}
   while(Occupied(data,planned,pair)<4){
    int slot=Occupied(data,planned,pair)+1;
    string next=main==null?null:PriorityPick(data,raw,choice,main,slot,evidence,existingIds,secondary);
    if(next==null)next=Pick(data,slot,scoringEvidence,id=>Gear(data,id),existingIds);
    if(next==null){var fallback=EquipmentRecommendations.Ratings(data,raw,slot).FirstOrDefault(r=>r.Grade!="X"&&Gear(data,Id(r.Item))&&!existingIds.Contains(Id(r.Item)));next=fallback==null?null:Id(fallback.Item);}
    if(next==null)break;
    planned.Add(Item(next));existingIds.Add(next);
   }
   return planned;
  }
  static RouteChoice Copy(RouteChoice c){return new RouteChoice{Route=c.Route,Starter=c.Starter,Boot=c.Boot,PreFusionRecipe=c.PreFusionRecipe,FollowupCore=new object[0],ShoeAdvice=c.ShoeAdvice,FusionAugment=c.FusionAugment,IsAugmentRoute=c.IsAugmentRoute,RouteLower=c.RouteLower,ReferenceLabel=c.ReferenceLabel,Flow=c.Flow};}
  static string PriorityPick(DataStore data,object raw,RouteChoice choice,SpecialRoute main,int slot,Evidence[] evidence,HashSet<string> existing,bool secondary){
   List<string> peers;
   if(main.Choice!=null){var ranked=EquipmentRecommendations.Later(data,main.ConditionalData,Copy(choice),slot,existing).Where(r=>r.Grade!="X"&&Gear(data,Id(r.Item))&&!existing.Contains(Id(r.Item))).ToList();peers=ranked.Where(r=>r.Grade=="S").Select(r=>Id(r.Item)).ToList();}
   else{var ranked=evidence[0].Items(slot).Where(id=>Gear(data,id)&&!existing.Contains(id)).Select(id=>{double v;evidence[0].TryScore(id,slot,out v);return new{Id=id,Score=v};}).OrderByDescending(x=>x.Score).ToList();peers=ranked.Where(x=>x.Score>=ranked[0].Score-Tolerance).Select(x=>x.Id).ToList();}
   return secondary?peers.OrderByDescending(id=>{double v;return evidence[1].TryScore(id,slot,out v)?v:double.NegativeInfinity;}).FirstOrDefault():peers.FirstOrDefault();
  }
  sealed class LaterChoices {
   public List<EquipmentRecommendations.RatedItem> Fifth=new List<EquipmentRecommendations.RatedItem>(),Sixth=new List<EquipmentRecommendations.RatedItem>();
   public string FifthPick,SixthPick;
   public int MissingRoute,MissingNormal;
  }
  static LaterChoices Later(DataStore data,object raw,RouteChoice choice,string[] pair,Evidence[] evidence,SpecialRoute main,List<object> order,bool secondary=true){
   var result=new LaterChoices();var existing=new HashSet<string>(order.Select(Id));int occupied=Occupied(data,order,pair);
   for(int slot=5;slot<=6;slot++){
    if(occupied>=slot)continue;
    List<EquipmentRecommendations.RatedItem> candidates;string preferred=null;
    if(main!=null){
     candidates=EquipmentRecommendations.Later(data,main.Choice==null?raw:main.ConditionalData,Copy(choice),slot,existing).Where(r=>r.Grade=="S"||r.Grade=="A"||r.Grade=="B").ToList();
     string pick=PriorityPick(data,raw,choice,main,slot,evidence,existing,secondary);
     int index=candidates.FindIndex(r=>Id(r.Item)==pick);
     if(index>=0){var row=candidates[index];if(secondary)row.Grade="S";candidates.RemoveAt(index);candidates.Insert(0,row);if(secondary)preferred=pick;}
     else if(pick!=null){if(main.Choice==null)result.MissingNormal++;else result.MissingRoute++;}
    }else{
     Func<string,bool> permitted=id=>Gear(data,id);
     var ranked=Candidates(slot,evidence,permitted,existing).Select(id=>new{Id=id,Loss=Regret(data,id,slot,evidence,permitted)}).OrderBy(r=>r.Loss).ThenBy(r=>r.Id,StringComparer.Ordinal).ToList();
     // Regret determines ranking and grades; no joint equipment rate is available.
     candidates=ranked.Select(r=>new EquipmentRecommendations.RatedItem{Item=Item(r.Id),Grade=LaterScoring.Grade(r.Loss-ranked[0].Loss),Lower=double.NaN,Gain=double.NaN}).Where(r=>r.Grade=="S"||r.Grade=="A"||r.Grade=="B").ToList();
    }
    if(slot==5){result.Fifth=candidates;result.FifthPick=preferred;}else{result.Sixth=candidates;result.SixthPick=preferred;}
    if(candidates.Count>0)existing.Add(Id(candidates[0].Item));
   }
   return result;
  }
  static string First(List<EquipmentRecommendations.RatedItem> rows){return rows.Count==0?null:Id(rows[0].Item);}
  public static double Lift(object raw,string id){var row=Json.Arr(Json.Get(Json.Get(raw,"augments"),"all")).FirstOrDefault(x=>Id(x)==id);return row==null?double.NaN:Statistics.Gain(row,Json.Get(Json.Get(raw,"champion"),"stats"));}
  public static int MainIndex(DataStore data,string[] pair,double[] lifts){var tasks=pair.Select(id=>InferredRoutes.TaskRecipe(data.Lookup("augments",id))).ToArray();if(tasks[0]!=tasks[1])return tasks[0]?0:1;if(tasks[0])return -1;bool a=!double.IsNaN(lifts[0]),b=!double.IsNaN(lifts[1]);if(a!=b)return a?0:1;return a&&Math.Abs(lifts[0]-lifts[1])>=MainGap?(lifts[0]>lifts[1]?0:1):-1;}
  public static string[] DisplayOrder(DataStore data,object raw,string[] pair){
   if(pair.Length!=2)return pair.ToArray();
   var lifts=pair.Select(id=>Lift(raw,id)).ToArray();int mainIndex=MainIndex(data,pair,lifts);
   bool reverse=mainIndex==1||(mainIndex<0&&lifts[1]>lifts[0]);
   return reverse?new[]{pair[1],pair[0]}:pair.ToArray();
  }
  public static double SecondaryGain(DataStore data,object raw,object combination){
   var pair=Json.Arr(Json.Get(combination,"augmentIds")).Select(Json.Str).ToArray();
   if(pair.Length!=2)return double.NaN;
   string main=DisplayOrder(data,raw,pair)[0];
   var single=Json.Arr(Json.Get(Json.Get(raw,"augments"),"all")).FirstOrDefault(row=>Json.Str(Json.Get(row,"id")??Json.Get(row,"augmentId"))==main);
   return single==null?double.NaN:Statistics.ComparativeGain(combination,single);
  }
  public static bool IsRecommended(DataStore data,object raw,object combination){return SecondaryGain(data,raw,combination)>=MinSecondaryGain;}
  static string LiftText(double lift){return double.IsNaN(lift)?"缺失":(lift>=0?"+":"")+(lift*100).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture);}
  static RouteChoice FallbackCore(DataStore data,RouteChoice normal,Dictionary<string,SpecialRoute> specials,string[] pair,int mainIndex,out List<object> order,out string label){
   var ids=mainIndex<0?pair:new[]{pair[mainIndex],pair[1-mainIndex]};
   foreach(string id in ids){
    SpecialRoute special;if(!specials.TryGetValue(id,out special)||special.Choice==null)continue;
    order=EquipmentRecommendations.CorePurchaseSequence(data,special.Choice);
    if(order.Any(i=>Gear(data,Id(i)))){label=data.Lookup("augments",id);return special.Choice;}
   }
   if(normal!=null){order=EquipmentRecommendations.CorePurchaseSequence(data,normal);if(order.Any(i=>Gear(data,Id(i)))){label="常规";return normal;}}
   throw new InvalidDataException("组合缺少可用的单海克斯及常规核心："+string.Join("-",pair));
  }
  public static List<CombinationRecommendation> Recommendations(DataStore data,int hero,object raw,RouteChoice normal,IEnumerable<SpecialRoute> specialRoutes){
   var result=new List<CombinationRecommendation>();
   string scope=Json.Str(Json.Get(raw,"_scope"));
   string path=Path.Combine(data.StatsRoot,scope,"champion-details",hero+"-single-augments.json");
   bool hasConditional=File.Exists(path);var conditional=hasConditional?Json.Read(path):null;
   var byId=Json.Arr(Json.Get(conditional,"items")).GroupBy(row=>Json.Str(Json.Get(row,"augmentId"))).ToDictionary(g=>g.Key,g=>g.First());
   var specials=specialRoutes.GroupBy(s=>s.AugmentId).ToDictionary(g=>g.Key,g=>g.First());
   foreach(var combo in Json.Arr(Json.Get(raw,"augmentCombinations"))){
    if(!IsRecommended(data,raw,combo))continue;
    var pair=Json.Arr(Json.Get(combo,"augmentIds")).Select(Json.Str).ToArray();if(pair.Length!=2)throw new InvalidDataException("组合海克斯数量无效："+hero);
    var lifts=pair.Select(id=>Lift(raw,id)).ToArray();int mainIndex=MainIndex(data,pair,lifts);
    var evidence=pair.Select(id=>new Evidence(id,byId.ContainsKey(id)?byId[id]:null){Fallback=new Evidence("",Json.Get(Json.Get(raw,"items"),"filtered"),0)}).ToArray();
    double sum=lifts.Sum(x=>double.IsNaN(x)?0:Math.Max(0,x));for(int i=0;i<2;i++)evidence[i].Weight=sum==0?.5:(double.IsNaN(lifts[i])?0:Math.Max(0,lifts[i]))/sum;
    SpecialRoute main=null;RouteChoice choice;
    if(mainIndex>=0){if(!specials.TryGetValue(pair[mainIndex],out main))main=new SpecialRoute{AugmentId=pair[mainIndex]};choice=main.Choice??normal;evidence=new[]{evidence[mainIndex],evidence[1-mainIndex]};}
    else choice=ChooseRoute(data,normal,specials,pair,evidence);
    string fallbackReason=!hasConditional?"缺少单海克斯装备数据文件":choice==null?"没有可用的推导核心方案":null,fallbackPlan=null;
    var order=fallbackReason==null?PurchaseOrder(data,raw,choice,pair,evidence,main):new List<object>();
    if(fallbackReason==null&&!order.Any(i=>Gear(data,Id(i))))fallbackReason=order.Count==0?"推导购买顺序为空":"推导核心没有成装";
    if(fallbackReason!=null)choice=FallbackCore(data,normal,specials,pair,mainIndex,out order,out fallbackPlan);
    var later=Later(data,raw,choice,pair,evidence,main,order);
    bool changed=mainIndex<0;
    if(mainIndex>=0&&fallbackReason==null){var original=PurchaseOrder(data,raw,choice,pair,evidence,main,false);var originalLater=Later(data,raw,choice,pair,evidence,main,original,false);changed=!order.Select(Id).SequenceEqual(original.Select(Id))||First(later.Fifth)!=First(originalLater.Fifth)||First(later.Sixth)!=First(originalLater.Sixth);}
    string mainName=mainIndex<0?"":data.Lookup("augments",pair[mainIndex]);
    if(fallbackReason!=null&&mainIndex>=0)changed=fallbackPlan!=mainName;
    string note=(mainIndex<0?"两者兼顾":"以"+mainName+"为主")+"（提升 "+LiftText(lifts[mainIndex<0?0:mainIndex])+" 对 "+LiftText(lifts[mainIndex<0?1:1-mainIndex])+" 个百分点）"+(fallbackReason!=null?"；出装同"+fallbackPlan+"方案":mainIndex>=0&&!changed?"；出装同"+mainName+"方案":"")+"。";
    var displayPair=DisplayOrder(data,raw,pair);
    string label=string.Join("＋",displayPair.Select(id=>data.Lookup("augments",id)));
    string key="combo-"+string.Join("-",pair.OrderBy(id=>int.Parse(id)));
    string title=label;
    var blocks=new List<object>(new[]{ShopBuilds.Block("出门装",Json.Arr(Json.Get(choice.Starter,"items"))),ShopBuilds.Block("组合 · 购买顺序",order)}.Where(b=>b!=null));
    ShopBuilds.AddLaterBlocks(blocks,slot=>slot==5?later.Fifth:later.Sixth);
    var set=new Dictionary<string,object>{{"title",title},{"type","custom"},{"map","HA"},{"mode","any"},{"associatedChampions",new[]{hero}},{"associatedMaps",new[]{12}},{"preferredItemSlots",new object[0]},{"sortrank",3},{"startedFrom","blank"},{"uid",ShopBuilds.Uid(hero,key)},{"blocks",blocks.ToArray()}};
    var shop=new ShopPlan{Hero=hero,Key=key,Role=ShopBuilds.CombinationRole,Flow=label,Title=title,Gap=double.NaN,Set=set};
    result.Add(new CombinationRecommendation{Shop=shop,Combination=combo,Starter=Json.Arr(Json.Get(choice.Starter,"items")),PurchaseOrder=order.ToArray(),FinalItems=FinalItems(data,order,pair),AugmentIds=pair,MainAugmentId=mainIndex<0?null:pair[mainIndex],Lifts=lifts,SecondaryChanged=changed,Note=note,FallbackReason=fallbackReason,FallbackPlan=fallbackPlan,Label=string.Join(" + ",displayPair.Select(id=>data.Lookup("augments",id))),Fifth=later.Fifth,Sixth=later.Sixth,FifthSecondaryPick=later.FifthPick,SixthSecondaryPick=later.SixthPick,MissingPrimaryRoutePicks=later.MissingRoute,MissingNormalRoutePicks=later.MissingNormal});
   }
   var ranked=Json.Arr(Json.Get(raw,"augmentCombinations")).Select(combo=>new{
    Row=combo,Plan=new ShopPlan{Key="combo-"+string.Join("-",Json.Arr(Json.Get(combo,"augmentIds")).Select(Json.Str).OrderBy(id=>int.Parse(id))),Set=new Dictionary<string,object>()}
   }).ToList();
   ShopBuilds.RankPlans(ranked.Select(r=>r.Plan),p=>ranked.First(r=>r.Plan==p).Row,1000);
   foreach(var recommendation in result)recommendation.Shop.Set["sortrank"]=ranked.Single(r=>r.Plan.Key==recommendation.Shop.Key).Plan.Set["sortrank"];
   return result;
  }
  public static List<ShopPlan> Build(DataStore data,int hero,object raw,RouteChoice normal,IEnumerable<SpecialRoute> specialRoutes){
   return Recommendations(data,hero,raw,normal,specialRoutes).Select(r=>r.Shop).ToList();
  }
 }
}
