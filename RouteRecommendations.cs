using System;
using System.Collections.Generic;
using System.Linq;
namespace ConfidenceDesktop {
 public sealed class RouteChoice {
  public string Flow,Name,StarterName,ShoeAdvice,ShoeNote,ReferenceLabel,FusionAugment;public bool IsFallback,IsAugmentRoute;
  public object Route,Starter,Boot;
  public object[] PreFusionRecipe=new object[0];
  public object[] FollowupCore=new object[0];
  public int ShoePositionAfterFusion;
  public double Lower,Gain,RouteLower,RouteGain,Count;
 }
 public static class RouteRecommendations {
  public const string GroupPath="champions/recommended-starters";
  static IEnumerable<object> Ranked(object value){return Json.Arr(value).Where(x=>!double.IsNaN(Statistics.Calculate(x).Lower)).OrderByDescending(x=>Statistics.Calculate(x).Lower).ThenByDescending(x=>Json.Num(Json.Get(x,"sampleCount")));}
  static string Key(object value,string field){return string.Join(",",Json.Arr(Json.Get(value,field)).Select(i=>Json.Str(Json.Get(i,"id"))));}
  public static bool ValidItem(DataStore data,object item){string id=Json.Str(Json.Get(item,"id"));Dictionary<string,object> resources;return id!="0"&&data.Resources.TryGetValue("items",out resources)&&resources.ContainsKey(id);}
  public static string Items(DataStore data,object value){return string.Join(" + ",Json.Arr(value).GroupBy(i=>Json.Str(Json.Get(i,"id"))).Select(g=>data.Lookup("items",g.Key)+(g.Count()>1?" ×"+g.Count():"")));}
  public static List<RouteChoice> Build(DataStore data,object raw){return Build(data,raw,true);}
  public static List<RouteChoice> Build(DataStore data,object raw,bool strict){
   var hero=Json.Get(Json.Get(raw,"champion"),"stats");var candidates=new List<RouteChoice>();
   foreach(var flow in Json.Arr(Json.Get(Json.Get(Json.Get(raw,"builds"),"filtered"),"archetypes")))foreach(var profile in Json.Arr(Json.Get(flow,"profiles")))foreach(var route in Json.Arr(Json.Get(profile,"routes"))){
    var order=Json.Arr(Json.Get(route,"purchaseOrder"));if(order.Length!=3||order.Any(i=>!ValidItem(data,i))||!order.Select(i=>Json.Str(Json.Get(i,"id"))).OrderBy(i=>i).SequenceEqual(Json.Arr(Json.Get(profile,"itemSet")).Select(i=>Json.Str(Json.Get(i,"id"))).OrderBy(i=>i)))continue;
    var score=Statistics.Calculate(route);if(double.IsNaN(score.Lower))continue;
    candidates.Add(new RouteChoice{Flow=Json.Str(Json.Get(flow,"key")),Route=route,RouteLower=score.Lower,RouteGain=Statistics.Gain(route,hero)});
   }
   if(candidates.Count==0)return candidates;double best=candidates.Max(c=>c.RouteLower);var result=new List<RouteChoice>();
   foreach(var c in candidates.Where(c=>!strict||(c.RouteLower>=best-.05&&c.RouteGain>=0))){
    c.Starter=BestStarter(data,c.Route);if(c.Starter==null)continue;
    var score=Statistics.Calculate(c.Starter);c.Lower=score.Lower;c.Count=score.N;c.Gain=Statistics.Gain(c.Starter,hero);
    c.Name=string.Join(" → ",Json.Arr(Json.Get(c.Route,"purchaseOrder")).Select(i=>data.Lookup("items",Json.Get(i,"id"))));c.StarterName=Items(data,Json.Get(c.Starter,"items"));
    c.Boot=Ranked(Json.Get(c.Route,"boots")).Select(b=>Json.Get(b,"item")).FirstOrDefault(i=>ValidItem(data,i)&&IsBoot(data,i));
    c.ShoeAdvice=JointShoeTiming(data,raw,c.Route,c.Boot,c.Starter,out c.ShoeNote);result.Add(c);
   }
   return result.GroupBy(c=>Key(c.Route,"purchaseOrder")).Select(g=>g.OrderByDescending(c=>Json.Num(Json.Get(c.Route,"sampleCount"))).First()).OrderByDescending(c=>c.RouteLower).ThenByDescending(c=>Json.Num(Json.Get(c.Route,"sampleCount"))).ToList();
  }
  public static object BestStarter(DataStore data,object route){
   var valid=Ranked(Json.Get(route,"starters")).Where(s=>Json.Arr(Json.Get(s,"items")).Length>0&&Json.Arr(Json.Get(s,"items")).All(i=>ValidItem(data,i))).ToList();
   if(valid.Count==0)return null;
   double floor=Math.Max(200,valid.Max(s=>Json.Num(Json.Get(s,"sampleCount")))*.1);
   return valid.FirstOrDefault(s=>Json.Num(Json.Get(s,"sampleCount"))>=floor)??valid.OrderByDescending(s=>Json.Num(Json.Get(s,"sampleCount"))).First();
  }
  public static bool IsBoot(DataStore data,object item){Dictionary<string,object> resources;object meta;return data.Resources.TryGetValue("items",out resources)&&resources.TryGetValue(Json.Str(Json.Get(item,"id")),out meta)&&Json.Arr(Json.Get(meta,"categories")).Any(c=>Json.Str(c)=="Boots")&&object.Equals(Json.Get(meta,"isFinal"),true)&&object.Equals(Json.Get(meta,"purchasable"),true)&&!EquipmentRecommendations.RequiresSpecialAccess(data,item);}
  public static string JointShoeTiming(DataStore data,object raw,object route,object boot,object starter,out string note,bool conditional=false){
   note=conditional?"鞋子位置比较指定海克斯下鞋子与被推迟核心装备的槽位表现；这是顺序评分，不是整套胜率。":"鞋子位置综合比较鞋子与被推迟核心装备的总体槽位表现变化；这是顺序评分，不是整套胜率。";
   if(boot==null)return "鞋子：暂无路线内有效样本";string name=data.Lookup("items",Json.Get(boot,"id"));
   var core=Json.Arr(Json.Get(route,"purchaseOrder"));string bid=Json.Str(Json.Get(boot,"id"));
   if(Json.Arr(Json.Get(starter,"items")).Concat(core).Any(i=>Json.Str(Json.Get(i,"id"))==bid))return name+"：已包含在出装中";
   var slots=Json.Get(Json.Get(Json.Get(raw,"items"),"filtered"),"slots");
   Func<object,int,double> value=(item,slot)=>{string id=Json.Str(Json.Get(item,"id"));var records=Enumerable.Range(1,6).Select(n=>Json.Arr(Json.Get(slots,n.ToString())).FirstOrDefault(r=>Json.Str(Json.Get(r,"id"))==id)).ToArray();double max=records.Max(r=>Json.Num(Json.Get(r,"sampleCount"),0));var score=Statistics.Calculate(records[slot-1]);return score.N>=(conditional?100:Math.Max(200,max*.1))?score.Lower:double.NaN;};
   int best=0;double bestScore=double.NegativeInfinity;
   for(int position=1;position<=(conditional?4:3);position++){double score=value(boot,position);for(int i=0;i<core.Length;i++){int original=i+1,actual=original>=position?original+1:original;if(actual!=original)score+=value(core[i],actual)-value(core[i],original);}if(!double.IsNaN(score)&&score>bestScore){bestScore=score;best=position;}}
   if(best==0){note+="可比槽位样本不足，暂用第一件核心之后作为默认位置，不代表最佳时点。";best=2;}
   return name+"：建议第 "+best+" 件（综合位置参考）";
  }
  // The fused Hydra occupies slot 1; materials are purchases, not three final slots.
  public static List<object> ConfigureHydraShoes(DataStore data,object raw,RouteChoice choice,IEnumerable<object> purchases){
   var recipe=new HashSet<string>(SpecialRoutes.RequiredItems("终极九头蛇"));
   var order=EquipmentRecommendations.OrderTaskPurchases(data,purchases,new[]{"终极九头蛇"});
   var core=order.Where(i=>!recipe.Contains(data.Lookup("items",Json.Get(i,"id")))&&!EquipmentRecommendations.IsShoe(data,Json.Str(Json.Get(i,"id")))).ToArray();
   var routeBoots=Json.Arr(Json.Get(choice.Route,"boots")).Where(b=>Json.Num(Json.Get(b,"sampleCount"))>=100&&IsBoot(data,Json.Get(b,"item"))&&!double.IsNaN(Statistics.Calculate(b).Lower)).OrderByDescending(b=>Statistics.Calculate(b).Lower).ThenByDescending(b=>Json.Num(Json.Get(b,"sampleCount"))).Select(b=>Json.Get(b,"item")).ToArray();
   var filtered=Json.Get(Json.Get(raw,"items"),"filtered");
   var overall=Json.Arr(Json.Get(filtered,"all")).Where(b=>Json.Num(Json.Get(b,"sampleCount"))>=100&&IsBoot(data,b)&&!double.IsNaN(Statistics.Calculate(b).Lower)).OrderByDescending(b=>Statistics.Calculate(b).Lower).ToArray();
   var candidates=routeBoots.Length>0?routeBoots:overall;
   object boot=candidates.FirstOrDefault()??choice.Boot;int bestPosition=2;double bestScore=double.NegativeInfinity;bool supported=false;
   var slots=Json.Get(filtered,"slots");
   Func<object,int,double> value=(item,slot)=>{if(slot>6)return double.NaN;string id=Json.Str(Json.Get(item,"id"));var row=Json.Arr(Json.Get(slots,slot.ToString())).FirstOrDefault(r=>Json.Str(Json.Get(r,"id"))==id);var score=Statistics.Calculate(row);return score.N>=100?score.Lower:double.NaN;};
   foreach(var candidate in candidates)for(int position=2;position<=Math.Min(6,core.Length+2);position++){
    double score=value(candidate,position);
    for(int i=0;i<core.Length;i++){int original=i+2;if(original>=position)score+=value(core[i],original+1)-value(core[i],original);}
    if(!double.IsNaN(score)&&score>bestScore){bestScore=score;bestPosition=position;boot=candidate;supported=true;}
   }
   choice.Boot=boot;choice.ShoePositionAfterFusion=boot==null?0:bestPosition;
   choice.ShoeAdvice=boot==null?"鞋子：暂无有效数据":data.Lookup("items",Json.Get(boot,"id"))+"：合成后建议有效第 "+bestPosition+" 件"+(supported?"（综合位置参考）":"（时点样本不足，默认位置）");
   choice.ShoeNote="三种九头蛇先完成并合成，占有效第一格。鞋型优先使用路线内至少100场的有效鞋类，否则使用同海克斯总体有效鞋类；比较鞋子槽位下界与实际后续核心被推迟的槽位下界变化，条件槽位至少100场。这是顺序评分，不是整套胜率。"+(supported?"":"可比槽位样本不足，沿用合成后第一件核心之前的默认位置，不代表最佳时点。");
   order=order.Where(i=>!EquipmentRecommendations.IsShoe(data,Json.Str(Json.Get(i,"id")))).ToList();
   if(boot!=null)order.Insert(Math.Min(order.Count,bestPosition+1),boot);
   return order;
  }
  public static string ShoeTiming(DataStore data,object raw,object boot,object starter,out string note){
   note="鞋子建议使用同英雄、同范围、筛选后单件槽位数据，未与核心路线联合统计；不是整套购买时序的胜率。只比较同一装备ID，不合并升级鞋。";
   if(boot==null)return "鞋子：暂无路线内有效样本";
   string id=Json.Str(Json.Get(boot,"id")),name=data.Lookup("items",id);
   if(Json.Arr(Json.Get(starter,"items")).Any(i=>Json.Str(Json.Get(i,"id"))==id))return name+"：出门装已包含";
   var slots=Json.Get(Json.Get(Json.Get(raw,"items"),"filtered"),"slots");var options=new List<Tuple<int,Score>>();
   for(int n=1;n<=6;n++){var row=Json.Arr(Json.Get(slots,n.ToString())).FirstOrDefault(i=>Json.Str(Json.Get(i,"id"))==id);if(row==null)continue;var score=Statistics.Calculate(row);if(!double.IsNaN(score.Lower))options.Add(Tuple.Create(n,score));}
   if(options.Count<2)return name+"：按需穿插（时点样本不足）";
   double min=Math.Max(200,options.Max(x=>x.Item2.N)*.1);var supported=options.Where(x=>x.Item2.N>=min).ToList();if(supported.Count<2)return name+"：按需穿插（时点样本不足）";
   double top=supported.Max(x=>x.Item2.Lower);var near=supported.Where(x=>top-x.Item2.Lower<=.005).OrderBy(x=>x.Item1).ToList();
   note+="\n各槽位："+string.Join("；",options.Select(x=>"第"+x.Item1+"件 "+Statistics.Pct(x.Item2.Lower)+" / "+x.Item2.N+"样本"))+"。候选至少200样本且不少于最大槽位样本的10%；与最高下界相差0.5个百分点内视为接近。后期单独领先不据此建议拖鞋。";
   var early=near.Where(x=>x.Item1<=3).ToList();if(early.Count==0)return name+"：按需穿插（后期统计不直接推断时点）";
   int first=early[0].Item1,last=first;foreach(var x in near.Where(x=>x.Item1>first)){if(x.Item1!=last+1||x.Item1>3)break;last=x.Item1;}
   return name+"：建议第 "+first+(last>first?"～"+last:"")+" 件（槽位参考）";
  }
  public static Group ToGroup(DataStore data,object raw){var choices=Build(data,raw);return new Group{Path=GroupPath,Title="出装路线与对应最佳出门装",Rows=choices.Select((c,i)=>new Row{Name=c.Name,StarterName=c.StarterName,ShoeAdvice=c.ShoeAdvice,Note=c.ShoeNote+"\n路线筛选：距最高统计学胜率不超过5个百分点且路线点估计收益非负。后两列为路线＋出门装的联合样本，显示提升相对英雄总体。",Lower=c.Lower,GainLower=Statistics.AverageGain(c.Starter,Json.Get(Json.Get(raw,"champion"),"stats")),Count=c.Count,Win=Json.Num(Json.Get(c.Starter,"winRate")),Raw=Json.Obj(c.Starter),Rank=i+1}).ToList()};}
 }
}
