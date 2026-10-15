using System;using System.IO;using System.Text;using System.Collections.Generic;
namespace ConfidenceDesktop {
    // Main-window statistics mode (2.0.0): advanced on unless the user turned it off.
    public static class StatisticsSettings {
        public static bool Load(string path=null){try{return !object.Equals(Json.Get(Json.Read(path??CloseBehaviorSettings.PathName),"advancedMode"),false);}catch{return true;}}
        public static void Save(bool advanced,string path=null){
            path=path??CloseBehaviorSettings.PathName;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var config=File.Exists(path)?Json.Read(path) as Dictionary<string,object>:null;config=config??new Dictionary<string,object>();config["advancedMode"]=advanced;
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,Json.Write(config),Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    public static class ThemeSettings {
        public static string Load(string path=null){try{return ThemePalette.Find(Json.Str(Json.Get(Json.Read(path??CloseBehaviorSettings.PathName),"theme"))).Id;}catch{return ThemePalette.Piltover.Id;}}
        public static void Save(string id,string path=null){
            path=path??CloseBehaviorSettings.PathName;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var config=File.Exists(path)?Json.Read(path) as Dictionary<string,object>:null;config=config??new Dictionary<string,object>();config["theme"]=ThemePalette.Find(id).Id;
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,Json.Write(config),Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
