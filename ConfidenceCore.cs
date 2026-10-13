using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ConfidenceDesktop {
    public static class Json {
        public static object Parse(string text) { return new JavaScriptSerializer { MaxJsonLength = 100000000, RecursionLimit = 200 }.DeserializeObject(text); }
        public static object Read(string file) { return Parse(File.ReadAllText(file, Encoding.UTF8)); }
        public static string Write(object value) { return new JavaScriptSerializer { MaxJsonLength = 100000000, RecursionLimit = 200 }.Serialize(value); }
        public static Dictionary<string, object> Obj(object v) { return v as Dictionary<string, object>; }
        public static object Get(object v, string key) { var d=Obj(v); return d != null && d.ContainsKey(key) ? d[key] : null; }
        public static string Str(object v) { return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double Num(object v, double fallback = double.NaN) { double n; return double.TryParse(Str(v), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : fallback; }
        public static object[] Arr(object v) { return v as object[] ?? new object[0]; }
    }
    public class Score {
        public double P, N, Lower;
        public string Note;
    }
    public static class Statistics {
        struct QuantileKey:IEquatable<QuantileKey>{
            readonly double probability,a,b;
            internal QuantileKey(double probability,double a,double b){this.probability=probability;this.a=a;this.b=b;}
            public bool Equals(QuantileKey other){return probability.Equals(other.probability)&&a.Equals(other.a)&&b.Equals(other.b);}
            public override bool Equals(object value){return value is QuantileKey&&Equals((QuantileKey)value);}
            public override int GetHashCode(){unchecked{return (probability.GetHashCode()*397^a.GetHashCode())*397^b.GetHashCode();}}
        }
        const int QuantileCacheCapacity=8192;
        static readonly object quantileLock=new object();
        static readonly Dictionary<QuantileKey,double> quantileCache=new Dictionary<QuantileKey,double>();
        public const double Z = 2.3263478740408408;
        public static double Wilson(double p, double n) {
            if(double.IsNaN(p)||double.IsNaN(n)||p<0||p>1||n<=0||n!=Math.Floor(n)) return double.NaN;
            if(p==0) return 0;
            double a=p+Z*Z/(2*n), b=Z*Math.Sqrt(p*(1-p)/n+Z*Z/(4*n*n));
            return Math.Max(0,Math.Min(1,p*p/(a+b)));
        }
        public static Score Calculate(object row) {
            double p=Json.Num(Json.Get(row,"winRate")),n=Json.Num(Json.Get(row,"sampleCount"));
            var result=new Score{P=p,N=n,Lower=double.NaN,Note="缺少有效样本数或胜率，不估算"};
            if(double.IsNaN(p)||p<0||p>1||double.IsNaN(n)||n<=0||n!=Math.Floor(n))return result;
            double count,low,high;
            if(!WinRange(row,out count,out low,out high)){result.Note="胜率与样本数不相容，含二次舍入容差后仍无整数胜场";return result;}
            double used=low/n;
            result.Note=Json.Get(row,"winCount")!=null||Json.Get(row,"wins")!=null?"接口样本数及真实胜场":"接口样本数；兼容四位及五位后转四位舍入，使用最小相容胜场="+low;
            result.Lower=Wilson(used,n);return result;
        }
        // Empirical shrinkage toward the observed baseline; not a confidence bound.
        public const double GainPriorCount=200;
        public static double PointRate(object row){double n,low,high;if(!WinRange(row,out n,out low,out high))return double.NaN;double wins=Json.Num(Json.Get(row,"winCount")??Json.Get(row,"wins"));if(double.IsNaN(wins))wins=Math.Max(low,Math.Min(high,Math.Round(Json.Num(Json.Get(row,"winRate"))*n,MidpointRounding.AwayFromZero)));return wins/n;}
        public static double Gain(object option,object hero){double rate=PointRate(option),baseline=PointRate(hero),n=Json.Num(Json.Get(option,"sampleCount"));return n/(n+GainPriorCount)*(rate-baseline);}
        // Each tail uses 0.005. Kept for evidence gates, not displayed as estimated gain.
        public static double EvidenceGain(object option,object hero){return GainBound(option,false)-GainBound(hero,true);}
        public static double SubsetGain(object option,object hero){
            double n,a0,a1,N,t0,t1;if(!WinRange(option,out n,out a0,out a1)||!WinRange(hero,out N,out t0,out t1)||n>N)return double.NaN;
            double remaining=N-n;if(Math.Max(a0,t0-remaining)>Math.Min(a1,t1))return double.NaN;
            return remaining==0?0:Gain(option,hero);
        }
        // Fixed observed mixture: pA - (q*pA + (1-q)*pB).
        public static double EvidenceSubsetGain(object option,object hero){
            double n,a0,a1,N,t0,t1;
            if(!WinRange(option,out n,out a0,out a1)||!WinRange(hero,out N,out t0,out t1)||n>N)return double.NaN;
            double remaining=N-n;
            if(remaining==0)return Math.Max(a0,t0)<=Math.Min(a1,t1)?0:double.NaN;
            double selected=Math.Max(a0,t0-remaining),total=Math.Min(t1,selected+remaining);
            if(selected>a1||total<t0||total<selected)return double.NaN;
            double other=total-selected;
            double lower=selected==0?0:BetaQuantile(.005,selected,n-selected+1);
            double upper=other==remaining?1:1-BetaQuantile(.005,remaining-other,other+1);
            return remaining/N*(lower-upper);
        }
        // Conditional on disjoint binomial groups; union of exact 97.5% one-sided bounds.
        // Optimize over every feasible integer win count allowed by published rounding.
        public static double ComparativeGain(object option,object population){
            double n,a0,a1,N,t0,t1;
            if(!WinRange(option,out n,out a0,out a1)||!WinRange(population,out N,out t0,out t1)||n>=N)return double.NaN;
            double remaining=N-n,selected=Math.Max(a0,t0-remaining);
            if(selected>Math.Min(a1,t1))return double.NaN;
            double total=Math.Min(t1,selected+remaining),other=total-selected;
            if(total<t0||other<0||other>remaining)return double.NaN;
            double lower=selected==0?0:BetaQuantile(.025,selected,n-selected+1);
            double upper=other==remaining?1:1-BetaQuantile(.025,remaining-other,other+1);
            return lower-upper;
        }
        // Compare this row directly with the hero average, using published integer-win ranges.
        public static double AverageGain(object option,object hero){
            double n,a0,a1,N,t0,t1;
            if(!WinRange(option,out n,out a0,out a1)||!WinRange(hero,out N,out t0,out t1))return double.NaN;
            double lower=a0==0?0:BetaQuantile(.025,a0,n-a0+1);
            double upper=t1==N?1:1-BetaQuantile(.025,N-t1,t1+1);
            return lower-upper;
        }
        static long combinationGainFallbackCount;
        public static long CombinationGainFallbackCount {get{return System.Threading.Interlocked.Read(ref combinationGainFallbackCount);}}
        static object CountedRow(double n,double wins){return new Dictionary<string,object>{{"sampleCount",n},{"winCount",wins},{"winRate",wins/n}};}
        static bool IntegerCounts(object row,out double n,out double wins){
            n=Json.Num(Json.Get(row,"sampleCount"));wins=Math.Round(PointRate(row)*n,MidpointRounding.AwayFromZero);
            return !double.IsNaN(wins)&&!double.IsInfinity(n)&&n>0&&n==Math.Floor(n)&&wins>=0&&wins<=n;
        }
        public static bool TryCombinationPopulation(object combination,object hero,object a,object b,out object selected,out object population,out double neither){
            selected=population=null;neither=double.NaN;
            double N,W,nA,wA,nB,wB,nAB,wAB;
            if(!IntegerCounts(hero,out N,out W)||!IntegerCounts(a,out nA,out wA)||!IntegerCounts(b,out nB,out wB)||!IntegerCounts(combination,out nAB,out wAB))return false;
            neither=N-nA-nB+nAB;double wins=W-wA-wB+wAB;
            if(nAB>nA||nAB>nB||nA>N||nB>N||wAB>wA||wAB>wB||wA-wAB>nA-nAB||wB-wAB>nB-nAB||neither<0||wins<0||wins>neither)return false;
            selected=CountedRow(nAB,wAB);population=CountedRow(nAB+neither,wAB+wins);return true;
        }
        // Compare the pair with games selecting neither augment, using exact integer wins.
        public static double CombinationGain(object combination,object hero,object a,object b){
            object selected,population;double neither;
            if(TryCombinationPopulation(combination,hero,a,b,out selected,out population,out neither))return ComparativeGain(selected,population);
            System.Threading.Interlocked.Increment(ref combinationGainFallbackCount);return ComparativeGain(combination,hero);
        }
        public static double CombinationGain(object combination,object heroRaw){
            var ids=Json.Arr(Json.Get(combination,"augmentIds")).Select(Json.Str).ToArray();
            var singles=Json.Arr(Json.Get(Json.Get(heroRaw,"augments"),"all"));
            return CombinationGain(combination,Json.Get(Json.Get(heroRaw,"champion"),"stats"),
                ids.Length==2?singles.FirstOrDefault(r=>Json.Str(Json.Get(r,"id")??Json.Get(r,"augmentId"))==ids[0]):null,
                ids.Length==2?singles.FirstOrDefault(r=>Json.Str(Json.Get(r,"id")??Json.Get(r,"augmentId"))==ids[1]):null);
        }
        static bool WinRange(object row,out double n,out double low,out double high){
            n=Json.Num(Json.Get(row,"sampleCount"));low=high=double.NaN;
            double p=Json.Num(Json.Get(row,"winRate")),wins=Json.Num(Json.Get(row,"winCount")??Json.Get(row,"wins"));
            if(double.IsNaN(n)||double.IsInfinity(n)||n<=0||n!=Math.Floor(n)||double.IsNaN(p)||p<0||p>1)return false;
            if(!double.IsNaN(wins)){low=high=wins;return wins>=0&&wins<=n&&wins==Math.Floor(wins);}
            // Union envelope for direct four-decimal or five-then-four-decimal rounding.
            low=Math.Ceiling(Math.Max(0,p-.000055)*n-1e-8);high=Math.Floor(Math.Min(1,p+.000055)*n+1e-8);return low<=high;
        }
        public static bool SummonerSubset(Group group,Row hero){
            if(group.Path!="champions/summoners"||hero.Count<=0||group.Rows.Sum(r=>r.Count)>hero.Count)return false;
            return group.Rows.All(r=>Json.Arr(Json.Get(r.Raw,"spells")).Length==2&&r.Count<=hero.Count&&!double.IsNaN(r.Pick)&&Math.Abs(r.Pick-r.Count/hero.Count)<=.00005001);
        }
        public static double GainBound(object row,bool upper){
            double n,low,high;
            if(!WinRange(row,out n,out low,out high))return double.NaN;
            double k=upper?high:low;
            if(upper)return k==n?1:1-BetaQuantile(.005,n-k,k+1);
            return k==0?0:BetaQuantile(.005,k,n-k+1);
        }
        static readonly double[] GammaCoefficients={676.5203681218851,-1259.1392167224028,771.32342877765313,-176.61502916214059,12.507343278686905,-.13857109526572012,9.9843695780195716e-6,1.5056327351493116e-7};
        static double LogGamma(double z){
            z-=1;double x=.99999999999980993;
            for(int i=0;i<GammaCoefficients.Length;i++)x+=GammaCoefficients[i]/(z+i+1);
            double t=z+7.5;return .9189385332046727+(z+.5)*Math.Log(t)-t+Math.Log(x);
        }
        static double Nonzero(double x){return Math.Abs(x)<1e-300?(x<0?-1e-300:1e-300):x;}
        static double BetaFraction(double a,double b,double x){
            double qab=a+b,qap=a+1,qam=a-1,c=1,d=1/Nonzero(1-qab*x/qap),h=d;
            for(int m=1;m<=10000;m++){
                double m2=2*m,aa=m*(b-m)*x/((qam+m2)*(a+m2));
                d=1/Nonzero(1+aa*d);c=Nonzero(1+aa/c);h*=d*c;
                aa=-(a+m)*(qab+m)*x/((a+m2)*(qap+m2));
                d=1/Nonzero(1+aa*d);c=Nonzero(1+aa/c);double delta=d*c;h*=delta;
                if(Math.Abs(delta-1)<3e-14)return h;
            }return double.NaN;
        }
        static double BetaCdf(double x,double a,double b,double logBeta){
            if(x<=0)return 0;if(x>=1)return 1;
            double factor=Math.Exp(a*Math.Log(x)+b*Math.Log(1-x)-logBeta);
            return x<(a+1)/(a+b+2)?factor*BetaFraction(a,b,x)/a:1-factor*BetaFraction(b,a,1-x)/b;
        }
        static double BetaQuantile(double probability,double a,double b){
            var key=new QuantileKey(probability,a,b);double cached;
            lock(quantileLock)if(quantileCache.TryGetValue(key,out cached))return cached;
            double lo=0,hi=1,logBeta=LogGamma(a)+LogGamma(b)-LogGamma(a+b);
            for(int i=0;i<55;i++){
                double mid=(lo+hi)/2,cdf=BetaCdf(mid,a,b,logBeta);
                if(double.IsNaN(cdf))return double.NaN;
                if(cdf<probability)lo=mid;else hi=mid;
            }
            lock(quantileLock){if(quantileCache.Count>=QuantileCacheCapacity)quantileCache.Clear();quantileCache[key]=lo;}return lo;
        }
        public static IOrderedEnumerable<T> OrderDisplayed<T>(IEnumerable<T> rows,Func<T,double> lower,Func<T,double> gain,Func<T,double> count){
            return rows.OrderByDescending(r=>double.IsNaN(lower(r))?double.NegativeInfinity:double.Parse((lower(r)*100).ToString("F1",CultureInfo.CurrentCulture),CultureInfo.CurrentCulture))
                .ThenByDescending(r=>double.IsNaN(gain(r))?double.NegativeInfinity:gain(r)).ThenByDescending(r=>double.IsNaN(lower(r))?double.NegativeInfinity:lower(r)).ThenByDescending(count);
        }
        public static string GainText(double value){if(double.IsNaN(value))return "";double points=Math.Round(value*100,1,MidpointRounding.AwayFromZero);return (points>0?"+":"")+points.ToString("F1",CultureInfo.InvariantCulture)+"%";}
        public static string Pct(double value) { return double.IsNaN(value)?"—":(value*100).ToString("F1",CultureInfo.InvariantCulture)+"%"; }
    }
    public class Row {
        public string Name, Note, StarterName, ShoeAdvice;
        public double Win, Pick, Lower, Count;
        public double GainLower=double.NaN;
        public bool GainUsesSubset, UsesOverallAugment;
        public Row OverallCopy(){var row=(Row)MemberwiseClone();row.UsesOverallAugment=true;return row;}
        public int Rank, OldRank;
        public Dictionary<string, object> Raw;
    }
    public class Group {
        public string Path, Title, RateTitle, GainTitle;
        public List<Row> Rows;
        public override string ToString() {return Title;}
    }
    public static class HeroNames {
        public static string Name(string full){full=full??"";int at=full.LastIndexOf(" · ",StringComparison.Ordinal);return at<0?full:full.Substring(at+3);}
        public static string Title(string full){full=full??"";int at=full.LastIndexOf(" · ",StringComparison.Ordinal);return at<0?"":full.Substring(0,at);}
    }
    public class Hero {
        public int Id; public string Name;
        public override string ToString(){return Name;}
    }
    public static class HeroAliases {
        static readonly Dictionary<string,object> aliases=Load();
        static Dictionary<string,object> Load(){try{return Json.Obj(Json.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"英雄别名.json")))??new Dictionary<string,object>();}catch(IOException){return new Dictionary<string,object>();}catch(ArgumentException){return new Dictionary<string,object>();}}
        static string Normalize(string value){return Regex.Replace(value??"",@"\s+","").ToLowerInvariant();}
        static bool Exact(Hero hero,string q){
            if(hero.Name.Split(new[]{" · "},StringSplitOptions.None).Any(n=>Normalize(n)==q))return true;
            object value;return aliases.TryGetValue(hero.Id.ToString(CultureInfo.InvariantCulture),out value)&&Json.Arr(value).Any(a=>Normalize(Json.Str(a))==q);
        }
        public static List<Hero> Search(IEnumerable<Hero> heroes,string query){
            string q=Normalize(query);var candidates=heroes.Where(h=>h.Id>0).ToList();if(q.Length==0)return new List<Hero>();
            var exact=candidates.Where(h=>Exact(h,q)).ToList();return exact.Count>0?exact:candidates.Where(h=>Matches(h,q)).ToList();
        }
        public static bool Matches(Hero hero,string query){
            string q=Normalize(query);if(q.Length==0)return false;
            if(Normalize(hero.Name).Contains(q))return true;
            object value;if(!aliases.TryGetValue(hero.Id.ToString(CultureInfo.InvariantCulture),out value))return false;
            return Json.Arr(value).Any(a=>Normalize(Json.Str(a))==q);
        }
    }
    public sealed class AugmentGainEntry {
        public readonly int HeroId;
        public readonly string HeroTitle,HeroName,HeroIcon,AugmentId,AugmentName,AugmentIcon,Rarity;
        public readonly double Count,Lower,GainLower;
        public readonly string[] AugmentIds;
        public readonly double SecondaryGain;
        internal AugmentGainEntry(int heroId,string title,string name,string heroIcon,string augmentId,string augmentName,string augmentIcon,string rarity,double count,double lower,double gain,string[] augmentIds=null,double secondaryGain=double.NaN){
            HeroId=heroId;HeroTitle=title;HeroName=name;HeroIcon=heroIcon;AugmentId=augmentId;AugmentName=augmentName;AugmentIcon=augmentIcon;Rarity=rarity;Count=count;Lower=lower;GainLower=gain;
            AugmentIds=augmentIds??new[]{augmentId};SecondaryGain=secondaryGain;
        }
    }
    public class DataStore {
        public const double AugmentGainBoardThreshold=.10;
        public const double CombinationGainBoardMinGain=.15,CombinationGainBoardMinSecondaryGain=.02;
        readonly string gainBoardCacheDirectory;
        readonly string gainBoardBundledFile;
        Task<GainBoards> gainBoards;
        Task<IReadOnlyList<AugmentGainEntry>> augmentGainBoard,combinationGainBoard;
        public Action GainBoardHeroComputed;
        public DataStore():this(null){}
        public DataStore(string cacheDirectory,string bundledFile=null){gainBoardCacheDirectory=cacheDirectory??AppVersion.LocalDataDirectory;gainBoardBundledFile=bundledFile??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gain-board.json");}
        // versionKey survives local snapshot-directory collisions during repair downloads.
        public string GainBoardKey {get{string versionKey=Json.Str(Json.Get(State,"versionKey"));return GainBoardCache.Key(string.IsNullOrEmpty(versionKey)?Fingerprint:SafeHash(versionKey));}}
        public Task<IReadOnlyList<AugmentGainEntry>> AugmentGainBoardAsync(){lock(cacheLock){PrepareGainBoardsAsync();return augmentGainBoard;}}
        public Task<IReadOnlyList<AugmentGainEntry>> CombinationGainBoardAsync(){lock(cacheLock){PrepareGainBoardsAsync();return combinationGainBoard;}}
        public Task<GainBoards> PrepareGainBoardsAsync(){
            lock(cacheLock){
                if(gainBoards!=null)return gainBoards;
                var snapshot=new DataStore(gainBoardCacheDirectory,gainBoardBundledFile){Root=Root,StatsRoot=StatsRoot,ResourceRoot=ResourceRoot,Version=Version,Date=Date,Fingerprint=Fingerprint,State=State,
                    Resources=new Dictionary<string,Dictionary<string,object>>(Resources),Heroes=new List<Hero>(Heroes),GainBoardHeroComputed=GainBoardHeroComputed};
                string key=GainBoardKey;
                gainBoards=Task.Run(()=>snapshot.Heroes.All(h=>h.Id<=0)?new GainBoards(new List<AugmentGainEntry>().AsReadOnly(),new List<AugmentGainEntry>().AsReadOnly()):
                    GainBoardCache.LoadOrCompute(gainBoardCacheDirectory,key,()=>snapshot.CalculateGainBoards(),gainBoardBundledFile));
                augmentGainBoard=SelectGainBoard(gainBoards,false);combinationGainBoard=SelectGainBoard(gainBoards,true);return gainBoards;
            }
        }
        static async Task<IReadOnlyList<AugmentGainEntry>> SelectGainBoard(Task<GainBoards> task,bool combination){var boards=await task.ConfigureAwait(false);return combination?boards.Combination:boards.Single;}
        internal GainBoards CalculateGainBoards(){
            var single=new List<AugmentGainEntry>();var combinations=new List<AugmentGainEntry>();
            foreach(var hero in Heroes.Where(h=>h.Id>0))foreach(bool combination in new[]{false,true}){
                var entries=combination?combinations:single;
                if(GainBoardHeroComputed!=null)GainBoardHeroComputed();
                var overall=AugmentGroups(hero.Id,"high").FirstOrDefault(g=>g.Path==(combination?"champions/augmentCombinations":"champions/augments/all"));
                if(overall==null)continue;
                var raw=combination?ReadHero(hero.Id,"high"):null;
                foreach(var row in overall.Rows.Where(r=>r.GainLower>=(combination?CombinationGainBoardMinGain:AugmentGainBoardThreshold))){
                    if(combination){
                        double secondary=CombinationShopBuilds.SecondaryGain(this,raw,row.Raw);if(!(secondary>=CombinationGainBoardMinSecondaryGain))continue;
                        var ids=CombinationShopBuilds.DisplayOrder(this,raw,Json.Arr(Json.Get(row.Raw,"augmentIds")).Select(Json.Str).ToArray());
                        entries.Add(new AugmentGainEntry(hero.Id,HeroNames.Title(hero.Name),HeroNames.Name(hero.Name),WebsiteData.ImageIcon(this,"champions",id:hero.Id.ToString()),
                            string.Join("-",ids),row.Name,null,"",row.Count,row.Lower,row.GainLower,ids,secondary));continue;
                    }
                    string id=Json.Str(Json.Get(row.Raw,"id")??Json.Get(row.Raw,"augmentId"));object resource;
                    Resources["augments"].TryGetValue(id,out resource);
                    entries.Add(new AugmentGainEntry(hero.Id,HeroNames.Title(hero.Name),HeroNames.Name(hero.Name),WebsiteData.ImageIcon(this,"champions",id:hero.Id.ToString()),
                        id,row.Name,WebsiteData.ImageIcon(this,"augments",id:id),Json.Str(Json.Get(resource,"rarity")),row.Count,row.Lower,row.GainLower));
                }
            }
            return new GainBoards(SortGainBoard(single),SortGainBoard(combinations));
        }
        static IReadOnlyList<AugmentGainEntry> SortGainBoard(List<AugmentGainEntry> entries){return entries.OrderByDescending(e=>e.GainLower).ThenByDescending(e=>e.Lower).ThenBy(e=>e.HeroName,StringComparer.CurrentCulture).ThenBy(e=>e.AugmentId,StringComparer.Ordinal).ToList().AsReadOnly();}
        const int HeroCacheCapacity=8;
        sealed class HeroData {
            internal readonly object Sync=new object();
            internal Dictionary<string,object> Raw;
            internal List<Group> Basic,Full,Display,Recommendations;
            internal int CollectedCount;
            internal LinkedListNode<string> Recency;
        }
        readonly object cacheLock=new object();
        readonly Dictionary<string,HeroData> heroCache=new Dictionary<string,HeroData>();
        readonly Dictionary<string,List<Group>> boardCache=new Dictionary<string,List<Group>>();
        readonly Dictionary<string,Dictionary<int,int>> boardRanks=new Dictionary<string,Dictionary<int,int>>();
        readonly LinkedList<string> recentHeroes=new LinkedList<string>();
        public string Root,StatsRoot,ResourceRoot,Version,Date,Fingerprint;
        public Dictionary<string,object> State;
        public Dictionary<string,Dictionary<string,object>> Resources=new Dictionary<string,Dictionary<string,object>>();
        public List<Hero> Heroes = new List<Hero>();
        public static string DefaultRoot {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"网站数据");}}
        public static readonly Dictionary<string,string> Words = new Dictionary<string,string> {
            {"champions","英雄"},{"champion","英雄基准"},{"stats","基准"},{"rows","英雄总榜"},{"summoners","召唤师技能"},{"skills","技能加点"},
            {"augments","海克斯"},{"augment","海克斯"},{"all","总体"},{"stages","阶段"},{"items","装备"},{"item","装备"},{"slots","第几件"},
            {"filtered","排除海克斯联动"},{"unfiltered","原始数据"},{"builds","构筑"},{"archetypes","流派"},{"profiles","核心出装"},{"routes","购买顺序"},
            {"starters","出门装"},{"boots","鞋子"},{"laterItems","后续装备"},{"augmentSpecific","海克斯联动"},{"augmentCombinations","海克斯组合"},
            {"itemSet","装备组合"},{"purchaseOrder","购买顺序明细"},{"utility","功能"},{"hybrid","混伤"},{"ad","物理"},{"ap","法术"},{"bruiser","战士"},{"on_hit","特效"},{"onhit","特效"},{"on-hit","特效"},{"tank","坦克"},{"fighter","战士"},{"crit","暴击"},{"lethality","穿甲"}
        };
        public static string Word(string x){return Words.ContainsKey(x)?Words[x]:x;}
        public static string SafeHash(object v) {string h=Json.Str(v);if(!Regex.IsMatch(h,"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("数据索引中的散列无效");return h;}
        public void Load(string root) {
            lock(cacheLock){gainBoards=null;augmentGainBoard=null;combinationGainBoard=null;heroCache.Clear();boardCache.Clear();boardRanks.Clear();recentHeroes.Clear();}Root=Path.GetFullPath(root);
            if(!File.Exists(Path.Combine(Root,"website-state.json"))){State=new Dictionary<string,object>();Resources.Clear();Heroes=new List<Hero>();Version=Date=Fingerprint="";StatsRoot=Path.Combine(Root,"stats");ResourceRoot=Path.Combine(Root,"resources");return;}
            State=Json.Obj(Json.Read(Path.Combine(Root,"website-state.json")));
            Version=Json.Str(Json.Get(State,"gameVersion"));Date=Json.Str(Json.Get(State,"dataDate"));
            Fingerprint=SafeHash(Json.Get(State,"snapshot"));
            string snapshot=Path.Combine(Root,"snapshots",Fingerprint);
            StatsRoot=Path.Combine(snapshot,"stats");ResourceRoot=Path.Combine(snapshot,"resources");
            Resources.Clear();
            foreach(var k in new[]{"champions","augments","items","summoner-spells","archetypes","champion-skills"}) {
                string file=Path.Combine(ResourceRoot,k+".json");Resources[k]=File.Exists(file)?Json.Obj(Json.Read(file)):new Dictionary<string,object>();
            }
            Heroes=Resources["champions"].Select(pair=>new Hero{Id=int.Parse(pair.Key),Name=Lookup("champions",pair.Key)}).OrderBy(h=>h.Name,StringComparer.CurrentCulture).ToList();
            Heroes.Insert(0,new Hero{Id=0,Name="全部英雄 · 总榜"});
        }
        public string Lookup(string kind,object id) {
            Dictionary<string,object> map;string key=Json.Str(id);
            if(Resources.TryGetValue(kind,out map)&&map.ContainsKey(key)) {
                object r=map[key];string name=kind=="archetypes"&&Words.ContainsKey(key)?Word(key):Json.Str(Json.Get(r,"name"));
                return name+(kind=="champions"?" · "+(key=="888"?"烈娜塔":Json.Str(Json.Get(r,"title"))):"");
            }return Word(kind)+" #"+key;
        }
        public string Name(object row,string path,object heroRaw=null) {
            if(Json.Get(row,"championId")!=null)return Lookup("champions",Json.Get(row,"championId"));
            if(Json.Get(row,"augmentId")!=null)return Lookup("augments",Json.Get(row,"augmentId"));
            if(Json.Get(row,"augmentIds")!=null)return string.Join(" + ",CombinationShopBuilds.DisplayOrder(this,heroRaw,Json.Arr(Json.Get(row,"augmentIds")).Select(Json.Str).ToArray()).Select(x=>Lookup("augments",x)));
            if(Json.Get(row,"spells")!=null)return string.Join(" + ",Json.Arr(Json.Get(row,"spells")).Select(x=>Lookup("summoner-spells",Json.Get(x,"id"))));
            if(Json.Get(row,"order")!=null)return Json.Str(Json.Get(row,"order"));
            foreach(string key in new[]{"purchaseOrder","itemSet","items"})if(Json.Get(row,key) is object[])return string.Join(" + ",Json.Arr(Json.Get(row,key)).Select(x=>Lookup("items",Json.Get(x,"id"))));
            if(Json.Get(row,"item")!=null)return Lookup("items",Json.Get(Json.Get(row,"item"),"id"));
            if(Json.Get(row,"key")!=null)return Word(Json.Str(Json.Get(row,"key")));
            if(Json.Get(row,"id")!=null) {
                string kind=path.Split('/').Reverse().FirstOrDefault(x=>new[]{"champions","champion","augments","items","item","augmentSpecific","itemSet","purchaseOrder"}.Contains(x))??"champions";
                if(kind=="champion")kind="champions";if(kind=="item"||kind=="itemSet"||kind=="purchaseOrder")kind="items";if(kind=="augmentSpecific")kind="augments";
                return Lookup(kind,Json.Get(row,"id"));
            }return "总体";
        }
        public object ReadHero(int id,string scope) {
            if(scope!="high"&&scope!="all")throw new ArgumentException("未知分段");
            var raw=Json.Read(id==0?Path.Combine(StatsRoot,scope,"champion-rankings.json"):Path.Combine(StatsRoot,scope,"champion-details",id+".json"));if(id>0)Json.Obj(raw)["_scope"]=scope;return raw;
        }
        public List<Group> Groups(int id,string scope){return CachedGroups(id,scope,true);}
        public List<Group> AugmentGroups(int id,string scope){return CachedGroups(id,scope,false);}
        public List<Group> DisplayGroups(int id,string scope,bool equipment){return CachedGroups(id,scope,equipment,true);}
        List<Group> BoardGroups(string scope){
            lock(cacheLock){List<Group> cached;if(boardCache.TryGetValue(scope,out cached))return cached;
                cached=Collect(ReadHero(0,scope));var board=cached.FirstOrDefault(g=>g.Path=="champions/rows");
                boardRanks[scope]=board==null?new Dictionary<int,int>():board.Rows.GroupBy(r=>(int)Json.Num(Json.Get(r.Raw,"id"),0)).ToDictionary(g=>g.Key,g=>g.First().Rank);
                foreach(var group in cached)SortDisplayedRates(group);boardCache[scope]=cached;return cached;}
        }
        HeroData HeroEntry(int id,string scope){
            lock(cacheLock){string key=scope+":"+id;HeroData entry;
                if(heroCache.TryGetValue(key,out entry)){recentHeroes.Remove(entry.Recency);recentHeroes.AddFirst(entry.Recency);return entry;}
                entry=new HeroData();entry.Recency=recentHeroes.AddFirst(key);heroCache[key]=entry;
                if(heroCache.Count>HeroCacheCapacity){string oldest=recentHeroes.Last.Value;recentHeroes.RemoveLast();heroCache.Remove(oldest);}return entry;
            }
        }
        List<Group> CachedGroups(int id,string scope,bool equipment,bool display=false){
            if(scope!="high"&&scope!="all")throw new ArgumentException("未知分段");
            if(id==0)return BoardGroups(scope);
            var entry=HeroEntry(id,scope);
            lock(entry.Sync){
                if(entry.Basic==null){
                    entry.Raw=Json.Obj(ReadHero(id,scope));
                    var basic=Collect(entry.Raw.Where(p=>p.Key!="items").ToDictionary(p=>p.Key,p=>p.Value));entry.CollectedCount=basic.Count;
                    FillAugmentStages(basic);ApplyHeroStatistics(basic,id,scope);
                    foreach(var group in basic)SortDisplayedRates(group);entry.Basic=basic;
                }
                if(!equipment)return entry.Basic;
                if(display&&entry.Display!=null)return entry.Display;
                if(!display&&entry.Full!=null)return entry.Full;
                if(entry.Raw==null)entry.Raw=Json.Obj(ReadHero(id,scope));
                if(entry.Recommendations==null){
                    var recommendations=new List<Group>{RouteRecommendations.ToGroup(this,entry.Raw)};recommendations.AddRange(EquipmentRecommendations.Build(this,entry.Raw));
                    foreach(var group in recommendations)SortDisplayedRates(group);entry.Recommendations=recommendations;
                }
                if(display){entry.Display=entry.Basic.Concat(entry.Recommendations).ToList();entry.Raw=null;return entry.Display;}
                if(entry.Full==null){
                    var items=new List<Group>();string label=entry.Raw.ContainsKey("id")?Name(entry.Raw,"champions"):null;
                    Walk(Json.Get(entry.Raw,"items"),"champions/items",label,false,items);
                    var baseline=entry.Basic.FirstOrDefault(g=>g.Path=="champions/champion/stats");
                    ApplyOptionStatistics(items,baseline==null||baseline.Rows.Count!=1?null:baseline.Rows[0]);
                    int insertion=entry.Basic.Take(entry.CollectedCount).Count(g=>g.Path=="champions");
                    foreach(var key in entry.Raw.Keys.TakeWhile(k=>k!="items")){string prefix="champions/"+key;insertion+=entry.Basic.Take(entry.CollectedCount).Count(g=>g.Path==prefix||g.Path.StartsWith(prefix+"/",StringComparison.Ordinal));}
                    var full=new List<Group>(entry.Basic);full.InsertRange(insertion,items);
                    full.AddRange(entry.Recommendations);
                    foreach(var group in items)SortDisplayedRates(group);entry.Full=full;entry.Raw=null;
                }
                return entry.Full;
            }
        }
        void ApplyHeroStatistics(List<Group> groups,int id,string scope){
            var baseline=groups.FirstOrDefault(g=>g.Path=="champions/champion/stats");
                if(baseline!=null&&baseline.Rows.Count==1){var board=BoardGroups(scope).FirstOrDefault(g=>g.Path=="champions/rows");
                    var ranked=board==null?null:board.Rows.FirstOrDefault(r=>(int)Json.Num(Json.Get(r.Raw,"id"),0)==id);
                    var row=baseline.Rows[0];row.Rank=0;row.OldRank=0;
                    if(ranked!=null&&row.Count==ranked.Count&&row.Win==ranked.Win){lock(cacheLock)row.Rank=boardRanks[scope][id];row.OldRank=ranked.OldRank;row.Note+="；名次来自同分段完整英雄总榜";}
                    else row.Note+="；总榜缺失或口径不一致，未提供排名";
                    baseline.Title="英雄在完整总榜中的排名"+(board==null?"":" / "+board.Rows.Count+" 位英雄");
                    ApplyOptionStatistics(groups.Where(g=>g!=baseline),row);
                    var singles=groups.FirstOrDefault(g=>g.Path=="champions/augments/all");
                    foreach(var group in groups.Where(g=>g.Path=="champions/augmentCombinations"||g.Path.StartsWith("champions/augmentCombinations/",StringComparison.Ordinal)))foreach(var option in group.Rows){
                        var ids=Json.Arr(Json.Get(option.Raw,"augmentIds")).Select(Json.Str).ToArray();if(ids.Length!=2)continue;
                        option.GainLower=Statistics.CombinationGain(option.Raw,row.Raw,
                            singles==null?null:singles.Rows.Where(r=>Json.Str(Json.Get(r.Raw,"id")??Json.Get(r.Raw,"augmentId"))==ids[0]).Select(r=>r.Raw).FirstOrDefault(),
                            singles==null?null:singles.Rows.Where(r=>Json.Str(Json.Get(r.Raw,"id")??Json.Get(r.Raw,"augmentId"))==ids[1]).Select(r=>r.Raw).FirstOrDefault());
                    }
                }
        }
        static void ApplyOptionStatistics(IEnumerable<Group> groups,Row baseline){
            if(baseline==null)return;
            foreach(var group in groups){bool augment=group.Path=="champions/augments/all"||group.Path.StartsWith("champions/augments/stages/",StringComparison.Ordinal);foreach(var option in group.Rows){option.GainLower=augment?Statistics.ComparativeGain(option.Raw,baseline.Raw):Statistics.AverageGain(option.Raw,baseline.Raw);option.GainUsesSubset=false;}}
        }
        public static void SortDisplayedRates(Group group){
            if(group.Path.StartsWith(EquipmentRecommendations.Prefix+"later/")||group.Path=="champions/champion/stats"||!group.Rows.Any(r=>r.Rank>0))return;
            group.Rows=Statistics.OrderDisplayed(group.Rows,r=>r.Lower,r=>r.GainLower,r=>r.Count).ToList();
            for(int i=0;i<group.Rows.Count;i++)group.Rows[i].Rank=double.IsNaN(group.Rows[i].Lower)?0:i+1;
        }
        public static void FillAugmentStages(List<Group> groups){var overall=groups.FirstOrDefault(g=>g.Path=="champions/augments/all");if(overall==null)return;for(int stage=1;stage<=4;stage++){string path="champions/augments/stages/"+stage;var group=groups.FirstOrDefault(g=>g.Path==path);if(group==null){group=new Group{Path=path,Title=new[]{"总体","一选","二选","三选","四选"}[stage],Rows=new List<Row>()};groups.Add(group);}foreach(var row in overall.Rows.Where(r=>!double.IsNaN(r.Lower)&&r.Count>0)){string id=Json.Str(Json.Get(row.Raw,"id"));if(id.Length==0)continue;var existing=group.Rows.FirstOrDefault(r=>Json.Str(Json.Get(r.Raw,"id"))==id);if(existing!=null&&!double.IsNaN(existing.Lower)&&existing.Count>0)continue;group.Rows.RemoveAll(r=>Json.Str(Json.Get(r.Raw,"id"))==id);var available=Json.Arr(Json.Get(row.Raw,"availableStages")).Select(v=>(int)Json.Num(v)).Where(v=>v>=1&&v<=4).ToArray();if(available.Length>0&&!available.Contains(stage))continue;group.Rows.Add(row.OverallCopy());}group.Rows=group.Rows.OrderByDescending(r=>double.IsNaN(r.Lower)?-1:r.Lower).ThenByDescending(r=>r.Count).ToList();for(int i=0;i<group.Rows.Count;i++)group.Rows[i].Rank=double.IsNaN(group.Rows[i].Lower)?0:i+1;}}
        public List<Group> Collect(object data) {var groups=new List<Group>();Walk(data,"champions",null,false,groups,data);return groups;}
        private void Add(IEnumerable<object> records,string path,string label,List<Group> groups,bool comparable=true,object heroRaw=null) {
            var rows=records.Select(x=>{var score=Statistics.Calculate(x);return new Row{Name=label??Name(x,path,heroRaw),Win=score.P,Count=score.N,Lower=score.Lower,Pick=Json.Num(Json.Get(x,"pickRate")),Note=score.Note,OldRank=(int)Json.Num(Json.Get(x,"rank"),0),Raw=Json.Obj(x)};})
                .OrderByDescending(x=>double.IsNaN(x.Lower)?-1:x.Lower).ThenByDescending(x=>x.Count).ToList();
            for(int i=0;i<rows.Count;i++)rows[i].Rank=!comparable||double.IsNaN(rows[i].Lower)?0:i+1;
            groups.Add(new Group{Path=path,Title=string.Join(" / ",path.Split('/').Select(Word)),Rows=rows});
        }
        private void Walk(object value,string path,string label,bool arrayMember,List<Group> groups,object heroRaw=null) {
            var array=value as object[];
            if(array!=null) {
                if(path=="champions/augmentCombinations"){
                    array=array.Where(row=>CombinationShopBuilds.IsRecommended(this,heroRaw,row)).ToArray();
                    if(array.Length==0){Add(array,path,null,groups,heroRaw:heroRaw);return;}
                }
                var rows=array.Where(x=>Json.Obj(x)!=null&&Json.Obj(x).ContainsKey("winRate")).ToList();if(rows.Count>0)Add(rows,path,null,groups,heroRaw:heroRaw);
                for(int i=0;i<array.Length;i++){string name=Name(array[i],path,heroRaw);Walk(array[i],path+"/"+(i+1)+":"+name,name,true,groups,heroRaw);}return;
            }
            var dict=Json.Obj(value);if(dict==null)return;
            if(!arrayMember&&dict.ContainsKey("winRate"))Add(new[]{value},path,label,groups,false,heroRaw);
            string childLabel=dict.ContainsKey("id")?Name(value,path,heroRaw):label;
            foreach(var pair in dict)if(pair.Value is object[]||pair.Value is Dictionary<string,object>)Walk(pair.Value,path+"/"+pair.Key,childLabel,false,groups,heroRaw);
        }
        public string Report(IEnumerable<Group> groups,string scope,string hero) {
            var text=new StringBuilder();text.AppendLine("统计学胜率排行");
            text.AppendLine("版本："+Version+" | 数据日期："+Date+" | 分段："+scope+" | 英雄："+hero);
            text.AppendLine("导出时间："+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("数据来源：https://data.aramkit.com/");
            foreach(var group in groups){
                text.AppendLine();text.AppendLine("["+group.Title+"]");text.AppendLine("新排名\t名称\t统计学胜率\t选取率\t原排名");
                foreach(var row in group.Rows)text.AppendLine(string.Join("\t",new[]{row.Rank==0?"—":row.Rank.ToString(),row.Name,Statistics.Pct(row.Lower),Statistics.Pct(row.Pick),row.OldRank.ToString()}.Select(x=>x.Replace('\t',' ').Replace('\r',' ').Replace('\n',' '))));
            }
            return text.ToString();
        }
    }
}


