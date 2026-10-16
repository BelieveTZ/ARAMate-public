using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConfidenceDesktop {
    public sealed class OverviewEntry {
        public readonly string HeaderJson,DisplayJson;
        public readonly string[] Images;
        readonly Lazy<string[]> resources;internal readonly string SourceFile;
        public string OverviewJson {get{return resources.Value[0];}}public string IconsJson {get{return resources.Value[1];}}
        public string TooltipsJson {get{return resources.Value[2];}}public string GroupsJson {get{return resources.Value[3];}}
        public OverviewEntry(string header,string overview,string icons,string[] images,string display,string tooltips,string groups):this(header,images,display,()=>new[]{overview,icons,tooltips,groups},null){}
        internal OverviewEntry(string header,string[] images,string display,Func<string[]> load,string source){HeaderJson=header;Images=images;DisplayJson=display;resources=new Lazy<string[]>(load);SourceFile=source;}
        // Preserve the already serialized display data. Only the small live UI state is serialized on a click.
        public string State(string live,bool displayOnly=false){return live.Substring(0,live.Length-1)+","+HeaderJson.Substring(1,HeaderJson.Length-2)+",\"overview\":"+(displayOnly?DisplayJson:OverviewJson)+",\"icons\":"+(displayOnly?"{}":IconsJson)+"}";}
    }
    public static class OverviewCache {
        // Bump when overview content, display algorithms, or asset mapping changes.
        public const string OverviewCacheVersion="6";
        public static string Key(string snapshot,bool advanced,string theme,string version=AppVersion.Number,string calculation=OverviewCacheVersion){return snapshot+"|"+version+"|"+calculation+"|"+(advanced?"advanced":"simple")+"|"+theme;}
        public static string Key(DataStore data,bool advanced,ThemePalette theme){string snapshot=Json.Str(Json.Get(data.State,"versionKey"));return Key(string.IsNullOrEmpty(snapshot)?data.Fingerprint:snapshot,advanced,theme.Id);}
        static string Hash(string text){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public static string FilePath(string directory,string key,int hero){return Path.Combine(directory,Hash(key),hero+".cache");}
        // The shared lease protects active workers and atomic writes across processes.
        internal static FileStream Lease(string directory){Directory.CreateDirectory(directory);return new FileStream(Path.Combine(directory,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);}
        public static void Prune(string directory,DataStore data){
            try{
                if(!Directory.Exists(directory)||(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return;
                using(var gate=new FileStream(Path.Combine(directory,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                    var keep=new HashSet<string>(ThemePalette.All.SelectMany(theme=>new[]{true,false}.Select(mode=>Hash(Key(data,mode,theme)))),StringComparer.OrdinalIgnoreCase);
                    foreach(string folder in Directory.GetDirectories(directory)){
                        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
                        if(!keep.Contains(Path.GetFileName(folder)))TryDelete(folder);
                        else foreach(string file in Directory.GetFiles(folder,"*.tmp"))try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                    foreach(string file in Directory.GetFiles(directory,"*.tmp"))try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}
        }
        static bool HasLink(string folder){return (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0||Directory.GetDirectories(folder).Any(HasLink);}
        static void TryDelete(string folder){try{if(!HasLink(folder))Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}}
        public static bool TryRead(string directory,string key,int hero,out OverviewEntry entry){
            entry=null;FileStream lease=null;
            try{
                string file=FilePath(directory,key,hero);if(!File.Exists(file))return false;
                lease=Lease(directory);string[] lines=File.ReadAllLines(file,Encoding.UTF8);
                if(lines.Length!=6||lines[0]!=key||Hash(string.Join("\n",lines.Skip(2)))!=lines[1]||!File.Exists(ResourceFile(file)))return false;
                var header=Json.Obj(Json.Parse(lines[3]));if((int)Json.Num(Json.Get(header,"heroId"))!=hero)return false;
                var images=Json.Arr(Json.Parse(lines[2])).Select(Json.Str).ToArray();var held=lease;
                entry=new OverviewEntry(lines[3],images,lines[4],()=>{try{return ReadResources(ResourceFile(file),lines[5]);}finally{held.Dispose();}},file);lease=null;return true;
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}finally{if(lease!=null)lease.Dispose();}
            return false;
        }
        public static void Write(string directory,string key,int hero,OverviewEntry entry){
            using(var lease=Lease(directory)){WriteLocked(directory,key,hero,entry);}
        }
        static void WriteLocked(string directory,string key,int hero,OverviewEntry entry){
            string file=FilePath(directory,key,hero);Directory.CreateDirectory(Path.GetDirectoryName(file));
            if(entry.SourceFile!=null){if(Path.GetFullPath(entry.SourceFile)==Path.GetFullPath(file))return;AtomicCopy(ResourceFile(entry.SourceFile),ResourceFile(file));AtomicCopy(entry.SourceFile,file);return;}
            string resources=Json.Write(new[]{entry.OverviewJson,entry.IconsJson,entry.TooltipsJson,entry.GroupsJson});
            string resourceTemporary=ResourceFile(file)+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var stream=File.Create(resourceTemporary))using(var gzip=new GZipStream(stream,CompressionLevel.Optimal))using(var writer=new StreamWriter(gzip,new UTF8Encoding(false)))writer.Write(resources);Replace(resourceTemporary,ResourceFile(file));}
            finally{if(File.Exists(resourceTemporary))File.Delete(resourceTemporary);}
            var body=new[]{Json.Write(entry.Images),entry.HeaderJson,entry.DisplayJson,Hash(resources)};
            string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllLines(temporary,new[]{key,Hash(string.Join("\n",body))}.Concat(body),new UTF8Encoding(false));
                if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        static string ResourceFile(string file){return Path.ChangeExtension(file,"resources.gz");}
        static string[] ReadResources(string file,string expected){string text;using(var stream=File.OpenRead(file))using(var gzip=new GZipStream(stream,CompressionMode.Decompress))using(var reader=new StreamReader(gzip,Encoding.UTF8))text=reader.ReadToEnd();if(Hash(text)!=expected)throw new InvalidDataException("Overview resource checksum");var values=Json.Arr(Json.Parse(text));if(values.Length!=4||values.Any(v=>!(v is string)))throw new InvalidDataException("Overview resources");return values.Cast<string>().ToArray();}
        static void Replace(string temporary,string file){if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);}
        static void AtomicCopy(string source,string target){string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.Copy(source,temporary);Replace(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
        public static OverviewEntry LoadOrCompute(string directory,string key,int hero,Func<OverviewEntry> compute){
            OverviewEntry entry;if(TryRead(directory,key,hero,out entry))return entry;
            entry=compute();
            try{Write(directory,key,hero,entry);}catch(IOException){}catch(UnauthorizedAccessException){}return entry;
        }
        public static OverviewEntry Compute(DataStore data,int hero,bool advanced,ThemePalette palette){
            data.DisplayGroups(0,"high",false);var groups=data.DisplayGroups(hero,"high",true);var presentation=new OverviewPresentation(data,groups,hero,advanced,palette);
            string overview=Json.Write(presentation.Build());var icons=new Dictionary<string,string>();
            foreach(string asset in presentation.webIconAssets.OrderBy(a=>a,StringComparer.Ordinal)){
                string file=WebsiteData.PathUnder(data.Root,asset);if(File.Exists(file))icons[asset]="data:image/"+(asset.EndsWith(".png")?"png":"webp")+";base64,"+Convert.ToBase64String(File.ReadAllBytes(file));
            }
            string header=Json.Write(presentation.Header());var images=new HashSet<string>();CollectImages(Json.Parse(overview),images);CollectImages(Json.Parse(header),images);
            var display=Json.Parse(overview);var tooltips=new Dictionary<string,object>();SeparateTooltips(display,tooltips);
            return new OverviewEntry(header,overview,Json.Write(icons),images.OrderBy(p=>p,StringComparer.Ordinal).ToArray(),Json.Write(display),Json.Write(tooltips),WriteGroups(groups));
        }
        // Framework double parsing can round some R strings to an adjacent value.
        // Persist IEEE bits so restoring detail rows preserves every value exactly.
        static string Exact(double value){return BitConverter.DoubleToInt64Bits(value).ToString("X16",CultureInfo.InvariantCulture);}
        static double ReadExact(object value){return BitConverter.Int64BitsToDouble(long.Parse(Json.Str(value),NumberStyles.HexNumber,CultureInfo.InvariantCulture));}
        static string WriteGroups(List<Group> groups){return Json.Write(groups.Select(g=>new{path=g.Path,title=g.Title,rateTitle=g.RateTitle,gainTitle=g.GainTitle,rows=g.Rows.Select(r=>new{
            name=r.Name,note=r.Note,starter=r.StarterName,shoe=r.ShoeAdvice,win=Exact(r.Win),pick=Exact(r.Pick),lower=Exact(r.Lower),count=Exact(r.Count),gain=Exact(r.GainLower),subset=r.GainUsesSubset,overall=r.UsesOverallAugment,
            gainKind=r.GainKind,selected=Statistics.CountFields(r.GainSelected),population=Statistics.CountFields(r.GainPopulation),rank=r.Rank,oldRank=r.OldRank,raw=r.Raw}).ToArray()}).ToArray());}
        public static List<Group> ReadGroups(OverviewEntry entry){return Json.Arr(Json.Parse(entry.GroupsJson)).Select(value=>new Group{Path=NullableText(value,"path"),Title=NullableText(value,"title"),RateTitle=NullableText(value,"rateTitle"),GainTitle=NullableText(value,"gainTitle"),Rows=Json.Arr(Json.Get(value,"rows")).Select(r=>new Row{
            Name=NullableText(r,"name"),Note=NullableText(r,"note"),StarterName=NullableText(r,"starter"),ShoeAdvice=NullableText(r,"shoe"),Win=ReadExact(Json.Get(r,"win")),Pick=ReadExact(Json.Get(r,"pick")),Lower=ReadExact(Json.Get(r,"lower")),Count=ReadExact(Json.Get(r,"count")),GainLower=ReadExact(Json.Get(r,"gain")),
            GainUsesSubset=object.Equals(Json.Get(r,"subset"),true),UsesOverallAugment=object.Equals(Json.Get(r,"overall"),true),GainKind=(int)Json.Num(Json.Get(r,"gainKind")),GainSelected=Json.Get(r,"selected"),GainPopulation=Json.Get(r,"population"),Rank=(int)Json.Num(Json.Get(r,"rank")),OldRank=(int)Json.Num(Json.Get(r,"oldRank")),Raw=Json.Obj(Json.Get(r,"raw"))}).ToList()}).ToList();}
        static string NullableText(object value,string key){var text=Json.Get(value,key);return text==null?null:Json.Str(text);}
        public static Dictionary<string,object> TooltipIcons(OverviewEntry entry,string root,Dictionary<string,object> tips=null){
            var icons=Json.Obj(Json.Parse(entry.IconsJson))??new Dictionary<string,object>();tips=tips??Json.Obj(Json.Parse(entry.TooltipsJson));
            foreach(var resource in tips.Values)foreach(var block in Json.Arr(Json.Get(Json.Get(resource,"rich"),"blocks")))foreach(var span in Json.Arr(Json.Get(block,"spans"))){
                string asset=Json.Str(Json.Get(span,"icon"));if(!WebsiteData.IsTooltipAsset(asset)||icons.ContainsKey(asset))continue;
                try{string file=WebsiteData.PathUnder(root,asset);if(File.Exists(file))icons[asset]="data:image/"+(asset.EndsWith(".png")?"png":"webp")+";base64,"+Convert.ToBase64String(File.ReadAllBytes(file));}catch(IOException){}catch(UnauthorizedAccessException){}
            }return icons;
        }
        static void SeparateTooltips(object node,Dictionary<string,object> tooltips){
            var map=Json.Obj(node);if(map==null){foreach(var item in Json.Arr(node))SeparateTooltips(item,tooltips);return;}
            object tip,rich;if(map.TryGetValue("tip",out tip)&&!string.IsNullOrEmpty(Json.Str(tip))&&map.TryGetValue("rich",out rich)){
                var resource=new{tip=tip,rich=rich};string key=Hash(Json.Write(resource));tooltips[key]=resource;map["tip"]=Json.Str(tip).Split('\n')[0];map["rich"]=null;map["tipKey"]=key;
            }
            foreach(var value in map.Values)SeparateTooltips(value,tooltips);
        }
        static void CollectImages(object node,HashSet<string> paths){
            var map=Json.Obj(node);if(map!=null){foreach(var pair in map){if(pair.Key=="icon"||pair.Key=="heroIcon"){string path=Json.Str(pair.Value);if(WebsiteData.IsImageAsset("assets/"+path))paths.Add(path);}CollectImages(pair.Value,paths);}}
            else foreach(var item in Json.Arr(node))CollectImages(item,paths);
        }
        // Diagnostic command only; release scripts deliberately do not call it.
        public static int Build(DataStore data,string directory){int count=0;foreach(var hero in data.Heroes.Where(h=>h.Id>0))foreach(var theme in ThemePalette.All){var entry=Compute(data,hero.Id,true,theme);Write(directory,Key(data,true,theme),hero.Id,entry);count++;}return count;}

    }
    // One independent result cache and one worker per loaded snapshot. Requests join
    // the same task; clicked/hovered heroes precede the remaining sequential scan.
    internal sealed class OverviewPreparation : IDisposable {
        readonly DataStore data;readonly string local;readonly Func<bool> paused;
        readonly object sync=new object();readonly LinkedList<Job> jobs=new LinkedList<Job>();
        readonly Dictionary<string,Job> pending=new Dictionary<string,Job>();readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly Thread worker;volatile bool stopped;string scanning;
        sealed class Job {internal int Hero;internal bool Advanced;internal ThemePalette Theme;internal string Key,Id;internal TaskCompletionSource<OverviewEntry> Completion=new TaskCompletionSource<OverviewEntry>();}
        internal OverviewPreparation(DataStore source,string local,Func<bool> paused){data=source.CreateDisplaySnapshot();this.local=local;this.paused=paused;worker=new Thread(Run){IsBackground=true,Name="Overview preparation",Priority=ThreadPriority.BelowNormal};worker.Start();}
        internal Task<OverviewEntry> Request(int hero,bool advanced,ThemePalette theme,bool priority=true){
            string key=OverviewCache.Key(data,advanced,theme),id=key+"|"+hero;
            lock(sync){if(stopped){var canceled=new TaskCompletionSource<OverviewEntry>();canceled.SetCanceled();return canceled.Task;}Job job;if(pending.TryGetValue(id,out job)){if(priority&&jobs.Remove(job))jobs.AddFirst(job);return job.Completion.Task;}
                job=new Job{Hero=hero,Advanced=advanced,Theme=theme,Key=key,Id=id};pending[id]=job;if(priority)jobs.AddFirst(job);else jobs.AddLast(job);wake.Set();return job.Completion.Task;}
        }
        internal void PrepareAll(bool advanced,ThemePalette theme){
            string key=OverviewCache.Key(data,advanced,theme);
            lock(sync){
                if(scanning==key)return;scanning=key;
                // Drop queued work from the previous profile; an in-flight hero finishes normally.
                foreach(var job in jobs.Where(j=>j.Key!=key).ToArray()){jobs.Remove(job);pending.Remove(job.Id);job.Completion.TrySetCanceled();}
                foreach(var hero in data.Heroes.Where(h=>h.Id>0))Request(hero.Id,advanced,theme,false);
            }
        }
        void Run(){
            OverviewCache.Prune(local,data);
            FileStream lease=null;
            for(int attempt=0;attempt<20&&!stopped&&lease==null;attempt++)try{lease=OverviewCache.Lease(local);}catch(IOException){wake.WaitOne(50);}catch(UnauthorizedAccessException){break;}
            // If persistence is unavailable, requests still complete from computation.
            try{RunJobs(lease!=null);}finally{if(lease!=null)lease.Dispose();}
        }
        void RunJobs(bool persistent){
            while(!stopped){if(paused()){wake.WaitOne(1000);continue;}Job job=null;lock(sync){if(jobs.Count>0){job=jobs.First.Value;jobs.RemoveFirst();}}if(job==null){wake.WaitOne(1000);continue;}
                try{OverviewEntry entry=null;bool localHit=persistent&&OverviewCache.TryRead(local,job.Key,job.Hero,out entry);if(!localHit)entry=OverviewCache.Compute(data,job.Hero,job.Advanced,job.Theme);
                    if(!stopped){if(persistent&&!localHit)try{OverviewCache.Write(local,job.Key,job.Hero,entry);}catch(IOException){}catch(UnauthorizedAccessException){}job.Completion.TrySetResult(entry);}else job.Completion.TrySetCanceled();
                }catch(Exception error){job.Completion.TrySetException(error);}finally{lock(sync)pending.Remove(job.Id);}
            }
            lock(sync){foreach(var job in pending.Values)job.Completion.TrySetCanceled();pending.Clear();jobs.Clear();}
            wake.Dispose();
        }
        public void Dispose(){if(stopped)return;stopped=true;try{wake.Set();}catch(ObjectDisposedException){}}
    }
}

