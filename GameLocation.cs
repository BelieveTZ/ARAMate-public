using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using Microsoft.Win32;using System.Web.Script.Serialization;using System.Runtime.InteropServices;using System.Text;
namespace ConfidenceDesktop {
 public static class ShopSettings {
  internal static readonly object Gate=new object();
  internal static Dictionary<string,object> Read(string path){return File.Exists(path)?new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path)):new Dictionary<string,object>{{"enabled",true},{"maxArchetypeGap",.03},{"gameDirectory",""}};}
  internal static void Write(string path,Dictionary<string,object> config){File.WriteAllText(path,new JavaScriptSerializer().Serialize(config),new UTF8Encoding(false));}
  public static bool Migrate(string path){lock(Gate){var config=Read(path);object enabled;bool migrated=!config.ContainsKey("userChoice")&&config.TryGetValue("enabled",out enabled)&&object.Equals(enabled,false);bool changed=config.Remove("scope")||!File.Exists(path);if(migrated){config["enabled"]=true;config["userChoice"]=false;changed=true;}if(changed)Write(path,config);return migrated;}}
  public static bool LoadEnabled(string path){lock(Gate){object enabled;var config=Read(path);return !config.TryGetValue("enabled",out enabled)||!object.Equals(enabled,false);}}
  public static void SaveEnabled(string path,bool enabled){lock(Gate){var config=Read(path);config.Remove("scope");config["enabled"]=enabled;config["userChoice"]=true;Write(path,config);}}
 }
 public interface IGameDirectorySource {string[] InstallDirectories(out string reason);}
 public sealed class GameDirectoryCandidate {public string Path;public int Priority;public DateTime ModifiedUtc;}
 public static class GameLocation {
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref int size);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  public static string ProcessPath(Process process){IntPtr handle=IntPtr.Zero;try{handle=OpenProcess(0x1000,false,process.Id);if(handle!=IntPtr.Zero){int length=32768;var name=new StringBuilder(length);if(QueryFullProcessImageName(handle,0,name,ref length))return name.ToString();}}catch{}finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}try{return process.MainModule.FileName;}catch{return null;}}
  // Lower source priority wins; timestamps and path ordering break ties deterministically.
  public static string SelectCandidate(IEnumerable<GameDirectoryCandidate> candidates){return candidates.OrderBy(c=>c.Priority).ThenByDescending(c=>c.ModifiedUtc).ThenBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(c=>c.Path).FirstOrDefault();}

  public static string Normalize(string value){try{if(string.IsNullOrWhiteSpace(value))return null;value=value.Trim(' ','"');if(File.Exists(value))value=Path.GetDirectoryName(value);if(!Directory.Exists(value))return null;foreach(var p in new[]{value,Path.Combine(value,"Game"),Path.Combine(value,"League of Legends","Game"),Path.Combine(value,"英雄联盟","Game"),Path.Combine(value,"..","Game")})if(File.Exists(Path.Combine(p,"League of Legends.exe")))return Path.GetFullPath(p);}catch{}return null;}
  public static string Detect(){return Detect(null,null);}
  public static string Detect(IGameDirectorySource client,Action<string,string> record){var candidates=new List<GameDirectoryCandidate>();var sources=new Dictionary<string,HashSet<string>>();string source="process";int priority=0,unreadable=0;string lcuReason="未连接客户端";
   foreach(var key in new[]{"process","lcu","riot","registry","drives"})sources[key]=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   Action<string> add=value=>{var path=Normalize(value);if(path!=null&&sources[source].Add(path)){DateTime modified=DateTime.MinValue;try{modified=File.GetLastWriteTimeUtc(Path.Combine(path,"League of Legends.exe"));}catch{}candidates.Add(new GameDirectoryCandidate{Path=path,Priority=priority,ModifiedUtc=modified});}};
   foreach(var name in new[]{"League of Legends","LeagueClient","LeagueClientUx"})foreach(var process in Process.GetProcessesByName(name))try{string path=ProcessPath(process);if(path==null)unreadable++;else add(path);}catch{unreadable++;}finally{process.Dispose();}
   source="lcu";priority=1;if(client!=null)foreach(var directory in client.InstallDirectories(out lcuReason))add(directory);
   source="riot";priority=2;
   try{var file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Riot Games","RiotClientInstalls.json");var obj=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string,object>;object clients;if(obj!=null&&obj.TryGetValue("associated_client",out clients)){var map=clients as Dictionary<string,object>;if(map!=null)foreach(var k in map.Keys)add(k);}}catch{}
   source="registry";foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})try{using(var root=RegistryKey.OpenBaseKey(hive,view)){
    foreach(var key in new[]{@"SOFTWARE\Tencent\LOL",@"SOFTWARE\Tencent\League of Legends",@"SOFTWARE\Riot Games\League of Legends"})using(var k=root.OpenSubKey(key)){if(k!=null)foreach(var n in new[]{"InstallPath","InstallLocation","Path","Location"})add(k.GetValue(n) as string);}
    using(var un=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){if(un!=null)foreach(var key in un.GetSubKeyNames())using(var k=un.OpenSubKey(key)){string n=Convert.ToString(k.GetValue("DisplayName"));if(n.Contains("英雄联盟")||n.IndexOf("League of Legends",StringComparison.OrdinalIgnoreCase)>=0)add(k.GetValue("InstallLocation") as string);}}
   }}catch{}
   source="drives";try{foreach(var drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady))foreach(var relative in new[]{@"Riot Games\League of Legends",@"英雄联盟",@"League of Legends",@"WeGameApps\英雄联盟",@"Applications\WeGameApps\英雄联盟",@"WeGameApps\League of Legends",@"腾讯游戏\英雄联盟",@"Games\英雄联盟",@"Program Files\Tencent\英雄联盟"})add(Path.Combine(drive.RootDirectory.FullName,relative));}catch{}
   string result=SelectCandidate(candidates);if(record!=null)record("游戏目录检测",(result==null?"失败：所有来源均无包含 League of Legends.exe 的有效目录":"成功："+result)+"；"+string.Join("；",sources.Select(entry=>entry.Key+"="+entry.Value.Count))+"；进程路径不可读="+unreadable+"；LCU="+lcuReason);return result;
  }
  public static bool SaveDetected(string settings){return SaveDetected(settings,Detect);}
  public static bool SaveDetected(string settings,Func<string> detect){
   lock(ShopSettings.Gate){object old;ShopSettings.Read(settings).TryGetValue("gameDirectory",out old);if(Normalize(Convert.ToString(old))!=null)return true;}
   var path=detect();
   // Detection may wait for LCU. Merge into the latest settings so a concurrent
   // user toggle cannot be overwritten by the old configuration.
   lock(ShopSettings.Gate){var config=ShopSettings.Read(settings);if(path==null){if(!File.Exists(settings))ShopSettings.Write(settings,config);return false;}config["gameDirectory"]=path;ShopSettings.Write(settings,config);return true;}
  }
 }
}
