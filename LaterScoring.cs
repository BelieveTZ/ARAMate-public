using System;
using System.Collections.Generic;
using System.Linq;

namespace ConfidenceDesktop {
 public static class LaterScoring {
  // 2026-10-10 snapshot: DerSimonian–Laird all-hero median estimates,
  // high/all: slot 5 ~611/638, slot 6 ~500/511, laterItems ~515/547; rounded.
  public const double KRoute=500;
  public static double KSlot(int slot){return slot==6?500:600;}
  public static string Grade(double gap){return gap<=.01+1e-12?"S":gap<=.02+1e-12?"A":gap<=.03+1e-12?"B":"C";}
  static string Id(object item){return Json.Str(Json.Get(item,"id"));}
  static double N(object row){return Json.Num(Json.Get(row,"sampleCount"),0);}
  sealed class Evidence {public object Item;public double N,W;}
  static HashSet<string> Ids(object row,string key){return new HashSet<string>(Json.Arr(Json.Get(row,key)).Select(Id));}
  static Dictionary<string,Evidence> RouteEvidence(object raw,RouteChoice choice){
   var result=new Dictionary<string,Evidence>();
   // Inferred routes have no measured profile, even if their ids happen to match one.
   if(choice==null||choice.Route==null||!string.IsNullOrEmpty(choice.ReferenceLabel))return result;
   var profiles=Json.Arr(Json.Get(Json.Get(Json.Get(raw,"builds"),"filtered"),"archetypes")).SelectMany(a=>Json.Arr(Json.Get(a,"profiles"))).ToList();
   var profile=profiles.FirstOrDefault(p=>Json.Arr(Json.Get(p,"routes")).Any(r=>ReferenceEquals(r,choice.Route)));
   if(profile==null){var ids=Ids(choice.Route,"purchaseOrder");profile=profiles.FirstOrDefault(p=>ids.SetEquals(Ids(p,"itemSet"))&&Json.Arr(Json.Get(p,"routes")).Any(r=>ids.SetEquals(Ids(r,"purchaseOrder"))));}
   if(profile==null)return result;
   foreach(var route in Json.Arr(Json.Get(profile,"routes")))foreach(var row in Json.Arr(Json.Get(route,"laterItems"))){
    double n=N(row),p=Statistics.PointRate(row);if(n<=0||double.IsNaN(p))continue;
    var item=Json.Get(row,"item")??row;string id=Id(item);Evidence e;
    if(!result.TryGetValue(id,out e)){e=new Evidence{Item=item};result.Add(id,e);}e.N+=n;e.W+=n*p;
   }
   return result;
  }
  public static List<EquipmentRecommendations.RatedItem> Score(DataStore data,object raw,RouteChoice choice,int slot,IEnumerable<object> source,Func<object,bool> allowed,bool overall=false,bool hero=false){
   var rows=source.ToList();double sn=rows.Sum(r=>N(r)),sw=rows.Sum(r=>N(r)>0?N(r)*Statistics.PointRate(r):0),ps=sn>0?sw/sn:double.NaN;
   var route=RouteEvidence(raw,choice);double ln=route.Values.Sum(e=>e.N),pl=ln>0?route.Values.Sum(e=>e.W)/ln:double.NaN;bool hasRoute=ln>0;
   var positions=rows.GroupBy(Id).ToDictionary(g=>g.Key,g=>g.First());
   var pool=rows.Where(r=>EquipmentRecommendations.Buyable(data,r)&&!double.IsNaN(Statistics.Calculate(r).Lower)&&allowed(r)).Concat(route.Values.Select(e=>e.Item).Where(r=>EquipmentRecommendations.Buyable(data,r)&&allowed(r))).GroupBy(Id).Select(g=>g.First());
   var result=new List<EquipmentRecommendations.RatedItem>();
   foreach(var item in pool){object b;positions.TryGetValue(Id(item),out b);double nb=b==null?0:N(b),db=nb>0?nb/(nb+KSlot(slot))*(Statistics.PointRate(b)-ps):0;Evidence a;route.TryGetValue(Id(item),out a);double na=a==null?0:a.N,wa=a==null?0:a.W;
    double delta=hasRoute?(wa-na*pl+KRoute*db)/(na+KRoute):db;
    result.Add(new EquipmentRecommendations.RatedItem{Item=item,Lower=(hasRoute?pl:ps)+delta,Gain=delta,EvidenceCount=na+nb,HasRouteEvidence=hasRoute,UsesOverallData=overall,UsesHeroData=hero});
   }
   result=result.OrderBy(r=>EquipmentRecommendations.RequiresSpecialAccess(data,r.Item)).ThenByDescending(r=>r.Gain).ThenByDescending(r=>r.EvidenceCount).ThenBy(r=>Id(r.Item),StringComparer.Ordinal).ToList();
   var best=result.FirstOrDefault(r=>!EquipmentRecommendations.RequiresSpecialAccess(data,r.Item));
   foreach(var r in result)r.Grade=EquipmentRecommendations.RequiresSpecialAccess(data,r.Item)?"X":Grade(best.Gain-r.Gain);
   return result;
  }
 }
}
