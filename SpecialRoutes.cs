using System;using System.IO;using System.Linq;using System.Collections.Generic;
namespace ConfidenceDesktop {
 public sealed class SpecialRoute { public string AugmentId;public RouteChoice Choice;public object Baseline,ConditionalData;public double AugmentGain,Improvement; }
 public static class SpecialRoutes {
  public static bool EligibleGain(double gain,bool equipmentBound){return equipmentBound?gain>=0:gain>=.01;}
  public static string[] RequiredItems(string name){
   switch(name){
    case "升级：中娅":return new[]{"中娅沙漏","沃格勒特的巫师帽"};
    case "升级：献祭":return new[]{"璀璨回响","日炎圣盾"};
    case "升级：耀光":return new[]{"三相之力","巫妖之祸","冰脉护手","夺萃之镰","黄昏与黎明"};
    case "终极九头蛇":return new[]{"巨型九头蛇","贪欲九头蛇","亵渎九头蛇"};
    case "沃格勒特的巫师帽":return new[]{"灭世者的死亡之帽","中娅沙漏"};
    case "艾卡西亚的陷落":return new[]{"日炎圣盾","璀璨回响"};
    default:return name.StartsWith("升级：")?new[]{name.Substring(3)}:new string[0];
   }
  }
  public static bool FitsEquipment(DataStore data,object route,string name,string[] required){
   var items=Json.Arr(Json.Get(route,"purchaseOrder")).Select(i=>data.Lookup("items",Json.Get(i,"id"))).ToArray();
   return InferredRoutes.TaskRecipe(name)?required.All(items.Contains):required.Any(items.Contains);
  }
  public static void ConfigureShoes(DataStore data,object conditional,RouteChoice choice){
   var routeBoots=Json.Arr(Json.Get(choice.Route,"boots")).Where(b=>Json.Num(Json.Get(b,"sampleCount"))>=100&&RouteRecommendations.IsBoot(data,Json.Get(b,"item"))&&!double.IsNaN(Statistics.Calculate(b).Lower)).OrderByDescending(b=>Statistics.Calculate(b).Lower).ThenByDescending(b=>Json.Num(Json.Get(b,"sampleCount"))).ToList();
   choice.Boot=routeBoots.Select(b=>Json.Get(b,"item")).FirstOrDefault();
   if(choice.Boot==null)choice.Boot=Json.Arr(Json.Get(Json.Get(Json.Get(conditional,"items"),"filtered"),"all")).Where(b=>Json.Num(Json.Get(b,"sampleCount"))>=100&&RouteRecommendations.IsBoot(data,b)&&!double.IsNaN(Statistics.Calculate(b).Lower)).OrderByDescending(b=>Statistics.Calculate(b).Lower).ThenByDescending(b=>Json.Num(Json.Get(b,"sampleCount"))).FirstOrDefault();
   choice.ShoeAdvice=RouteRecommendations.JointShoeTiming(data,conditional,choice.Route,choice.Boot,choice.Starter,out choice.ShoeNote,true);
  }
  public static string Label(DataStore d,SpecialRoute s){return d.Lookup("augments",s.AugmentId);}
  public static string Order(object route){return string.Join(",",Json.Arr(Json.Get(route,"purchaseOrder")).Select(x=>Json.Str(Json.Get(x,"id"))));}
  static object[] MissingRecipeItems(DataStore data,RouteChoice choice,string name){
   var present=new HashSet<string>(Json.Arr(Json.Get(choice.Route,"purchaseOrder")).Select(i=>data.Lookup("items",Json.Get(i,"id"))));
   var missing=new List<object>();
   foreach(var ingredient in RequiredItems(name)){
    if(present.Contains(ingredient))continue;
    string id=data.Resources["items"].Keys.FirstOrDefault(key=>data.Lookup("items",key)==ingredient&&EquipmentRecommendations.Buyable(data,new Dictionary<string,object>{{"id",key}})&&!EquipmentRecommendations.RequiresSpecialAccess(data,new Dictionary<string,object>{{"id",key}}));
    if(id==null)return null;
    missing.Add(new Dictionary<string,object>{{"id",id}});
   }
   return missing.ToArray();
  }
  public static List<SpecialRoute> Build(DataStore data,object raw,RouteChoice normal){
   if(normal==null)return new List<SpecialRoute>();int hero=(int)Json.Num(Json.Get(Json.Get(raw,"champion"),"id"),0);string scope=Json.Str(Json.Get(raw,"_scope"));if(scope!="all"&&scope!="high")return new List<SpecialRoute>();
   string path=Path.Combine(data.StatsRoot,scope,"champion-details",hero+"-single-augments.json");if(!File.Exists(path))return new List<SpecialRoute>();
   return Evaluate(data,raw,normal,Json.Read(path));
  }
  public static List<SpecialRoute> Evaluate(DataStore data,object raw,RouteChoice normal,object conditional){
   var result=new List<SpecialRoute>();if(normal==null)return result;
   var overall=Json.Get(Json.Get(raw,"champion"),"stats");var augments=Json.Arr(Json.Get(Json.Get(raw,"augments"),"all"));string defaultOrder=Order(normal.Route);
   var builds=Json.Arr(Json.Get(conditional,"builds")).ToList();foreach(var item in Json.Arr(Json.Get(conditional,"items")))if(!builds.Any(b=>Json.Str(Json.Get(b,"augmentId"))==Json.Str(Json.Get(item,"augmentId"))))builds.Add(new Dictionary<string,object>{{"augmentId",Json.Get(item,"augmentId")}});
   foreach(var augment in augments){string id=Json.Str(Json.Get(augment,"id"));if(InferredRoutes.TaskRecipe(data.Lookup("augments",id))&&!builds.Any(b=>Json.Str(Json.Get(b,"augmentId"))==id))builds.Add(new Dictionary<string,object>{{"augmentId",id}});}
   foreach(var build in builds){
    string id=Json.Str(Json.Get(build,"augmentId"));var augment=augments.FirstOrDefault(a=>Json.Str(Json.Get(a,"id"))==id);if(augment==null)continue;
    double gain=Statistics.ComparativeGain(augment,overall);if(!(gain>=0))continue;
    var routes=Json.Arr(Json.Get(build,"archetypes")).SelectMany(f=>Json.Arr(Json.Get(f,"profiles"))).SelectMany(p=>Json.Arr(Json.Get(p,"routes"))).ToList();
    string augmentName=data.Lookup("augments",id);var required=RequiredItems(augmentName);bool equipmentBound=required.Length>0;if(!EligibleGain(gain,equipmentBound))continue;
    var baseline=routes.Where(r=>Order(r)==defaultOrder&&!double.IsNaN(Statistics.Calculate(r).Lower)).OrderByDescending(r=>Json.Num(Json.Get(r,"sampleCount"))).FirstOrDefault();
    var candidates=Json.Arr(Json.Get(build,"archetypes")).SelectMany(f=>Json.Arr(Json.Get(f,"profiles")).SelectMany(p=>Json.Arr(Json.Get(p,"routes")).Select(r=>new {Flow=f,Profile=p,Route=r})))
     .Where(x=>Json.Arr(Json.Get(x.Route,"purchaseOrder")).Length==3&&Json.Arr(Json.Get(x.Route,"purchaseOrder")).All(i=>RouteRecommendations.ValidItem(data,i))&&Json.Arr(Json.Get(x.Route,"purchaseOrder")).Select(i=>Json.Str(Json.Get(i,"id"))).OrderBy(i=>i).SequenceEqual(Json.Arr(Json.Get(x.Profile,"itemSet")).Select(i=>Json.Str(Json.Get(i,"id"))).OrderBy(i=>i))&&!double.IsNaN(Statistics.Calculate(x.Route).Lower))
     .Where(x=>Json.Num(Json.Get(x.Route,"sampleCount"))>=100&&(!equipmentBound||InferredRoutes.TaskRecipe(augmentName)||FitsEquipment(data,x.Route,augmentName,required)))
     .OrderByDescending(x=>Statistics.Calculate(x.Route).Lower).ThenByDescending(x=>Json.Num(Json.Get(x.Route,"sampleCount")));
    var selected=candidates.FirstOrDefault(x=>RouteRecommendations.BestStarter(data,x.Route)!=null);if(selected==null){var itemData=Json.Arr(Json.Get(conditional,"items")).FirstOrDefault(i=>Json.Str(Json.Get(i,"augmentId"))==id);var reference=InferredRoutes.Build(data,itemData,normal,augmentName);if(reference!=null){var context=new Dictionary<string,object>{{"items",new Dictionary<string,object>{{"filtered",itemData}}},{"_fallbackItems",Json.Get(Json.Get(raw,"items"),"filtered")}};result.Add(new SpecialRoute{AugmentId=id,Choice=reference,ConditionalData=context,AugmentGain=gain,Improvement=double.NaN});}continue;}
    var profileCopy=new Dictionary<string,object>(Json.Obj(selected.Profile));profileCopy["routes"]=new object[]{selected.Route};
    var flowCopy=new Dictionary<string,object>(Json.Obj(selected.Flow));flowCopy["profiles"]=new object[]{profileCopy};
    var buildCopy=new Dictionary<string,object>(Json.Obj(build));buildCopy["archetypes"]=new object[]{flowCopy};
    var items=Json.Arr(Json.Get(conditional,"items")).FirstOrDefault(i=>Json.Str(Json.Get(i,"augmentId"))==id);
    var synthetic=new Dictionary<string,object>{{"champion",new Dictionary<string,object>{{"stats",augment}}},{"builds",new Dictionary<string,object>{{"filtered",buildCopy}}},{"items",new Dictionary<string,object>{{"filtered",items}}},{"_fallbackItems",Json.Get(Json.Get(raw,"items"),"filtered")}};
    var choice=RouteRecommendations.Build(data,synthetic,false).FirstOrDefault();if(choice==null)continue;
    choice.IsAugmentRoute=true;
    if(InferredRoutes.TaskRecipe(augmentName)){
     choice.FusionAugment=augmentName;
     choice.PreFusionRecipe=MissingRecipeItems(data,choice,augmentName);
     if(choice.PreFusionRecipe==null)continue;
    }
    ConfigureShoes(data,synthetic,choice);
    if(choice.PreFusionRecipe.Length>0)choice.ShoeNote+=" 网站实测核心三件没有列齐任务装备；已将"+string.Join("、",choice.PreFusionRecipe.Select(i=>data.Lookup("items",Json.Get(i,"id"))))+"放在路线前以完成任务。任务装备与网站三件核心的合并顺序没有联合胜率数据。";
    double improvement=baseline==null?double.NaN:choice.RouteLower-Statistics.Calculate(baseline).Lower;
    result.Add(new SpecialRoute{AugmentId=id,Choice=choice,Baseline=baseline,ConditionalData=synthetic,AugmentGain=gain,Improvement=improvement});
   }
   foreach(var special in result)EquipmentRecommendations.CompleteCore(data,special.ConditionalData,special.Choice);
   return result.OrderByDescending(r=>r.Improvement).ThenBy(r=>r.AugmentId).ToList();
  }
 }
}
