using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
namespace ConfidenceDesktop {
 public sealed class ShopPlan { public int Hero; public string Role,Flow,Title,Key; public double Gap; public Dictionary<string,object> Set; }
 public static class ShopBuilds {
  const string Prefix="aramate-shop-";
  const string LegacyPrefix="believetz-shop-";
  static IEnumerable<string> CandidateFiles(string directory){return Directory.GetFiles(directory,Prefix+"*.json").Concat(Directory.GetFiles(directory,LegacyPrefix+"*.json"));}
  // 文件名按补零名次升序排列，sortrank 同步保留。
  public const bool SortAscending=true;
  internal static int SortRank(int position){return SortAscending?position:-position;}
  internal static IEnumerable<T> OrderPlans<T>(IEnumerable<T> plans,Func<T,object> row,Func<T,double> gain){
   var scored=plans.Select(p=>new{Plan=p,Lower=Statistics.Calculate(row(p)).Lower,Gain=gain(p),Count=Json.Num(Json.Get(row(p),"sampleCount"))});
   return Statistics.OrderDisplayed(scored,p=>p.Lower,p=>p.Gain,p=>p.Count).Select(p=>p.Plan);
  }
  internal static void RankPlans(IEnumerable<ShopPlan> plans,Func<ShopPlan,object> row,object heroRaw,int start){
   int index=0;
   foreach(var plan in OrderPlans(plans,row,p=>p.Key.StartsWith("combo-",StringComparison.Ordinal)?Statistics.CombinationGain(row(p),heroRaw):Statistics.ComparativeGain(row(p),Json.Get(Json.Get(heroRaw,"champion"),"stats"))))plan.Set["sortrank"]=SortRank(start+index++);
  }
  public const string CombinationRole="海克斯组合";
  static IEnumerable<object> Ranked(object rows){return Json.Arr(rows).Where(r=>!double.IsNaN(Statistics.Calculate(r).Lower)).OrderByDescending(r=>Statistics.Calculate(r).Lower).ThenByDescending(r=>Json.Num(Json.Get(r,"sampleCount")));}
  internal static string Short(string name){return name.Split(new[]{" · "},StringSplitOptions.None).Last();}
  static string Flow(string key){return DataStore.Word(key);}
  internal static string Uid(int hero,string role){return Uid(hero,role,Prefix);}
  static string Uid(int hero,string role,string prefix){using(var md5=MD5.Create())return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(prefix+hero+"-"+role))).ToString();}
  // 商店忽略 count 的显示数量；重复购买必须保留为原顺序的多个图标条目。
  internal static object Block(string label,IEnumerable<object> entries){var items=entries.Select(e=>Json.Get(e,"id")).Where(i=>Json.Num(i,0)>0).Select(i=>new {id=Json.Str(i),count=1}).ToArray();return items.Length==0?null:new {type=label,items=items,hideIfSummonerSpell="",showIfSummonerSpell=""};}
  static void AddLaterBlocks(List<object> blocks,DataStore data,object raw,RouteChoice choice){
   AddLaterBlocks(blocks,slot=>EquipmentRecommendations.RecommendedLater(data,raw,choice,slot));
  }
  internal static void AddLaterBlocks(List<object> blocks,Func<int,List<EquipmentRecommendations.RatedItem>> later){
   var fifthItems=new HashSet<string>();
   for(int slot=5;slot<=6;slot++){
    string label="第"+new[]{"零","一","二","三","四","五","六"}[slot]+"件 · ";
    var candidates=later(slot);
    foreach(string grade in new[]{"S","A"}){
     var items=candidates.Where(r=>r.Grade==grade&&(slot!=6||!fifthItems.Contains(Json.Str(Json.Get(r.Item,"id"))))).Select(r=>r.Item).ToArray();
     var block=Block(label+grade+"级",items);if(block!=null)blocks.Add(block);
     if(slot==5)fifthItems.UnionWith(items.Select(i=>Json.Str(Json.Get(i,"id"))));
    }
   }
  }
  static string Rate(object row){return Statistics.Pct(Statistics.Calculate(row).Lower);}
  public static List<ShopPlan> Build(DataStore data,int hero,string scope,double maxGap){
   scope="high";var raw=data.ReadHero(hero,scope);var archetypes=Ranked(Json.Get(Json.Get(Json.Get(raw,"builds"),"filtered"),"archetypes")).ToList();var plans=new List<ShopPlan>();
   var routeChoices=EquipmentRecommendations.Choices(data,raw,maxGap);var chosen=routeChoices.Select(c=>archetypes.First(a=>Json.Str(Json.Get(a,"key"))==c.Flow)).ToList();var best=chosen.FirstOrDefault();
   foreach(var a in chosen){
    var choice=routeChoices.FirstOrDefault(c=>c.Flow==Json.Str(Json.Get(a,"key")));if(choice==null)continue;var route=choice.Route;
    string role=ReferenceEquals(a,best)?"主方案":"备选",flow=Flow(Json.Str(Json.Get(a,"key"))),title="★常规";
    var blocks=new List<object>();
    blocks.Add(Block("核心装备",Json.Arr(Json.Get(choice.Starter,"items")).Concat(EquipmentRecommendations.CorePurchaseSequence(data,choice))));
    AddLaterBlocks(blocks,data,raw,choice);
    var set=new Dictionary<string,object>{{"title",title},{"type","custom"},{"map","HA"},{"mode","any"},{"associatedChampions",new[]{hero}},{"associatedMaps",new[]{12}},{"preferredItemSlots",new object[0]},{"sortrank",SortRank(plans.Count)},{"startedFrom","blank"},{"uid",Uid(hero,Json.Str(Json.Get(a,"key")))},{"blocks",blocks.Where(b=>b!=null).ToArray()}};
    plans.Add(new ShopPlan{Hero=hero,Key=Json.Str(Json.Get(a,"key")),Role=role,Flow=flow,Title=title,Gap=ReferenceEquals(a,best)?0:Statistics.GainBound(best,true)-Statistics.GainBound(a,false),Set=set});
   }
   var normal=routeChoices.FirstOrDefault();var specialRoutes=SpecialRoutes.Build(data,raw,normal);
   plans.AddRange(SpecialPlans(data,hero,raw,specialRoutes));
   plans.AddRange(CombinationShopBuilds.Build(data,hero,raw,normal,specialRoutes));
   return plans;
  }
  public static List<ShopPlan> SpecialPlans(DataStore data,int hero,object raw,RouteChoice normal){
   return SpecialPlans(data,hero,raw,SpecialRoutes.Build(data,raw,normal));
  }
  static List<ShopPlan> SpecialPlans(DataStore data,int hero,object raw,IEnumerable<SpecialRoute> routes){
   var result=new List<ShopPlan>();
   foreach(var special in routes){
    var choice=special.Choice;string name=SpecialRoutes.Label(data,special),key="augment-"+special.AugmentId;
    string title=name;var blocks=new List<object>();
    blocks.Add(Block("核心装备",Json.Arr(Json.Get(choice.Starter,"items")).Concat(EquipmentRecommendations.CorePurchaseSequence(data,choice))));
    AddLaterBlocks(blocks,data,special.ConditionalData,choice);
    var set=new Dictionary<string,object>{{"title",title},{"type","custom"},{"map","HA"},{"mode","any"},{"associatedChampions",new[]{hero}},{"associatedMaps",new[]{12}},{"preferredItemSlots",new object[0]},{"sortrank",2},{"startedFrom","blank"},{"uid",Uid(hero,key)},{"blocks",blocks.Where(b=>b!=null).ToArray()}};
    result.Add(new ShopPlan{Hero=hero,Key=key,Role="海克斯专用",Flow=name,Title=title,Gap=special.Improvement,Set=set});
   }
   var rows=Json.Arr(Json.Get(Json.Get(raw,"augments"),"all")).ToDictionary(r=>Json.Str(Json.Get(r,"id")));
   RankPlans(result,p=>rows[p.Key.Substring(8)],raw,100);
   return result;
  }
  public static string FileName(ShopPlan p){
   double rank=Json.Num(Json.Get(p.Set,"sortrank"));if(double.IsNaN(rank)||rank<0||rank>=10000||rank!=Math.Floor(rank))throw new InvalidDataException("商店套装名次必须为 0–9999 的整数");
   return Prefix+p.Hero+"-"+((int)rank).ToString("D4",System.Globalization.CultureInfo.InvariantCulture)+"-"+p.Key+".json";
  }
  public static List<ShopPlan> Export(DataStore data,string scope,double gap,string directory){
   scope="high";if(gap<0||gap>.1||double.IsNaN(gap))throw new ArgumentException("流派差距阈值无效");Directory.CreateDirectory(directory);
   var plans=data.Heroes.Where(h=>h.Id>0).SelectMany(h=>Build(data,h.Id,scope,gap)).ToList();foreach(var plan in plans)File.WriteAllText(Path.Combine(directory,FileName(plan)),Json.Write(plan.Set),new UTF8Encoding(false));
   var expected=new HashSet<string>(plans.Select(FileName));foreach(var old in CandidateFiles(directory))if(!expected.Contains(Path.GetFileName(old))&&IsOwned(old))File.Delete(old);
   var report=new StringBuilder("# 商店套装预览\n\n版本 "+data.Version+" · "+data.Date+"；"+"高分段对局"+"；备选流派差距阈值 "+Statistics.Pct(gap)+"。\n\n");
   foreach(var p in plans){report.AppendLine("## "+Short(data.Lookup("champions",p.Hero))+" · "+p.Title);report.AppendLine(p.Role=="海克斯专用"?"\n仅在已选对应海克斯时使用；单件装备评分不代表整套购买顺序的联合胜率。\n":p.Role==CombinationRole?"\n组合排名有实测胜率；装备按两项单海克斯装备数据合成，未有组合装备联合胜率。\n":"\n流派差距保守上界："+Statistics.Pct(p.Gap)+"\n");foreach(var block in Json.Arr(Json.Get(Json.Parse(Json.Write(p.Set)),"blocks"))){report.AppendLine("- "+Json.Str(Json.Get(block,"type"))+"："+string.Join(" + ",Json.Arr(Json.Get(block,"items")).Select(i=>data.Lookup("items",Json.Get(i,"id"))+(Json.Num(Json.Get(i,"count"),1)>1?" ×"+Json.Str(Json.Get(i,"count")):""))));}report.AppendLine();}
   File.WriteAllText(Path.Combine(directory,"套装预览.md"),report.ToString(),Encoding.UTF8);return plans;
  }
  static void WriteOwned(string path,string contents,string backup){
   if(File.Exists(path)){
    var previous=Json.Read(path);if(!IsOwned(path)||Json.Str(Json.Get(previous,"uid"))!=Json.Str(Json.Get(Json.Parse(contents),"uid")))throw new IOException("拒绝覆盖非本助手套装："+Path.GetFileName(path));
    if(File.ReadAllText(path)==contents)return;Directory.CreateDirectory(backup);File.Copy(path,Path.Combine(backup,Path.GetFileName(path)),true);
   }
   string temp=path+".tmp";File.WriteAllText(temp,contents,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
  }
  public static void Install(List<ShopPlan> plans,string gameDirectory,string backup){
   gameDirectory=Path.GetFullPath(gameDirectory);if(!File.Exists(Path.Combine(gameDirectory,"League of Legends.exe")))throw new IOException("游戏目录未通过验证");
   if(Process.GetProcessesByName("League of Legends").Length>0)throw new IOException("对局运行中，留待下次启动更新商店方案");
   string directory=Path.Combine(gameDirectory,"Config","Global","Recommended");Directory.CreateDirectory(directory);
   foreach(var p in plans)WriteOwned(Path.Combine(directory,FileName(p)),Json.Write(p.Set),backup);
   var expected=new HashSet<string>(plans.Select(FileName),StringComparer.OrdinalIgnoreCase);
   foreach(var path in CandidateFiles(directory)){if(expected.Contains(Path.GetFileName(path))||!IsOwned(path))continue;Directory.CreateDirectory(backup);File.Copy(path,Path.Combine(backup,Path.GetFileName(path)),true);File.Delete(path);}

  }
  static bool IsOwned(string path){
   try{var old=Json.Read(path);int hero=(int)Json.Num(Json.Arr(Json.Get(old,"associatedChampions")).FirstOrDefault(),0);string stem=Path.GetFileNameWithoutExtension(path),prefix=stem.StartsWith(LegacyPrefix,StringComparison.Ordinal)?LegacyPrefix:Prefix,head=prefix+hero+"-";
    if(hero<=0||!stem.StartsWith(head,StringComparison.Ordinal))return false;string key=stem.Substring(head.Length);var rank=System.Text.RegularExpressions.Regex.Match(key,@"^(?:[0-9]{4}|[012])-");if(rank.Success)key=key.Substring(rank.Length);
    return System.Text.RegularExpressions.Regex.IsMatch(key,"^[a-z0-9_-]+$")&&Json.Str(Json.Get(old,"uid"))==Uid(hero,key,prefix);
   }catch{return false;}
  }
  public static int Cleanup(string gameDirectory){
   gameDirectory=Path.GetFullPath(gameDirectory);if(!File.Exists(Path.Combine(gameDirectory,"League of Legends.exe")))throw new IOException("游戏目录未通过验证");
   string directory=Path.Combine(gameDirectory,"Config","Global","Recommended");if(!Directory.Exists(directory))return 0;
   int count=0;foreach(var path in CandidateFiles(directory))if(IsOwned(path)){File.Delete(path);count++;}return count;

  }
 }
 public sealed class ShopSession {
  DateTime nextPathCheck=DateTime.MinValue;string installedKey="",lastEvent="",lastPhase="";bool observedGame,initialized,notMayhemLogged;DateTime retryAfter=DateTime.MinValue;
  internal Func<bool> GameRunning=()=>Process.GetProcessesByName("League of Legends").Length>0;
  public string Root=AppDomain.CurrentDomain.BaseDirectory;
  public LocalClient Client;
  public Func<string,bool> DetectSettings;
  bool settingsInitialized;string lastDetection="";readonly object settingsGate=new object(),logGate=new object();
  string SettingsPath {get{return Path.Combine(Root,"商店套装设置.json");}}
  public void InitializeSettings(){lock(settingsGate){if(settingsInitialized)return;Directory.CreateDirectory(Root);if(!File.Exists(SettingsPath))Record("缺少设置文件","按默认值创建：enabled=true、maxArchetypeGap=0.03，自动检测 gameDirectory");if(ShopSettings.Migrate(SettingsPath))Record("迁移商店设置","旧默认关闭改为 enabled=true；userChoice=false；删除 scope");settingsInitialized=true;}}
  public bool AutoImportEnabled {get{InitializeSettings();return ShopSettings.LoadEnabled(SettingsPath);}}
  public void SetAutoImport(bool enabled){InitializeSettings();ShopSettings.SaveEnabled(SettingsPath,enabled);installedKey="";DisplayKind="";Record("用户设置","enabled="+enabled.ToString().ToLowerInvariant()+"；userChoice=true");}
  void RecordDetection(string evt,string detail){if(detail==lastDetection)return;Record(evt,detail);lastDetection=detail;}
  bool DetectPath(string settings){return DetectSettings!=null?DetectSettings(settings):GameLocation.SaveDetected(settings,()=>GameLocation.Detect(Client,RecordDetection));}
  public string DisplayKind {get;private set;} public int ImportedCount {get;private set;} public string Error {get;private set;}
  public void Record(string evt,string detail){lock(logGate){string content=evt+" | "+detail;if(content==lastEvent)return;string path=Path.Combine(Root,"商店套装记录.log");Directory.CreateDirectory(Root);
   string line=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" "+content.Replace("\r","\\r").Replace("\n","\\n")+Environment.NewLine;
   if(File.Exists(path)&&new FileInfo(path).Length+Encoding.UTF8.GetByteCount(line)>200*1024){string rotated=Path.Combine(Root,"商店套装记录.1.log");if(File.Exists(rotated))File.Delete(rotated);File.Move(path,rotated);}
   File.AppendAllText(path,line,new UTF8Encoding(false));lastEvent=content;}
  }
  public void Tick(DataStore data,ClientStatus status,string selectedScope){
   string root=Root,settings=Path.Combine(root,"商店套装设置.json");
   try{
    if(status.Phase=="ChampSelect"&&lastPhase!="ChampSelect")notMayhemLogged=false;lastPhase=status.Phase;
    InitializeSettings();
    if(DateTime.UtcNow>=nextPathCheck){nextPathCheck=DateTime.UtcNow.AddSeconds(30);DetectPath(settings);}
    var config=Json.Read(settings);string game=Json.Str(Json.Get(config,"gameDirectory"));bool running=GameRunning();
    if(string.IsNullOrWhiteSpace(game)||GameLocation.Normalize(game)==null){if(!object.Equals(Json.Get(config,"enabled"),true)){DisplayKind="";Record("设置已关闭","enabled=false");return;}DisplayKind="missing";ImportedCount=0;Error="";Record("未确定游戏目录","请运行设置游戏目录；暂未导入套装");WriteStatus(root,"未确定游戏目录，请运行设置游戏目录；暂未导入套装");return;}
    bool inGame=status.Connected&&new[]{"InProgress","GameStart","Reconnect"}.Contains(status.Phase);
    bool ended=status.Connected&&new[]{"EndOfGame","PreEndOfGame","WaitingForStats","Lobby","None","Matchmaking","ReadyCheck"}.Contains(status.Phase);
    if(ended||(!running&&status.Connected&&!inGame&&(!initialized||observedGame||(!status.IsMayhem&&installedKey!="")))){
     if(!initialized||observedGame||installedKey!=""){int deleted=ShopBuilds.Cleanup(game);Record("清理","删除文件数="+deleted);}
     installedKey="";initialized=true;observedGame=false;DisplayKind="";ImportedCount=0;Error="";WriteStatus(root,"临时套装已清理");
    }
    if(running||inGame){observedGame=true;Record("对局中保留等待重连","阶段="+status.Phase);if(!running)WriteStatus(root,"对局未结束，保留临时套装等待重连");return;}
    if(!object.Equals(Json.Get(config,"enabled"),true)){DisplayKind="";Record("设置已关闭","enabled=false");return;}
    if(status.Phase=="ChampSelect"&&!status.IsMayhem){DisplayKind="";if(!notMayhemLogged){Record("当前不是海克斯大乱斗","本次选人不导入商店方案");notMayhemLogged=true;}return;}
    if(!status.IsMayhem||status.Phase!="ChampSelect"||status.Champion<=0||DateTime.UtcNow<retryAfter)return;
    string scope="high",key=status.Champion+":"+scope+":"+data.Fingerprint;if(key==installedKey)return;
    double gap=Json.Num(Json.Get(config,"maxArchetypeGap"),.03);if(double.IsNaN(gap)||gap<0||gap>.1)throw new ArgumentException("流派差距阈值无效");
    var plans=ShopBuilds.Build(data,status.Champion,scope,gap);ShopBuilds.Install(plans,game,Path.Combine(root,"商店套装备份"));installedKey=key;ImportedCount=plans.Count;DisplayKind="success";Error="";
    Record("导入成功","英雄="+data.Lookup("champions",status.Champion)+"；套数="+plans.Count+"；写入目录="+Path.Combine(game,"Config","Global","Recommended")+"；文件="+string.Join(",",plans.Select(ShopBuilds.FileName)));
    WriteStatus(root,"已添加 "+plans.Count+" 套临时方案；对局结束后自动删除");
   }catch(Exception e){retryAfter=DateTime.UtcNow.AddSeconds(10);DisplayKind="failure";Error=e.ToString();Record("失败",Error);WriteStatus(root,"商店方案处理未完成："+e.Message);}
  }
  static void WriteStatus(string root,string message){string path=Path.Combine(root,"商店套装状态.txt");if(!File.Exists(path)||File.ReadAllText(path,Encoding.UTF8)!=message)File.WriteAllText(path,message,Encoding.UTF8);}
 }
}
