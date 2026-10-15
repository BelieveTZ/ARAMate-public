using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ConfidenceDesktop {
    public sealed class GainBoards {
        public readonly IReadOnlyList<AugmentGainEntry> Single,Combination;
        public GainBoards(IReadOnlyList<AugmentGainEntry> single,IReadOnlyList<AugmentGainEntry> combination){Single=single;Combination=combination;}
    }
    public static class GainBoardCache {
        // Increment whenever board thresholds, calculation or display ordering changes.
        public const string CalculationVersion="5";
        public static string Key(string snapshot,string version=AppVersion.Number,string calculation=CalculationVersion){return snapshot+"|"+version+"|"+calculation;}
        public static string FilePath(string directory){return Path.Combine(directory,"cache","gain-board.json");}
        public static GainBoards LoadOrCompute(string directory,string key,Func<GainBoards> compute,string bundledFile=null){
            GainBoards cached;if(TryRead(directory,key,out cached))return cached;
            if(bundledFile!=null&&TryReadFile(bundledFile,key,out cached)){Save(directory,key,cached);return cached;}
            var result=compute();Save(directory,key,result);return result;
        }
        public static bool TryRead(string directory,string key,out GainBoards boards){
            return TryReadFile(FilePath(directory),key,out boards);
        }
        public static bool TryReadFile(string file,string key,out GainBoards boards){
            boards=null;
            try{
                var document=Json.Obj(Json.Read(file));
                if(document==null||Text(document,"key")!=key)return false;
                boards=new GainBoards(ReadRows(Required(document,"single"),false),ReadRows(Required(document,"combination"),true));return true;
            }catch(Exception){boards=null;return false;}
        }
        static object Required(Dictionary<string,object> row,string field){object value;if(!row.TryGetValue(field,out value))throw new InvalidDataException(field);return value;}
        static string Text(Dictionary<string,object> row,string field,bool nullable=false){var value=Required(row,field);if(nullable&&value==null)return null;if(!(value is string))throw new InvalidDataException(field);return (string)value;}
        static double Number(Dictionary<string,object> row,string field,bool allowNaN=false){double value=double.Parse(Text(row,field),NumberStyles.Float,CultureInfo.InvariantCulture);if(double.IsInfinity(value)||(!allowNaN&&double.IsNaN(value)))throw new InvalidDataException(field);return value;}
        static IReadOnlyList<AugmentGainEntry> ReadRows(object value,bool combination){
            var array=value as object[];if(array==null)throw new InvalidDataException("rows");
            var rows=new List<AugmentGainEntry>();
            foreach(var item in array){
                var row=Json.Obj(item);if(row==null)throw new InvalidDataException("row");
                var hero=Required(row,"heroId");if(!(hero is int)||(int)hero<=0)throw new InvalidDataException("heroId");
                var ids=Required(row,"augmentIds") as object[];if(ids==null||ids.Length!=(combination?2:1)||ids.Any(valueId=>!(valueId is string)||((string)valueId).Length==0))throw new InvalidDataException("augmentIds");
                string id=Text(row,"augmentId");if(id!=string.Join("-",ids.Cast<string>()))throw new InvalidDataException("augmentId");
                double count=Number(row,"count"),lower=Number(row,"lower"),gain=Number(row,"gain"),secondary=Number(row,"secondary",!combination);
                double win=Number(row,"win",true),pick=Number(row,"pick",true);
                if(count<=0||count!=Math.Floor(count)||lower>1||lower<0||gain<(combination?DataStore.CombinationGainBoardMinGain:DataStore.AugmentGainBoardThreshold)||gain>1||(combination&&secondary<DataStore.CombinationGainBoardMinSecondaryGain))throw new InvalidDataException("statistics");
                rows.Add(new AugmentGainEntry((int)hero,Text(row,"heroTitle"),Text(row,"heroName"),Text(row,"heroIcon",true),id,Text(row,"augmentName"),Text(row,"augmentIcon",true),Text(row,"rarity"),count,lower,gain,ids.Cast<string>().ToArray(),secondary,win,pick,Counts(row,"winCounts"),Counts(row,"gainSelected"),Counts(row,"gainPopulation")));
            }
            return rows.AsReadOnly();
        }
        // Count fields for display bounds; values stay as parsed so the round trip is exact.
        static Dictionary<string,object> Counts(Dictionary<string,object> row,string field){var value=Json.Obj(Required(row,field));if(value==null||!(Json.Num(Json.Get(value,"sampleCount"))>0))throw new InvalidDataException(field);return value;}
        static string RoundTrip(double value){return value.ToString("R",CultureInfo.InvariantCulture);}
        static object[] WriteRows(IReadOnlyList<AugmentGainEntry> rows){return rows.Select(e=>(object)new{heroId=e.HeroId,heroTitle=e.HeroTitle,heroName=e.HeroName,heroIcon=e.HeroIcon,augmentId=e.AugmentId,augmentName=e.AugmentName,augmentIcon=e.AugmentIcon,rarity=e.Rarity,augmentIds=e.AugmentIds,count=RoundTrip(e.Count),lower=RoundTrip(e.Lower),gain=RoundTrip(e.GainLower),secondary=RoundTrip(e.SecondaryGain),win=RoundTrip(e.Win),pick=RoundTrip(e.Pick),winCounts=e.WinCounts,gainSelected=e.GainSelected,gainPopulation=e.GainPopulation}).ToArray();}
        public static void Save(string directory,string key,GainBoards boards){
            try{WriteFile(FilePath(directory),key,boards);}catch(Exception){}
        }
        // Build commands need write failures to propagate; runtime caches remain optional.
        public static void WriteFile(string output,string key,GainBoards boards){
            string temporary=null;
            try{
                string file=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(file));
                string mutexName;using(var hash=SHA256.Create())mutexName="Local\\ARAMate-GainCache-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(file.ToUpperInvariant()))).Replace("-","");
                using(var mutex=new Mutex(false,mutexName)){
                    bool held=false;
                    try{
                        try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Timed out waiting for gain board writer");
                        temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
                        byte[] bytes=Encoding.UTF8.GetBytes(Json.Write(new{key=key,single=WriteRows(boards.Single),combination=WriteRows(boards.Combination)}));
                        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                        if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);temporary=null;
                    }finally{if(held)mutex.ReleaseMutex();}
                }
            }finally{if(temporary!=null)try{File.Delete(temporary);}catch(Exception){}}
        }
    }
}
