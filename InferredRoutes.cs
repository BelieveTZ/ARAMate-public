using System;using System.Linq;using System.Collections.Generic;
namespace ConfidenceDesktop {
 public static class InferredRoutes {
  class O {public int Index;public object Item;public string Id,Name;public double Overall,N;public double[] Score=new double[4];public bool[] Exact=new bool[4];}
  public static bool TaskRecipe(string n){return n=="终极九头蛇"||n=="艾卡西亚的陷落"||n=="沃格勒特的巫师帽";}
  static bool Conflict(string a,string b){string[][] sets={new[]{"魔宗","大天使之杖","凛冬之临","末日寒冬","炽天使之拥","魔切"},new[]{"三相之力","巫妖之祸","冰脉护手","黄昏与黎明"},new[]{"斯特拉克的挑战护手","玛莫提乌斯之噬","不朽盾弓"},new[]{"贪欲九头蛇","巨型九头蛇","亵渎九头蛇"}};return sets.Any(g=>g.Contains(a)&&g.Contains(b));}
  public static RouteChoice Build(DataStore d,object items,RouteChoice normal,string name,double priorWeight=0.12){
   if(normal==null||(items==null&&!TaskRecipe(name)))return null;var required=SpecialRoutes.RequiredItems(name);
   var all=Json.Arr(Json.Get(items,"all"));
   var options=all.Where(r=>Json.Num(Json.Get(r,"sampleCount"))>=100&&EquipmentRecommendations.Buyable(d,r)&&!EquipmentRecommendations.RequiresSpecialAccess(d,r)&&!EquipmentRecommendations.IsShoe(d,Json.Str(Json.Get(r,"id")))&&!double.IsNaN(Statistics.Calculate(r).Lower)).Select(r=>new O{Item=r,Id=Json.Str(Json.Get(r,"id")),Name=d.Lookup("items",Json.Get(r,"id")),Overall=Statistics.Calculate(r).Lower,N=Json.Num(Json.Get(r,"sampleCount"))}).Where(o=>o.Name!="终极九头蛇"&&o.Name!="虚空献祭"&&o.Name!="沃格勒特的巫师帽").ToList();
   var boot=all.Where(r=>Json.Num(Json.Get(r,"sampleCount"))>=100&&RouteRecommendations.IsBoot(d,r)&&!double.IsNaN(Statistics.Calculate(r).Lower)).OrderByDescending(r=>Statistics.Calculate(r).Lower).FirstOrDefault();
   if(TaskRecipe(name)){
    var mandatory=new List<O>();int unscored=0;
    foreach(var n in required){var item=options.FirstOrDefault(o=>o.Name==n);
     if(item==null){var id=d.Resources["items"].Keys.FirstOrDefault(k=>d.Lookup("items",k)==n&&EquipmentRecommendations.Buyable(d,new Dictionary<string,object>{{"id",k}}));if(id==null)return null;item=new O{Id=id,Name=n,Item=new Dictionary<string,object>{{"id",id}},Overall=double.NegativeInfinity,N=0};unscored++;}
     mandatory.Add(item);
    }
    var normalIds=SpecialRoutes.Order(normal.Route).Split(',');
    return Make(d,normal,mandatory.OrderByDescending(o=>o.Overall).ThenBy(o=>{int index=Array.IndexOf(normalIds,o.Id);return index<0?int.MaxValue:index;}).ThenByDescending(o=>o.N).Select(o=>o.Item).ToArray(),"参考",boot,2,"任务配方装备必须列齐，不受单件样本门槛限制。有效总体单件评分优先；无有效评分时参考常规顺序，再按配方顺序列出。缺少至少100样本评分的必做装备："+unscored+"件。鞋子暂放第一件核心之后；此顺序未被联合数据验证，不使用合成后槽位反推任务前顺序。出门装沿用常规。",name);
   }
   if(options.Count==0)return null;
   var bootScore=new double[4];var bootExact=new bool[4];
   for(int slot=1;slot<=4;slot++){var rows=Json.Arr(Json.Get(Json.Get(items,"slots"),slot.ToString())).Where(r=>Json.Num(Json.Get(r,"sampleCount"))>=100&&!double.IsNaN(Statistics.Calculate(r).Lower)).GroupBy(r=>Json.Str(Json.Get(r,"id"))).ToDictionary(g=>g.Key,g=>g.First());double top=rows.Values.Select(r=>Statistics.Calculate(r).Lower).DefaultIfEmpty(options.Max(o=>o.Overall)).Max();
    foreach(var o in options){object r;o.Exact[slot-1]=rows.TryGetValue(o.Id,out r);o.Score[slot-1]=(o.Exact[slot-1]?Statistics.Calculate(r).Lower:o.Overall)-top;}
    if(boot!=null){object r;bootExact[slot-1]=rows.TryGetValue(Json.Str(Json.Get(boot,"id")),out r);bootScore[slot-1]=(bootExact[slot-1]?Statistics.Calculate(r).Lower:Statistics.Calculate(boot).Lower)-top;}
   }
   var normalOrder=SpecialRoutes.Order(normal.Route).Split(',');
   var pool=options.OrderByDescending(o=>o.Overall).Take(6).Concat(Enumerable.Range(0,4).SelectMany(i=>options.Where(o=>o.Exact[i]).OrderByDescending(o=>o.Score[i]).Take(6))).Concat(options.Where(o=>required.Contains(o.Name)||normalOrder.Contains(o.Id))).GroupBy(o=>o.Id).Select(g=>g.First()).ToArray();
   for(int i=0;i<pool.Length;i++)pool[i].Index=i;var conflicts=new bool[pool.Length,pool.Length];for(int i=0;i<pool.Length;i++)for(int j=0;j<pool.Length;j++)conflicts[i,j]=Conflict(pool[i].Name,pool[j].Name);
   O[] best=null;int bestMissing=int.MaxValue,bestPos=2;double bestScore=double.NegativeInfinity,bestN=0;
   for(int pos=1;pos<=(boot==null?1:4);pos++)foreach(var a in pool)foreach(var b in pool)foreach(var c in pool){if(a.Id==b.Id||a.Id==c.Id||b.Id==c.Id||conflicts[a.Index,b.Index]||conflicts[a.Index,c.Index]||conflicts[b.Index,c.Index])continue;var seq=new[]{a,b,c};if(required.Length>0&&!seq.Any(o=>required.Contains(o.Name)))continue;int missing=boot!=null&&!bootExact[pos-1]?1:0;double score=boot==null?0:bootScore[pos-1];for(int i=0;i<3;i++){int slot=i+1;if(boot!=null&&slot>=pos)slot++;if(!seq[i].Exact[slot-1])missing++;score+=seq[i].Score[slot-1];if(normalOrder.Contains(seq[i].Id))score+=priorWeight;if(i<normalOrder.Length&&normalOrder[i]==seq[i].Id)score+=priorWeight*.35;}double n=seq.Min(o=>o.N);if(missing<bestMissing||(missing==bestMissing&&(score>bestScore+1e-9||(Math.Abs(score-bestScore)<=1e-9&&n>bestN)))){best=seq;bestMissing=missing;bestScore=score;bestPos=pos;bestN=n;}}
   return best==null?null:Make(d,normal,best.Select(o=>o.Item).ToArray(),"参考",boot,bestPos,"单件槽位评分结合常规核心参考，缺失槽位数："+bestMissing+"；缺失时使用同海克斯总体单件数据。出门装沿用常规，无三件套联合胜率。");
  }
  static RouteChoice Make(DataStore d,RouteChoice normal,object[] items,string label,object boot,int pos,string note,string fusionAugment=null){return new RouteChoice{FusionAugment=fusionAugment,IsAugmentRoute=true,Flow=normal.Flow,ReferenceLabel=label,IsFallback=true,Route=new Dictionary<string,object>{{"purchaseOrder",items}},Starter=normal.Starter,StarterName=normal.StarterName,Boot=boot,Name=string.Join(" → ",items.Select(i=>d.Lookup("items",Json.Get(i,"id")))),RouteLower=double.NaN,RouteGain=double.NaN,Lower=double.NaN,Gain=double.NaN,Count=0,ShoeAdvice=boot==null?"鞋子：暂无有效数据":d.Lookup("items",Json.Get(boot,"id"))+"：建议第 "+pos+" 件",ShoeNote=note};}
 }
}
