using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ConfidenceDesktop {
    public class ClientStatus { public bool Connected; public int Champion,ProcessId; public int QueueId=-1; public string GameMode; public bool IsMayhem {get{return Connected&&(QueueId==2400||string.Equals(GameMode,"KIWI",StringComparison.OrdinalIgnoreCase));}} public int[] Team=new int[0]; public int[] Bench=new int[0]; public string Phase,Message; }
    public class LocalClient : IGameDirectorySource {
        private int port, pid;
        private string token,connectedDirectory;
        public string ManualDirectory;
        public bool AllowActiveLog = true;
        private bool Discover() {
            var running=Process.GetProcessesByName("LeagueClientUx");
            var ids=new HashSet<int>(running.Select(p=>p.Id));
            if(ids.Count==0){port=0;token=null;connectedDirectory=null;return false;}
            if(port>0&&ids.Contains(pid))return true;
            var dirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(!string.IsNullOrWhiteSpace(ManualDirectory))dirs.Add(ManualDirectory);
            foreach(var p in running) {try{dirs.Add(Path.GetDirectoryName(GameLocation.ProcessPath(p)));}catch{} finally{p.Dispose();}}
            string installs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Riot Games","RiotClientInstalls.json");
            try {var map=Json.Obj(Json.Get(Json.Read(installs),"associated_client"));if(map!=null)foreach(string dir in map.Keys)dirs.Add(Path.GetFullPath(dir));}catch{}
            foreach(string dir in dirs) {
                try {
                    var a=ReadHead(Path.Combine(dir,"lockfile"),4096).Trim().Split(':');int proc,p;
                    if(a.Length==5&&int.TryParse(a[1],out proc)&&ids.Contains(proc)&&int.TryParse(a[2],out p)&&p>0&&p<=65535&&a[4]=="https") {pid=proc;port=p;token=a[3];connectedDirectory=dir;return true;}
                }catch{}
            }
            // Optional, user-authorized fallback for CN clients with empty lockfiles.
            // Only logs whose filename contains a currently running UX process ID
            // are considered. Credentials remain in memory and go only to loopback.
            if(AllowActiveLog)foreach(string dir in dirs) {
                try {
                    foreach(var file in Directory.GetFiles(dir,"*LeagueClientUx.log").OrderByDescending(File.GetLastWriteTimeUtc)) {
                        int proc=ids.FirstOrDefault(id=>Path.GetFileName(file).Contains("_"+id+"_LeagueClientUx.log"));if(proc==0)continue;
                        string head=ReadHead(file,100000);
                        var p=Regex.Match(head,"--app-port[= ]+\"?([0-9]+)");
                        var t=Regex.Match(head,"--remoting-auth-token[= ]+\"?([^\"\\s]+)");
                        int n;if(p.Success&&t.Success&&int.TryParse(p.Groups[1].Value,out n)&&n>0&&n<=65535){pid=proc;port=n;token=t.Groups[1].Value;connectedDirectory=dir;return true;}
                    }
                }catch{}
            }
            return false;
        }
        private static string ReadHead(string path,int max) {
            using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var r=new StreamReader(f,Encoding.UTF8,true)){var b=new char[max];int n=r.ReadBlock(b,0,max);return new string(b,0,n);}
        }
        private string Get(string endpoint) {
            if(endpoint!="/data-store/v1/install-dir"&&endpoint!="/lol-gameflow/v1/session"&&endpoint!="/lol-gameflow/v1/gameflow-phase"&&endpoint!="/lol-champ-select/v1/current-champion"&&endpoint!="/lol-champ-select/v1/session")throw new InvalidOperationException("Only read-only allowlisted endpoints are supported");
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var req=(HttpWebRequest)WebRequest.Create("https://127.0.0.1:"+port+endpoint);
            req.Proxy=null;req.Method="GET";req.Timeout=1500;req.ReadWriteTimeout=1500;req.AllowAutoRedirect=false;req.KeepAlive=false;
            req.Headers[HttpRequestHeader.Authorization]="Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+token));
            // LCU uses a local self-signed certificate. This callback applies only
            // to this exact loopback request, never to global OS certificate trust.
            req.ServerCertificateValidationCallback=(sender,cert,chain,errors)=>req.RequestUri.Host=="127.0.0.1"&&req.RequestUri.Port==port&&cert!=null;
            try {using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return reader.ReadToEnd();}
            catch(WebException e){var response=e.Response as HttpWebResponse;if(response!=null){int status=(int)response.StatusCode;response.Dispose();if(status==404)return null;}throw;}
        }
        public string[] InstallDirectories(out string reason) {
            var dirs=new List<string>();reason="未连接客户端";
            if(port<=0||string.IsNullOrEmpty(token))return dirs.ToArray();
            if(!string.IsNullOrWhiteSpace(connectedDirectory))dirs.Add(connectedDirectory);
            if(!string.IsNullOrWhiteSpace(ManualDirectory))dirs.Add(ManualDirectory);
            try{string response=Get("/data-store/v1/install-dir");string path=response==null?null:Json.Str(Json.Parse(response));if(!string.IsNullOrWhiteSpace(path))dirs.Add(path);reason=response==null?"接口无安装目录，使用已连接客户端目录":"接口已返回，含已连接客户端目录";}
            catch(Exception error){reason="安装目录接口不可用（"+error.GetType().Name+"），使用已连接客户端目录";}
            return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public ClientStatus Poll() {
            try {
                if(!Discover())return new ClientStatus{Message="未检测到客户端；仍可手动选英雄"};
                string phase=Get("/lol-gameflow/v1/gameflow-phase");
                if(phase==null)throw new IOException();
                string p=Json.Str(Json.Parse(phase));int champion=0,queue=-1;string mode="";
                try{string flow=Get("/lol-gameflow/v1/session");if(flow!=null){var session=Json.Parse(flow);queue=ReadQueue(session);mode=ReadMode(session);}}catch{}
                if(p=="ChampSelect"||p=="InProgress"||p=="GameStart") {
                    string current=Get("/lol-champ-select/v1/current-champion");if(current!=null)int.TryParse(current.Trim(),out champion);
                }
                int[] bench=new int[0],team=new int[0]; if(p=="ChampSelect"){try{string session=Get("/lol-champ-select/v1/session");if(session!=null){var parsed=Json.Parse(session);bench=ReadBench(parsed);team=ReadTeam(parsed);}}catch{}}
                return new ClientStatus{Connected=true,QueueId=queue,GameMode=mode,Team=team,Bench=bench,Champion=champion,ProcessId=pid,Phase=p,Message=champion>0?"已识别当前英雄":"客户端已连接 · "+PhaseName(p)+" · 等待选定英雄"};
            }catch(Exception e){port=0;token=null;var web=e as WebException;return new ClientStatus{Message="客户端连接暂不可用（"+(web==null?e.GetType().Name:web.Status.ToString())+"）；可手动选英雄或指定 LeagueClient 文件夹"};}
        }
        public static int[] ReadTeam(object session){return Json.Arr(Json.Get(session,"myTeam")).OrderBy(r=>Json.Num(Json.Get(r,"cellId"),0)).Take(5).Select(r=>(int)Json.Num(Json.Get(r,"championId"),0)).ToArray();}
        public static string ReadMode(object session){var game=Json.Get(session,"gameData");return Json.Str(Json.Get(game,"gameMode")??Json.Get(Json.Get(game,"queue"),"gameMode")??Json.Get(Json.Get(session,"map"),"gameMode"));}
        public static int ReadQueue(object session){var game=Json.Get(session,"gameData");return (int)Json.Num(Json.Get(Json.Get(game,"queue"),"id")??Json.Get(game,"queueId"),-1);}
        public static int[] ReadBench(object session){return Json.Arr(Json.Get(session,"benchChampions")).Take(10).Select(x=>(int)Json.Num(Json.Get(x,"championId"),0)).ToArray();}
        public static string PhaseName(string p){switch(p){case "None":case "Lobby":return "大厅";case "Matchmaking":return "匹配中";case "ReadyCheck":return "等待确认";case "ChampSelect":return "选择英雄";case "InProgress":return "游戏中";case "GameStart":return "游戏启动";case "EndOfGame":return "对局结束";default:return p;}}
    }
}

