using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace ConfidenceDesktop {
 public static class WebsiteData {
  public const string Origin="https://data.aramkit.com/";
  public static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  public static string PathUnder(string root,string relative){
   if(string.IsNullOrEmpty(relative)||relative.Contains("..")||relative.Contains(":")||relative.StartsWith("/")||relative.Contains("\\"))throw new InvalidDataException("不安全的数据路径");
   return Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
  }
  static byte[] Fetch(string path){
   PathUnder(".",path);ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;ServicePointManager.DefaultConnectionLimit=6;
   for(int attempt=0;;attempt++)try{
    var request=(HttpWebRequest)WebRequest.Create(Origin+path);request.Method="GET";request.AllowAutoRedirect=false;request.Timeout=20000;request.ReadWriteTimeout=20000;request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;request.UserAgent="ConfidenceCompanion/1.0";
    using(var response=(HttpWebResponse)request.GetResponse()){
     if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("网站返回非成功状态");
     using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){
      byte[] buffer=new byte[32768];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){output.Write(buffer,0,count);if(output.Length>25000000)throw new InvalidDataException("单文件超过大小限制");}return output.ToArray();
     }
    }
   }catch(WebException){if(attempt>=2)throw;Thread.Sleep(400*(attempt+1));}
  }
  public static bool IsTooltipAsset(string asset){return asset!=null&&Regex.IsMatch(asset,@"\Aassets/tooltip/[A-Za-z0-9._-]+\.(webp|png)\z");}
  public static IEnumerable<string> TooltipAssets(DataStore data){var result=new HashSet<string>();foreach(var resource in data.Resources.Values)CollectTooltipAssets(resource,result);return result;}
  static void CollectTooltipAssets(object node,HashSet<string> result){var map=Json.Obj(node);if(map!=null){object asset;if(map.TryGetValue("asset",out asset)&&IsTooltipAsset(Json.Str(asset)))result.Add(Json.Str(asset));foreach(var value in map.Values)CollectTooltipAssets(value,result);}else foreach(var value in Json.Arr(node))CollectTooltipAssets(value,result);}
  public sealed class TooltipDownloadResult {public int Downloaded;public long Bytes;public Dictionary<string,string> Failures=new Dictionary<string,string>();}
  public static bool IsImageAsset(string asset){return asset!=null&&Regex.IsMatch(asset,@"\Aassets/(champion|item|augment|summoner|spell)/[A-Za-z0-9._-]+\.(webp|png)\z")&&!asset.Contains("..");}
  public static IEnumerable<string> ImageAssets(DataStore data){var result=new HashSet<string>();foreach(var resource in data.Resources.Values)CollectImageAssets(resource,result);return result;}
  static void CollectImageAssets(object node,HashSet<string> result){var map=Json.Obj(node);if(map!=null){object icon;if(map.TryGetValue("icon",out icon)&&IsImageAsset(Json.Str(icon)))result.Add(Json.Str(icon));foreach(var value in map.Values)CollectImageAssets(value,result);}else foreach(var value in Json.Arr(node))CollectImageAssets(value,result);}
  public static string ImageIcon(DataStore data,string kind,string name=null,string id=null){
   Dictionary<string,object> resources;object entry=null;
   if(kind.StartsWith("skills:")){object hero;if(data.Resources.TryGetValue("champion-skills",out resources)&&resources.TryGetValue(kind.Substring(7),out hero))entry=Json.Get(hero,name=="被动"?"P":name);}
   else if(data.Resources.TryGetValue(kind,out resources)){if(id!=null)resources.TryGetValue(id,out entry);if(entry==null)entry=resources.Values.FirstOrDefault(v=>Json.Str(Json.Get(v,"name"))==name);if(entry==null&&kind=="augments")entry=EffectText.Find(data,kind,name??"");}
   string asset=Json.Str(Json.Get(entry,"icon"));if(!IsImageAsset(asset))return null;PathUnder(data.Root??".",asset);return asset.Substring(7);
  }
  static readonly object imageDownloadGate=new object();
  public static TooltipDownloadResult FillImageIcons(DataStore data,string root=null,Func<string,byte[]> fetch=null){
   lock(imageDownloadGate){
    var result=new TooltipDownloadResult();string destination=root??data.Root;var download=fetch??Fetch;object sync=new object();
    Parallel.ForEach(ImageAssets(data),new ParallelOptions{MaxDegreeOfParallelism=6},asset=>{
     string temporary=null;
     try{string path=PathUnder(destination,asset);if(File.Exists(path))return;Directory.CreateDirectory(Path.GetDirectoryName(path));byte[] bytes=download(asset);if(bytes==null||bytes.Length==0||bytes.Length>262144)throw new InvalidDataException("图标大小无效");temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temporary,bytes);
      lock(sync){if(!File.Exists(path)){File.Move(temporary,path);result.Downloaded++;result.Bytes+=bytes.Length;}}
     }catch(Exception error){lock(sync)result.Failures[asset]=error.GetBaseException().Message;}
     finally{if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);}
    });return result;
   }
  }
  static readonly object tooltipDownloadGate=new object();
  public static TooltipDownloadResult FillTooltipIcons(DataStore data,string root=null,Func<string,byte[]> fetch=null){
   lock(tooltipDownloadGate){
   var result=new TooltipDownloadResult();root=root??data.Root;fetch=fetch??Fetch;
   foreach(string asset in TooltipAssets(data))try{string path=PathUnder(root,asset);if(File.Exists(path))continue;Directory.CreateDirectory(Path.GetDirectoryName(path));byte[] bytes=fetch(asset);if(bytes.Length==0||bytes.Length>65536)throw new InvalidDataException("图标大小无效");string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temporary,bytes);if(!File.Exists(path)){File.Move(temporary,path);result.Downloaded++;result.Bytes+=bytes.Length;}}
   catch(Exception error){result.Failures[asset]=error.GetBaseException().Message;}
   return result;
   }
  }
  static bool HasConditionalData(string root){
   try{var state=Json.Read(Path.Combine(root,"website-state.json"));string snapshot=Path.Combine(root,"snapshots",DataStore.SafeHash(Json.Get(state,"snapshot")));var files=Json.Obj(Json.Get(Json.Read(Path.Combine(snapshot,"files.json")),"files"));
    if(files==null||!files.ContainsKey("resources/champion-skills.json"))return false;foreach(string scope in new[]{"high"})foreach(var hero in Json.Arr(Json.Get(Json.Read(Path.Combine(snapshot,"stats",scope,"champion-rankings.json")),"rows")))if(!files.ContainsKey("stats/"+scope+"/champion-details/"+Json.Str(Json.Get(hero,"id"))+"-single-augments.json"))return false;return true;
   }catch{return false;}
  }
  static void DeleteVerificationStamp(string path){try{if(path!=null)File.Delete(path);}catch{}}
  static Dictionary<string,object> FileMetadata(string snapshot,IEnumerable<string> paths){
   var result=new Dictionary<string,object>();
   foreach(string relative in paths){var file=new FileInfo(PathUnder(snapshot,relative));if(!file.Exists)throw new FileNotFoundException(relative);result[relative]=new{length=file.Length,ticks=file.LastWriteTimeUtc.Ticks};}
   return result;
  }
  static void WriteVerificationStamp(string snapshot,string key,string manifestHash,Dictionary<string,object> metadata){
   try{File.WriteAllText(Path.Combine(snapshot,"verified.json"),Json.Write(new{snapshot=key,manifestHash=manifestHash,files=metadata}),Encoding.UTF8);}catch{}
  }
  public static bool Verify(string root){
   string stampPath=null;
   try{
    var state=Json.Read(Path.Combine(root,"website-state.json"));string key=DataStore.SafeHash(Json.Get(state,"snapshot"));string snapshot=Path.Combine(root,"snapshots",key);stampPath=Path.Combine(snapshot,"verified.json");
    byte[] manifest=File.ReadAllBytes(Path.Combine(snapshot,"files.json"));string manifestHash=Hash(manifest);
    var files=Json.Obj(Json.Get(Json.Parse(Encoding.UTF8.GetString(manifest).TrimStart('\uFEFF')),"files"));
    if(files==null||files.Count<9||!files.ContainsKey("resources/archetypes.json")){DeleteVerificationStamp(stampPath);return false;}
    var required=files.Where(p=>!p.Key.StartsWith("stats/all/",StringComparison.Ordinal)).ToList();
    var metadata=FileMetadata(snapshot,required.Select(p=>p.Key));
    try{
     var stamp=Json.Read(stampPath);var saved=Json.Obj(Json.Get(stamp,"files"));
     if(Json.Str(Json.Get(stamp,"snapshot"))==key&&Json.Str(Json.Get(stamp,"manifestHash"))==manifestHash&&saved!=null&&saved.Count==metadata.Count&&metadata.All(p=>saved.ContainsKey(p.Key)&&Json.Write(saved[p.Key])==Json.Write(p.Value)))return true;
    }catch{}
    foreach(var file in required)if(Hash(File.ReadAllBytes(PathUnder(snapshot,file.Key)))!=Json.Str(file.Value)){DeleteVerificationStamp(stampPath);return false;}
    WriteVerificationStamp(snapshot,key,manifestHash,metadata);return true;
   }catch{DeleteVerificationStamp(stampPath);return false;}
  }
  // Call before creating readers. Retired stores can still read their snapshot in background tasks.
  public static void PruneSnapshots(string root){
   try{
    if(!Directory.Exists(root))return;
    using(var gate=new FileStream(Path.Combine(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
     string current=DataStore.SafeHash(Json.Get(Json.Read(Path.Combine(root,"website-state.json")),"snapshot"));
     string snapshots=Path.Combine(root,"snapshots"),keep=Path.Combine(snapshots,current);
     if(!Directory.Exists(keep)||(File.GetAttributes(snapshots)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(keep)&FileAttributes.ReparsePoint)!=0)return;
     foreach(string directory in Directory.GetDirectories(snapshots)){
      if(string.Equals(Path.GetFileName(directory),current,StringComparison.OrdinalIgnoreCase))continue;
      TryDeleteSnapshotDirectory(directory);
     }
     foreach(string directory in Directory.GetDirectories(root,"pending-*"))TryDeleteSnapshotDirectory(directory);
    }
   }catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(System.Security.SecurityException){}
   catch(InvalidOperationException){}
  }
  static void TryDeleteSnapshotDirectory(string directory){
   try{
    // Do not traverse junctions or symbolic links outside the data root.
    if(HasDirectoryLink(directory))return;
    Directory.Delete(directory,true);
   }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}
  }
  static bool HasDirectoryLink(string directory){
   if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return true;
   return Directory.GetDirectories(directory).Any(HasDirectoryLink);
  }
  public sealed class UpdateInfo {
   public string CurrentVersion,CurrentDate,LatestVersion,LatestDate;public bool NeedsSync;
  }
  static object Latest(Func<string,byte[]> fetch){
   var index=Json.Parse(Encoding.UTF8.GetString(fetch("data/versions.json")));string latest=Json.Str(Json.Get(index,"latest"));
   var version=Json.Arr(Json.Get(index,"versions")).FirstOrDefault(v=>Json.Str(Json.Get(v,"version"))==latest);if(version==null)throw new InvalidDataException("网站版本索引缺少最新版本");return version;
  }
  public static UpdateInfo Check(string root,Func<string,byte[]> fetch=null){
   var version=Latest(fetch??Fetch);object state=null;try{state=Json.Read(Path.Combine(root,"website-state.json"));}catch{}
   return new UpdateInfo{CurrentVersion=state==null?"—":Json.Str(Json.Get(state,"gameVersion")),CurrentDate=state==null?"—":Json.Str(Json.Get(state,"dataDate")),LatestVersion=Json.Str(Json.Get(version,"version")),LatestDate=Json.Str(Json.Get(version,"dataDate")),NeedsSync=Json.Str(Json.Get(state,"versionKey"))!=Hash(Encoding.UTF8.GetBytes(Json.Write(version)))||!HasConditionalData(root)||!Verify(root)};
  }
  public static string Update(string root,Action<string> progress,Func<string,byte[]> fetch=null,Action<int,int> fileProgress=null){
   fetch=fetch??Fetch;Directory.CreateDirectory(root);
   using(var gate=new FileStream(Path.Combine(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    progress("正在检查网站版本…");var version=Latest(fetch);string latest=Json.Str(Json.Get(version,"version"));
    string dataPath=Json.Str(Json.Get(version,"dataPath")),resourcePath=Json.Str(Json.Get(version,"resourcePath"));
    if(!Regex.IsMatch(dataPath,@"^data/[A-Za-z0-9._-]+$")||!Regex.IsMatch(resourcePath,@"^resources/[A-Za-z0-9._-]+$"))throw new InvalidDataException("网站数据目录无效");
    string key=Hash(Encoding.UTF8.GetBytes(Json.Write(version))),statePath=Path.Combine(root,"website-state.json");
    object previous=null;try{if(File.Exists(statePath))previous=Json.Read(statePath);}catch{}
    if(Json.Str(Json.Get(previous,"versionKey"))==key&&HasConditionalData(root)&&Verify(root))return "网站数据已是最新："+latest;
    string staging=Path.Combine(root,"pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
    var hashes=new Dictionary<string,object>();object sync=new object();int finished=0,total=0;
    string reusable=null;Dictionary<string,object> oldHashes=null;
    if(Json.Str(Json.Get(previous,"versionKey"))==key)try{reusable=Path.Combine(root,"snapshots",DataStore.SafeHash(Json.Get(previous,"snapshot")));oldHashes=Json.Obj(Json.Get(Json.Read(Path.Combine(reusable,"files.json")),"files"));}catch{reusable=null;}

    Action<string,string> save=(remote,local)=>{
     byte[] bytes=null;
     if(reusable!=null&&oldHashes!=null&&oldHashes.ContainsKey(local))try{var cached=File.ReadAllBytes(PathUnder(reusable,local));if(Hash(cached)==Json.Str(oldHashes[local]))bytes=cached;}catch{}
     if(bytes==null)bytes=fetch(remote);var parsed=Json.Parse(Encoding.UTF8.GetString(bytes));if(Json.Obj(parsed)==null||Json.Obj(parsed).Count==0)throw new InvalidDataException("网站数据不是 JSON 对象："+local);
     if(local.EndsWith("-single-augments.json")&&(Json.Get(parsed,"items")==null||Json.Get(parsed,"builds")==null))throw new InvalidDataException("海克斯条件数据字段不完整："+local);
     if(local.Contains("champion-details/")&&!local.EndsWith("-single-augments.json")&&(Json.Obj(Json.Get(parsed,"champion"))==null||Json.Obj(Json.Get(parsed,"augments"))==null))throw new InvalidDataException("英雄详情字段不完整："+local);
     string destination=PathUnder(staging,local);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllBytes(destination,bytes);
     lock(sync){hashes[local]=Hash(bytes);finished++;progress("正在下载网站数据："+finished+" 个文件");if(total>0&&fileProgress!=null)fileProgress(finished,total);}
    };
    foreach(string kind in new[]{"champions","augments","items","summoner-spells","archetypes","champion-skills"})save(resourcePath+"/zh-CN/resources/"+kind+".json","resources/"+kind+".json");
    var jobs=new List<Tuple<string,string>>();
    foreach(string scope in new[]{"high"}){
     string board="stats/"+scope+"/champion-rankings.json";save(dataPath+"/"+board,board);
     var ids=Json.Arr(Json.Get(Json.Read(PathUnder(staging,board)),"rows")).Select(r=>(int)Json.Num(Json.Get(r,"id"),0)).Distinct().ToList();
     if(ids.Count<1||ids.Count>500||ids.Any(id=>id<=0))throw new InvalidDataException("英雄总榜无有效英雄");
     foreach(int id in ids){string local="stats/"+scope+"/champion-details/"+id+".json";jobs.Add(Tuple.Create(dataPath+"/"+local,local));string conditional="stats/"+scope+"/champion-details/"+id+"-single-augments.json";jobs.Add(Tuple.Create(dataPath+"/"+conditional,conditional));}
    }
    total=finished+jobs.Count;if(fileProgress!=null)fileProgress(finished,total);
    Parallel.ForEach(jobs,new ParallelOptions{MaxDegreeOfParallelism=4},job=>save(job.Item1,job.Item2));
    File.WriteAllText(Path.Combine(staging,"files.json"),Json.Write(new{files=hashes}),Encoding.UTF8);
    // Publish only a complete, parseable snapshot. Existing readers retain their old paths.
    var state=new Dictionary<string,object>{{"snapshot",key},{"versionKey",key},{"gameVersion",latest},{"dataDate",Json.Get(version,"dataDate")},{"source",Origin},{"index",version},{"downloadedAt",DateTime.UtcNow.ToString("o")}};
    string snapshots=Path.Combine(root,"snapshots");Directory.CreateDirectory(snapshots);string final=Path.Combine(snapshots,key);
    if(Directory.Exists(final)){key=Hash(Encoding.UTF8.GetBytes(key+Guid.NewGuid().ToString("N")));state["snapshot"]=key;final=Path.Combine(snapshots,key);}
    Directory.Move(staging,final);
    WriteVerificationStamp(final,key,Hash(File.ReadAllBytes(Path.Combine(final,"files.json"))),FileMetadata(final,hashes.Keys));
    string next=Path.Combine(root,"website-state.next.json");File.WriteAllText(next,Json.Write(state),Encoding.UTF8);
    if(File.Exists(statePath))File.Replace(next,statePath,null);else File.Move(next,statePath);
    return "网站更新完成："+latest+" · "+Json.Str(Json.Get(version,"dataDate"))+" · "+finished+" 个文件";
   }
  }
 }
}

