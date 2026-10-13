using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text;using System.Windows.Forms;
namespace ConfidenceDesktop {
 public sealed class FocusHandoffDecision {
  IntPtr game;DateTime firstSeen;bool attempted;
  public bool Observe(IntPtr window,DateTime now,bool visible,bool minimized,int foregroundPid,int ownPid){
   if(window!=game){game=window;firstSeen=now;attempted=false;}
   if(game==IntPtr.Zero||attempted||!visible||minimized||ownPid<=0||foregroundPid!=ownPid||now<firstSeen||now-firstSeen>TimeSpan.FromSeconds(5))return false;
   attempted=true;return true;
  }
 }
 public enum GameCardHit { None,Game,OwnOverlay,Other }
 public static class GameVisibleGate {
  public static bool Allows(bool visible,bool minimized,GameCardHit[] hits){return visible&&!minimized&&hits!=null&&hits.Length==3&&hits.All(h=>h==GameCardHit.Game||h==GameCardHit.OwnOverlay);}
  // Card frames run from y=256 to y=964 at 1440h (ComboStackLayout.CardFrame).
  public static System.Drawing.Point[] CardCenters(System.Drawing.Rectangle bounds){return new[]{.32,.50,.68}.Select(x=>new System.Drawing.Point(bounds.X+(int)Math.Round(bounds.Width*x),bounds.Y+(int)Math.Round(bounds.Height*610.0/1440))).ToArray();}
 }
 public static class AutoMinimizeSettings {
  public static bool Load(string path=null){try{return !object.Equals(Json.Get(Json.Read(path??CloseBehaviorSettings.PathName),"autoMinimizeOnGame"),false);}catch{return true;}}
  public static void Save(bool value,string path=null){path=path??CloseBehaviorSettings.PathName;Directory.CreateDirectory(Path.GetDirectoryName(path));var config=File.Exists(path)?Json.Read(path) as Dictionary<string,object>:null;config=config??new Dictionary<string,object>();config["autoMinimizeOnGame"]=value;string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,Json.Write(config),Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
 public sealed class AutoMinimizeState {public bool Active,Owned,ManuallyRestored;public FormWindowState Previous;}
 public sealed class AutoMinimizeResult {public AutoMinimizeState State;public string Action="";public FormWindowState Restore;}
 public static class AutoMinimizeDecision {
  public static AutoMinimizeResult Next(AutoMinimizeState old,string phase,bool visible,FormWindowState window,bool sameScreen,bool enabled,bool manualRestore){
   var state=new AutoMinimizeState{Active=old.Active,Owned=old.Owned,ManuallyRestored=old.ManuallyRestored,Previous=old.Previous};var result=new AutoMinimizeResult{State=state};
   if(state.Owned&&(manualRestore||(visible&&window!=FormWindowState.Minimized)))state.ManuallyRestored=true;
   bool starts=phase=="GameStart"||phase=="InProgress",ends=new[]{"EndOfGame","PreEndOfGame","WaitingForStats","Lobby","None"}.Contains(phase);
   if(ends){if(state.Owned&&!state.ManuallyRestored&&visible&&window==FormWindowState.Minimized){result.Action="restore";result.Restore=state.Previous;}state.Active=state.Owned=state.ManuallyRestored=false;return result;}
   if(starts&&!state.Active){state.Active=true;if(enabled&&visible&&window!=FormWindowState.Minimized&&sameScreen){state.Previous=window;state.Owned=true;result.Action="minimize";}}
   return result;
  }
 }
}
