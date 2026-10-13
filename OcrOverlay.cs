using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ConfidenceDesktop {
    public class TextLine { public string MatchMethod; public string Text; public RectangleF Bounds; public Rectangle Frame; }
    public class Candidate { public int Id; public string Name; public string MatchMethod; public RectangleF Title; public double ObservedWin=double.NaN; }
    public class CardReading { public List<Candidate> Cards=new List<Candidate>(); public List<Candidate> Hits=new List<Candidate>(); public string FailureReason; public int Width,Height; }
    public class CardResult { public Candidate Card; public Row Row; public Row Combination; public string CombinationText; public List<Row> Combinations=new List<Row>(); public List<string> CombinationTexts=new List<string>(); public Rectangle Bounds; public Rectangle CardFrame; public Size CaptureSize; public int HiddenCombinations; public string Context; public string SampleNote; public bool CombinationOnly; public bool HudSkill; public bool Compact; public bool BenchSlot; public int BenchIndex; public bool BenchOutside; public float BenchFont; public bool PortraitArc; public bool PlainChoice; public float ArcRadius,ArcDepth,ArcCenterX,ArcCenterY; public List<Row> Recommendations; public List<Row> SkillRecommendations; }
    public class CaptureTarget { public Rectangle Bounds; public IntPtr Handle; }
    public class LocalOcr {
        OcrEngine engine;
        public LocalOcr(){engine=OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"));if(engine==null)throw new InvalidOperationException("Windows 未安装中文 OCR。请在 Windows 语言设置中添加简体中文的文字识别组件。");}
        static async Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> operation){
            var elapsed=Stopwatch.StartNew();try{while(operation.Status==Windows.Foundation.AsyncStatus.Started){if(elapsed.ElapsedMilliseconds>5000){operation.Cancel();throw new TimeoutException("OCR timeout");}await Task.Delay(10);}return operation.GetResults();}finally{try{operation.Close();}catch{}}
        }
        public async Task<List<TextLine>> ReadInitial(Bitmap bitmap){
            var lines=new List<TextLine>();
            var header=new Rectangle((int)(bitmap.Width*.34),(int)(bitmap.Height*.10),(int)(bitmap.Width*.34),(int)(bitmap.Height*.12));
            using(var tile=new Bitmap(header.Width*2,header.Height*2)){
                using(var graphics=Graphics.FromImage(tile))graphics.DrawImage(bitmap,new Rectangle(0,0,tile.Width,tile.Height),header,GraphicsUnit.Pixel);
                foreach(var line in await Read(tile)){line.Bounds=new RectangleF(header.X+line.Bounds.X/2,header.Y+line.Bounds.Y/2,line.Bounds.Width/2,line.Bounds.Height/2);lines.Add(line);}
            }
            if(!lines.Any(l=>CardMatcher.Normalize(l.Text).Contains("选择你的英雄")))return lines;
            foreach(var frame in SelectionMatcher.FindCardFrames(bitmap)){
                var crop=new Rectangle(frame.Left+7,frame.Top+(int)(frame.Height*.76),frame.Width-14,(int)(frame.Height*.14));
                for(int variant=0;variant<3;variant++)using(var enlarged=new Bitmap(crop.Width*4,crop.Height*4)){
                    using(var graphics=Graphics.FromImage(enlarged))graphics.DrawImage(bitmap,new Rectangle(0,0,enlarged.Width,enlarged.Height),crop,GraphicsUnit.Pixel);
                    if(variant>0){var bits=enlarged.LockBits(new Rectangle(0,0,enlarged.Width,enlarged.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);try{
                        var row=new byte[enlarged.Width*4];for(int y=0;y<enlarged.Height;y++){IntPtr pointer=IntPtr.Add(bits.Scan0,y*bits.Stride);Marshal.Copy(pointer,row,0,row.Length);for(int px=0;px<row.Length;px+=4){int luminance=(row[px+2]*299+row[px+1]*587+row[px]*114)/1000;byte value=(byte)(luminance>(variant==1?150:185)?0:255);row[px]=row[px+1]=row[px+2]=value;}Marshal.Copy(row,0,pointer,row.Length);}
                    }finally{enlarged.UnlockBits(bits);}}
                    foreach(var line in await Read(enlarged)){line.Frame=frame;line.Bounds=new RectangleF(crop.X+line.Bounds.X/4,crop.Y+line.Bounds.Y/4,line.Bounds.Width/4,line.Bounds.Height/4);lines.Add(line);}
                }
            }return lines;
        }
        public async Task<List<TextLine>> ReadCandidates(Bitmap bitmap,Rectangle cachedRegion,Dictionary<string,Dictionary<string,object>> resources){
            Rectangle wide=new Rectangle(0,(int)(bitmap.Height*.25),bitmap.Width,(int)(bitmap.Height*.35));
            Rectangle region=cachedRegion.IsEmpty?wide:Rectangle.Intersect(cachedRegion,new Rectangle(Point.Empty,bitmap.Size));
            var lines=await ReadRegion(bitmap,region);
            // A previous round's narrow title crop can stop recognizing new titles.
            // Retry the proven card region before falling back to a full-frame scan.
            if(region!=wide&&CardMatcher.Find(lines,resources,bitmap.Width,bitmap.Height).Cards.Count!=3)
                {lines=MergeReadings(lines,await ReadRegion(bitmap,wide));}
            if(CardMatcher.Find(lines,resources,bitmap.Width,bitmap.Height).Cards.Count!=3){
                // The cards occupy the middle of the game view, not three screen thirds.
                // Isolate and enlarge each card to avoid cross-card line grouping and tiny glyphs.
                var isolated=new List<TextLine>();
                for(int i=0;i<3;i++){
                    var card=new Rectangle((int)((.22+i*.18)*bitmap.Width),(int)(bitmap.Height*.25),(int)(bitmap.Width*.20),(int)(bitmap.Height*.35));
                    
                    using(var tile=new Bitmap(card.Width*2,card.Height*2)){
                        using(var g=Graphics.FromImage(tile))g.DrawImage(bitmap,new Rectangle(Point.Empty,tile.Size),card,GraphicsUnit.Pixel);
                        foreach(var line in await Read(tile)){line.Bounds=new RectangleF(card.X+line.Bounds.X/2,card.Y+line.Bounds.Y/2,line.Bounds.Width/2,line.Bounds.Height/2);isolated.Add(line);}
                    }
                }
                
                lines=MergeReadings(lines,isolated);
            }
            return await RecoverSlots(bitmap,lines,resources);
        }
        public static float[] MissingSlots(CardReading reading){if(reading.Cards.Count!=2)return new float[0];float left=CardMatcher.CenterX(reading.Cards[0]),right=CardMatcher.CenterX(reading.Cards[1]),gap=right-left;return gap>reading.Width*.28?new[]{(left+right)/2}:new[]{left-gap,right+gap}.Where(x=>x>=0&&x<reading.Width).ToArray();}
        public async Task<List<TextLine>> RecoverSlots(Bitmap bitmap,List<TextLine> lines,Dictionary<string,Dictionary<string,object>> resources){
            var reading=CardMatcher.Find(lines,resources,bitmap.Width,bitmap.Height);if(reading.Cards.Count!=2)return lines;
            float gap=CardMatcher.CenterX(reading.Cards[1])-CardMatcher.CenterX(reading.Cards[0]);if(gap>bitmap.Width*.28)gap/=2;
            float top=reading.Cards.Average(c=>c.Title.Top),height=reading.Cards.Average(c=>c.Title.Height);var excluded=new HashSet<int>(reading.Cards.Select(c=>c.Id));var recovered=new List<TextLine>();
            foreach(float center in MissingSlots(reading)){
                var crop=Rectangle.Intersect(Rectangle.FromLTRB((int)(center-gap*.45),(int)(top-height*.5),(int)(center+gap*.45),(int)(top+height*2.5)),new Rectangle(Point.Empty,bitmap.Size));if(crop.Width<=0||crop.Height<=0)continue;
                int recoveredBefore=recovered.Count;var slotLines=new List<TextLine>();for(int variant=0;variant<3;variant++)using(var tile=new Bitmap(crop.Width*3,crop.Height*3,PixelFormat.Format32bppArgb)){
                    using(var g=Graphics.FromImage(tile))g.DrawImage(bitmap,new Rectangle(Point.Empty,tile.Size),crop,GraphicsUnit.Pixel);
                    if(variant>0){var bits=tile.LockBits(new Rectangle(Point.Empty,tile.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);try{var row=new byte[tile.Width*4];for(int y=0;y<tile.Height;y++){var pointer=IntPtr.Add(bits.Scan0,y*bits.Stride);Marshal.Copy(pointer,row,0,row.Length);for(int x=0;x<row.Length;x+=4){int light=(row[x+2]*299+row[x+1]*587+row[x]*114)/1000;byte value=(byte)(light>(variant==1?150:185)?0:255);row[x]=row[x+1]=row[x+2]=value;}Marshal.Copy(row,0,pointer,row.Length);}}finally{tile.UnlockBits(bits);}}
                    var local=await Read(tile);foreach(var line in local)line.Bounds=new RectangleF(crop.X+line.Bounds.X/3,crop.Y+line.Bounds.Y/3,line.Bounds.Width/3,line.Bounds.Height/3);
                    var texts=new List<TextLine>(local);foreach(var upper in local)foreach(var lower in local.Where(l=>l!=upper&&l.Bounds.Top>=upper.Bounds.Bottom&&l.Bounds.Top-upper.Bounds.Bottom<Math.Max(height,upper.Bounds.Height)*.8&&Math.Max(l.Bounds.Height,upper.Bounds.Height)<=Math.Min(l.Bounds.Height,upper.Bounds.Height)*1.3&&Math.Abs(l.Bounds.Left+l.Bounds.Width/2-upper.Bounds.Left-upper.Bounds.Width/2)<gap*.25))if(CardMatcher.Match(upper.Text,resources)==null&&CardMatcher.Match(lower.Text,resources)==null)texts.Add(new TextLine{Text=upper.Text+lower.Text,Bounds=upper.Bounds});
                    slotLines.AddRange(texts);
                }
                for(int relaxed=0;relaxed<2&&recovered.Count==recoveredBefore;relaxed++)foreach(var line in slotLines){var match=CardMatcher.MatchSlot(line.Text,resources,excluded,relaxed==1);if(match==null)continue;recovered.Add(new TextLine{Text=match.Name,MatchMethod=match.MatchMethod,Bounds=new RectangleF(line.Bounds.Left,top,line.Bounds.Width,height)});excluded.Add(match.Id);break;}
            }
            return MergeReadings(lines,recovered);
        }
        // Merge passes from one captured image only; never carry candidates between frames.
        public static List<TextLine> MergeReadings(List<TextLine> first,List<TextLine> next){
            var merged=new List<TextLine>(first);
            foreach(var line in next){
                bool duplicate=merged.Any(old=>CardMatcher.Normalize(old.Text)==CardMatcher.Normalize(line.Text)&&Math.Abs(old.Bounds.Left+old.Bounds.Width/2-line.Bounds.Left-line.Bounds.Width/2)<Math.Max(old.Bounds.Height,line.Bounds.Height)&&Math.Abs(old.Bounds.Top+old.Bounds.Height/2-line.Bounds.Top-line.Bounds.Height/2)<Math.Max(old.Bounds.Height,line.Bounds.Height));
                if(!duplicate)merged.Add(line);
            }
            return merged;
        }
        async Task<List<TextLine>> ReadRegion(Bitmap bitmap,Rectangle region){
            if(region.Width<=0||region.Height<=0)return new List<TextLine>();
            using(var tile=bitmap.Clone(region,PixelFormat.Format32bppArgb)){
                var lines=await Read(tile);foreach(var line in lines)line.Bounds=new RectangleF(line.Bounds.X+region.X,line.Bounds.Y+region.Y,line.Bounds.Width,line.Bounds.Height);return lines;
            }
        }
        public async Task<List<TextLine>> Read(Bitmap bitmap){
            double scale=Math.Min(1,2000.0/Math.Max(bitmap.Width,bitmap.Height));
            int width=Math.Max(1,(int)(bitmap.Width*scale)),height=Math.Max(1,(int)(bitmap.Height*scale));
            using(var pixels=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
                using(var g=Graphics.FromImage(pixels)){g.DrawImage(bitmap,new Rectangle(0,0,width,height));}
                var data=pixels.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                var bytes=new byte[width*height*4];
                try{for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),bytes,y*width*4,width*4);}finally{pixels.UnlockBits(data);}
                using(var writer=new DataWriter())using(var software=new SoftwareBitmap(BitmapPixelFormat.Bgra8,width,height,BitmapAlphaMode.Ignore)){
                    writer.WriteBytes(bytes);software.CopyFromBuffer(writer.DetachBuffer());
                    var recognized=await Wait(engine.RecognizeAsync(software));var lines=new List<TextLine>();
                    foreach(var line in recognized.Lines){if(line.Words.Count==0)continue;float left=(float)line.Words.Min(w=>w.BoundingRect.X),top=(float)line.Words.Min(w=>w.BoundingRect.Y),right=(float)line.Words.Max(w=>w.BoundingRect.Right),bottom=(float)line.Words.Max(w=>w.BoundingRect.Bottom);
                        lines.Add(new TextLine{Text=line.Text,Bounds=RectangleF.FromLTRB((float)(left/scale),(float)(top/scale),(float)(right/scale),(float)(bottom/scale))});}
                    return lines;
                }
            }
        }
    }
    public sealed class TitleStamp {
        readonly Rectangle area;readonly bool[] pixels;
        public TitleStamp(Bitmap image,RectangleF bounds){area=Rectangle.Intersect(new Rectangle((int)bounds.X-3,(int)bounds.Y-3,(int)bounds.Width+6,(int)bounds.Height+6),new Rectangle(Point.Empty,image.Size));pixels=Sample(image);}
        bool[] Sample(Bitmap image){var sample=new bool[1024];if(area.Width<1||area.Height<1)return sample;for(int y=0;y<16;y++)for(int x=0;x<64;x++){int px=Math.Min(image.Width-1,area.X+x*area.Width/64),py=Math.Min(image.Height-1,area.Y+y*area.Height/16);var c=image.GetPixel(px,py);sample[y*64+x]=(c.R*299+c.G*587+c.B*114)>150000;}return sample;}
        public bool Matches(Bitmap image){var other=Sample(image);int ink=0,diff=0;for(int i=0;i<pixels.Length;i++){if(pixels[i]||other[i])ink++;if(pixels[i]!=other[i])diff++;}return ink>20&&diff<=ink*.40;}
    }
    public static class CardMatcher {
        public static string Normalize(string value){return Regex.Replace(value.Normalize(NormalizationForm.FormKC),@"\s+","");}
        public static string NormalizeName(string value){return Regex.Replace(Normalize(value),@"[\p{P}\p{S}]","");}
        public static int Distance(string a,string b){var previous=Enumerable.Range(0,b.Length+1).ToArray();for(int i=1;i<=a.Length;i++){var next=new int[b.Length+1];next[0]=i;for(int j=1;j<=b.Length;j++)next[j]=Math.Min(Math.Min(next[j-1]+1,previous[j]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));previous=next;}return previous[b.Length];}
        sealed class NameEntry {public int Id;public string Name,Original,Clean;public bool Safe;}
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dictionary<string,object>,List<NameEntry>> nameCache=new System.Runtime.CompilerServices.ConditionalWeakTable<Dictionary<string,object>,List<NameEntry>>();
        static List<NameEntry> Names(Dictionary<string,Dictionary<string,object>> resources){return nameCache.GetValue(resources["augments"],source=>{var entries=source.Select(p=>new NameEntry{Id=int.Parse(p.Key),Name=Json.Str(Json.Get(p.Value,"name"))}).Where(x=>x.Name.Length>1).ToList();foreach(var entry in entries){entry.Original=Normalize(entry.Name);entry.Clean=NormalizeName(entry.Name);}var collisions=new HashSet<string>(entries.GroupBy(x=>x.Clean).Where(g=>g.Select(x=>x.Id).Distinct().Count()>1).Select(g=>g.Key));foreach(var entry in entries)entry.Safe=!collisions.Contains(entry.Clean);return entries;});}
        public static Candidate Match(string text,Dictionary<string,Dictionary<string,object>> resources){
            var names=Names(resources);string original=Normalize(text),clean=NormalizeName(text);
            var exact=names.FirstOrDefault(x=>x.Original==original);if(exact!=null)return new Candidate{Id=exact.Id,Name=exact.Name,MatchMethod="exact"};
            var safe=names.Where(x=>x.Safe).ToList();
            exact=safe.FirstOrDefault(x=>x.Clean==clean);if(exact!=null)return new Candidate{Id=exact.Id,Name=exact.Name,MatchMethod="exact"};
            var distances=safe.Where(x=>x.Clean.Length>=4&&Math.Abs(x.Clean.Length-clean.Length)<=2).Select(x=>new{Entry=x,Length=x.Clean.Length,Distance=Distance(clean,x.Clean)}).OrderBy(x=>x.Distance).ToList();
            if(distances.Count>0){var best=distances[0];if(best.Distance<=(best.Length<=6?1:best.Length/4)&&(distances.Count==1||best.Distance<distances[1].Distance))return new Candidate{Id=best.Entry.Id,Name=best.Entry.Name,MatchMethod="fuzzy("+best.Distance+")"};}
            var contained=safe.Where(x=>x.Clean.Length>=4&&clean.Contains(x.Clean)).OrderByDescending(x=>x.Clean.Length).ToList();
            if(contained.Count>0&&(contained.Count==1||NormalizeName(contained[0].Name).Length>NormalizeName(contained[1].Name).Length))return new Candidate{Id=contained[0].Id,Name=contained[0].Name,MatchMethod="contains"};return null;
        }
        public static Candidate MatchSlot(string text,Dictionary<string,Dictionary<string,object>> resources,ISet<int> excluded,bool allowRelaxed=true){
            var match=Match(text,resources);if(match!=null){if(excluded.Contains(match.Id))return null;match.MatchMethod="slot/"+match.MatchMethod;return match;}
            string clean=NormalizeName(text);if(!allowRelaxed||clean.Length<=3)return null;
            var ranked=Names(resources).Where(n=>n.Safe&&n.Clean.Length>=4).Select(n=>new{Entry=n,Distance=Distance(clean,n.Clean)}).OrderBy(n=>n.Distance).ToList();if(ranked.Count==0)return null;var best=ranked[0];
            if(excluded.Contains(best.Entry.Id)||best.Distance>(best.Entry.Clean.Length>=8?3:best.Entry.Clean.Length>=5?2:1)||(ranked.Count>1&&ranked[1].Distance-best.Distance<2))return null;
            return new Candidate{Id=best.Entry.Id,Name=best.Entry.Name,MatchMethod="slot/fuzzy("+best.Distance+")"};
        }
        public static CardReading Find(List<TextLine> lines,Dictionary<string,Dictionary<string,object>> resources,int width,int height){
            var result=new CardReading{Width=width,Height=height};var hits=result.Hits;
            var eligible=lines.Where(l=>l.Bounds.Height>=12&&l.Bounds.Y<height*.85).OrderBy(l=>l.Bounds.Top).ToList();var used=new HashSet<TextLine>();Func<TextLine,bool> exactAlone=l=>{var m=Match(l.Text,resources);return m!=null&&m.MatchMethod=="exact";};
            foreach(var line in eligible){if(used.Contains(line)||exactAlone(line))continue;
                foreach(var below in eligible.Where(l=>!used.Contains(l)&&l!=line&&Math.Max(l.Bounds.Height,line.Bounds.Height)<=Math.Min(l.Bounds.Height,line.Bounds.Height)*1.3&&!exactAlone(l)&&l.Bounds.Top>=line.Bounds.Bottom&&l.Bounds.Top-line.Bounds.Bottom<line.Bounds.Height*.8&&Math.Abs(l.Bounds.Left+l.Bounds.Width/2-line.Bounds.Left-line.Bounds.Width/2)<line.Bounds.Width*.5)){
                    var merged=Match(line.Text+below.Text,resources);if(merged==null)continue;merged.Title=line.Bounds;merged.MatchMethod="merged/"+merged.MatchMethod;hits.Add(merged);used.Add(line);used.Add(below);break;
                }
            }
            foreach(var line in eligible){if(used.Contains(line))continue;var match=Match(line.Text,resources);if(match!=null){match.Title=line.Bounds;if(line.MatchMethod!=null)match.MatchMethod=line.MatchMethod;hits.Add(match);}}
            double best=0;
            foreach(var anchor in hits){var row=hits.Where(x=>Math.Abs(CenterY(x)-CenterY(anchor))<Math.Max(x.Title.Height,anchor.Title.Height)*.75).OrderBy(x=>CenterX(x)).ToList();
                for(int i=0;i+2<row.Count;i++){
                    var triple=row.Skip(i).Take(3).ToList();double a=CenterX(triple[1])-CenterX(triple[0]),b=CenterX(triple[2])-CenterX(triple[1]);
                    if(triple.Select(x=>x.Id).Distinct().Count()!=3||a<width*.09||b<width*.09||a/b<.6||a/b>1.67)continue;
                    double size=triple.Average(x=>x.Title.Height);if(triple.Max(x=>x.Title.Height)>triple.Min(x=>x.Title.Height)*1.6)continue;
                    double score=size+(height-triple.Average(x=>x.Title.Y))/height;
                    if(score>best){best=score;result.Cards=triple;}
                }
            }
            if(result.Cards.Count!=3){
                var reasons=new List<string>();if(hits.Select(c=>c.Id).Distinct().Count()<3)reasons.Add("命中数不足");else{
                    bool sameRow=false,spacing=false;foreach(var anchor in hits){var row=hits.Where(x=>Math.Abs(CenterY(x)-CenterY(anchor))<Math.Max(x.Title.Height,anchor.Title.Height)*.75).OrderBy(CenterX).ToList();for(int i=0;i+2<row.Count;i++){var triple=row.Skip(i).Take(3).ToList();if(triple.Select(x=>x.Id).Distinct().Count()!=3)continue;sameRow=true;double a=CenterX(triple[1])-CenterX(triple[0]),b=CenterX(triple[2])-CenterX(triple[1]);if(a>=width*.09&&b>=width*.09&&a/b>=.6&&a/b<=1.67)spacing=true;}}
                    if(!sameRow)reasons.Add("不同行");else if(!spacing)reasons.Add("间距");else reasons.Add("字高比");}result.FailureReason=string.Join(" / ",reasons);
                best=0;foreach(var left in hits)foreach(var right in hits){if(left.Id==right.Id||CenterX(right)-CenterX(left)<width*.09||Math.Abs(CenterY(left)-CenterY(right))>=Math.Max(left.Title.Height,right.Title.Height)*.75||Math.Max(left.Title.Height,right.Title.Height)>Math.Min(left.Title.Height,right.Title.Height)*1.6)continue;double score=(left.Title.Height+right.Title.Height)/2+(height-(left.Title.Y+right.Title.Y)/2)/height;if(score>best){best=score;result.Cards=new List<Candidate>{left,right};}}
            }
            if(result.Cards.Count<2)return result;
            double gap=(CenterX(result.Cards.Last())-CenterX(result.Cards[0]))/(result.Cards.Count-1);
            foreach(var card in result.Cards){
                foreach(var line in lines.Where(l=>l.Bounds.Top>card.Title.Bottom&&Math.Abs(l.Bounds.X+l.Bounds.Width/2-CenterX(card))<gap*.38).OrderBy(l=>l.Bounds.Y)){
                    if(Normalize(line.Text).Contains("统计学胜率"))continue;
                    var m=Regex.Match(Normalize(line.Text),@"胜率([0-9]{1,2}(?:\.[0-9]{1,3})?)%");
                    if(m.Success){card.ObservedWin=double.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)/100;break;}
                }
            }
            return result;
        }
        public static float CenterX(Candidate c){return c.Title.Left+c.Title.Width/2;}
        static float CenterY(Candidate c){return c.Title.Top+c.Title.Height/2;}
        public static void AddCombination(CardResult result,DataStore data,int hero,string scope){
            var group=data.AugmentGroups(hero,scope).FirstOrDefault(g=>g.Path=="champions/augmentCombinations");
            AddCombinations(result,group==null?Enumerable.Empty<Row>():group.Rows,id=>data.Lookup("augments",id));
        }
        public static void AddCombinations(CardResult result,IEnumerable<Row> rows,Func<string,string> partnerName){
            result.Combinations=new List<Row>();result.CombinationTexts=new List<string>();result.Combination=null;result.CombinationText=null;
            string cardId=result.Card.Id.ToString();var partners=new HashSet<string>();
            foreach(var row in rows.Where(r=>!double.IsNaN(r.Lower)&&Json.Arr(Json.Get(r.Raw,"augmentIds")).Length==2&&Json.Arr(Json.Get(r.Raw,"augmentIds")).Any(id=>Json.Str(id)==cardId))
                .OrderByDescending(r=>r.Lower).ThenByDescending(r=>r.GainLower).ThenByDescending(r=>r.Count)){
                string partner=Json.Arr(Json.Get(row.Raw,"augmentIds")).Select(Json.Str).FirstOrDefault(id=>id!=cardId);
                if(partner==null||!partners.Add(partner))continue;
                result.Combinations.Add(row);result.CombinationTexts.Add(partnerName(partner));
            }
            if(result.Combinations.Count>0){result.Combination=result.Combinations[0];result.CombinationText=result.CombinationTexts[0];}
        }
        public static int InferStage(CardReading reading,object hero){
            var candidates=new List<int>();
            for(int stage=0;stage<=4;stage++){
                object source=stage==0?Json.Get(Json.Get(hero,"augments"),"all"):Json.Get(Json.Get(Json.Get(hero,"augments"),"stages"),stage.ToString());
                int matches=0;bool mismatch=false;
                foreach(var c in reading.Cards.Where(c=>!double.IsNaN(c.ObservedWin))){var row=Json.Arr(source).FirstOrDefault(x=>(int)Json.Num(Json.Get(x,"id"),0)==c.Id);if(row==null||Math.Abs(Json.Num(Json.Get(row,"winRate"))-c.ObservedWin)>.000051)mismatch=true;else matches++;}
                if(!mismatch&&matches>=2)candidates.Add(stage);
            }
            return candidates.Count==1?candidates[0]:-1;
        }
        public static List<CardResult> Rank(CardReading reading,DataStore store,int heroId,string scope,int stage,string provenance){
            if(reading.Cards.Count<2)return new List<CardResult>();
            var all=store.AugmentGroups(heroId,scope);string suffix=stage==0?"/augments/all":"/augments/stages/"+stage;
            var group=all.FirstOrDefault(g=>g.Path.EndsWith(suffix,StringComparison.Ordinal));
            var overall=stage>=1&&stage<=4?all.FirstOrDefault(g=>g.Path.EndsWith("/augments/all",StringComparison.Ordinal)):null;
            float gap=(CenterX(reading.Cards.Last())-CenterX(reading.Cards[0]))/(reading.Cards.Count-1);if(reading.Cards.Count==2)gap=Math.Min(gap,reading.Width*.18f);
            float titleHeight=reading.Cards.Average(c=>c.Title.Height);
            // Align equal-size badges above the standard three-card frame.
            int boxWidth=(int)Math.Min(gap*.86,400),boxHeight=126;
            int badgeTop=Math.Max(0,(int)(reading.Cards.Min(c=>c.Title.Top)-gap*.72)-boxHeight-8);
            return reading.Cards.Select(c=>{var row=group==null?null:group.Rows.FirstOrDefault(r=>(int)Json.Num(Json.Get(r.Raw,"id"),0)==c.Id);if(row==null&&overall!=null){var fallback=overall.Rows.FirstOrDefault(r=>(int)Json.Num(Json.Get(r.Raw,"id"),0)==c.Id);if(fallback!=null)row=fallback.OverallCopy();}return new CardResult{Card=c,Row=row,CardFrame=ComboStackLayout.CardFrame(CenterX(c),new Size(reading.Width,reading.Height)),CaptureSize=new Size(reading.Width,reading.Height),Bounds=new Rectangle(Math.Max(0,Math.Min(reading.Width-boxWidth,(int)(CenterX(c)-boxWidth/2))),badgeTop,boxWidth,boxHeight),Context=string.Join(" · ",new[]{stage>=1&&stage<=4?new[]{"总体","一选","二选","三选","四选"}[stage]:"总体",provenance}.Where(x=>!string.IsNullOrEmpty(x)))};}).ToList();
        }
    }
    public sealed class ComboStackLayout {
        public Rectangle[] Rectangles;
        public int HiddenCount;
        // Frame edges measured on the standard in-game three-choice screen.
        public static Rectangle CardFrame(float centerX,Size screen){
            return Rectangle.FromLTRB((int)Math.Floor(centerX-screen.Height*215.0/1440),
                (int)Math.Floor(screen.Height*256.0/1440),
                (int)Math.Ceiling(centerX+screen.Height*215.0/1440),(int)Math.Ceiling(screen.Height*964.0/1440));
        }
        public const int RowHeight=20;
        static Rectangle Scaled(Rectangle r,Size screen){
            double scale=screen.Height/1440.0;double center=screen.Width/2.0;
            return Rectangle.FromLTRB((int)Math.Floor(center+(r.Left-1280)*scale),(int)Math.Floor(r.Top*scale),
                (int)Math.Ceiling(center+(r.Right-1280)*scale),(int)Math.Ceiling(r.Bottom*scale));
        }
        public static Rectangle RefreshBounds(Rectangle card,Size screen){
            double scale=screen.Height/1440.0,center=card.Left+card.Width/2.0+6*scale;
            return Rectangle.FromLTRB((int)Math.Floor(center-48*scale),(int)Math.Floor(990*scale),
                (int)Math.Ceiling(center+48*scale),(int)Math.Ceiling(1048*scale));
        }
        // Conservative envelopes of the HUD panels measured on the real 1440p capture.
        // Keep the buff icons separate: they matter only when the column crosses their x range.
        public static Rectangle[] HudBounds(Size screen){
            return new[]{new Rectangle(744,1313,180,127),new Rectangle(882,1306,118,134),
                new Rectangle(1000,1304,576,136),new Rectangle(1576,1306,32,134),
                new Rectangle(1002,1269,70,39),new Rectangle(2190,1070,370,370)}.Select(r=>Scaled(r,screen)).ToArray();
        }
        public static ComboStackLayout Calculate(Rectangle card,Rectangle main,Size screen,int count,int height,Rectangle refresh,IEnumerable<Rectangle> hud){
            if(screen.Width<=0||screen.Height<=0||height<=0||count<0)throw new ArgumentOutOfRangeException();
            const int margin=8,gap=6;
            int width=Math.Min(main.Width,screen.Width-2*margin);
            int x=Math.Max(margin,Math.Min(screen.Width-margin-width,main.Left));
            int upperEnd=Math.Min(card.Top,main.Top)-gap;
            int lowerStart=Math.Max(Math.Max(card.Bottom,main.Bottom),refresh.Bottom)+gap;
            int lowerEnd=Math.Min(screen.Height-margin,(hud??Enumerable.Empty<Rectangle>())
                .Where(r=>r.Left<x+width&&r.Right>x).Select(r=>r.Top-gap).DefaultIfEmpty(screen.Height-margin).Min());
            int upper=Math.Max(0,upperEnd-margin),lower=Math.Max(0,lowerEnd-lowerStart);
            int minimum=(int)Math.Ceiling(height*.85),actual=height;
            bool allBelow=count<=lower/height;
            if(allBelow&&count>0)actual=Math.Min(Math.Max(height,(int)Math.Floor(30.0*screen.Height/1440)),lower/count);
            if(!allBelow)while(actual>minimum&&upper/actual+lower/actual<count)actual--;
            int capacity=allBelow?lower/actual:upper/actual+lower/actual;
            int shown=Math.Min(count,capacity),hidden=count>capacity?count-Math.Max(0,shown-1):0;
            int above=allBelow?0:Math.Min(shown,upper/actual);
            var rectangles=new Rectangle[shown];
            for(int i=0;i<shown;i++)rectangles[i]=new Rectangle(x,i<above?upperEnd-(i+1)*actual:lowerStart+(i-above)*actual,width,actual);
            return new ComboStackLayout{Rectangles=rectangles,HiddenCount=hidden};
        }
    }

    public sealed class PartialCardGate {
        string key;int count;
        public void Reset(){key=null;count=0;}
        public bool Observe(CardReading reading,string context){if(reading.Cards.Count!=2){Reset();return false;}string next=context+":"+string.Join(",",reading.Cards.Select(c=>c.Id).OrderBy(id=>id));count=next==key?count+1:1;key=next;return count>=2;}
    }
    public sealed class AugmentOcrDiagnostics {
        string stateCategory;DateTime stateAt=DateTime.MinValue,stateCheckAt=DateTime.MinValue,suspectAt=DateTime.MinValue;
        public static string StateCategory(string state){return state=="skip-by-stamp"||state.StartsWith("ocr:lines=")?"ocr:running":state;}
        public bool StateCheckDue(string state,DateTime now){return StateCategory(state)!=stateCategory||((now-stateAt).TotalSeconds>=30&&(now-stateCheckAt).TotalSeconds>=30);}
        public bool ShouldWriteState(string state,Func<bool> gameExists,DateTime now){if(!StateCheckDue(state,now))return false;stateCheckAt=now;string category=StateCategory(state);if(category==stateCategory&&!gameExists())return false;stateCategory=category;stateAt=now;return true;}
        public void State(string state,Func<string> detail,Func<bool> gameExists,List<TextLine> lines=null,int hits=0){try{if(!ShouldWriteState(state,gameExists,DateTime.UtcNow))return;string message=detail();if(hits==0&&lines!=null&&lines.Count>0)message+=" | text="+string.Join(" / ",lines.Take(5).Select(l=>(l.Text.Length>20?l.Text.Substring(0,20):l.Text)+" "+l.Bounds));WriteState(state,message);}catch{}}
        public static void WriteState(string state,string detail){try{Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" state "+state+" | "+detail.Replace("\r"," ").Replace("\n"," "));}catch{}}
        // Columns come from ReadCandidates (.22 + .18*i, width .20); the
        // title half-width uses RecoverSlots' .45 of the .18 column spacing.
        // Title Y is 578/1440 from the 2026-10-12 game, with +/- .05h tolerance.
        public const double CardTitleY=578.0/1440,CardTitleTolerance=.05;
        public static int TitleColumn(TextLine line,int width,int height){
            if(line==null||width<=0||height<=0||line.Text==null||line.Text.Count(c=>c>='\u4e00'&&c<='\u9fff')<2||line.Bounds.Width<=0||line.Bounds.Height<height*.012||line.Bounds.Height>height*.05||line.Bounds.Top<height*(CardTitleY-CardTitleTolerance)||line.Bounds.Bottom>height*(CardTitleY+CardTitleTolerance))return -1;
            // Require the entire text rectangle inside one column, so a scoreboard
            // or tooltip spanning several columns is not mistaken for a title.
            for(int i=0;i<3;i++)if(line.Bounds.Left>=width*(.32+i*.18-.18*.45)&&line.Bounds.Right<=width*(.32+i*.18+.18*.45))return i;
            return -1;
        }
        public static bool SuspectRow(IEnumerable<TextLine> lines,int width,int height){
            if(lines==null||width<=0||height<=0)return false;
            var eligible=lines.Select(l=>new{Line=l,Column=TitleColumn(l,width,height)}).Where(l=>l.Column>=0).ToList();
            for(int i=0;i<eligible.Count;i++)for(int j=i+1;j<eligible.Count;j++)if(eligible[i].Column!=eligible[j].Column&&Math.Abs(eligible[i].Line.Bounds.Top-eligible[j].Line.Bounds.Top)<height*.03&&Math.Abs(eligible[i].Line.Bounds.Left+eligible[i].Line.Bounds.Width/2-eligible[j].Line.Bounds.Left-eligible[j].Line.Bounds.Width/2)>=width*.15)return true;
            return false;
        }
        public static string[] ExpiredScreenshots(IEnumerable<string> paths,bool suspect){return paths.Where(p=>Path.GetFileName(p).EndsWith("-suspect.png",StringComparison.OrdinalIgnoreCase)==suspect).OrderByDescending(p=>Path.GetFileName(p),StringComparer.Ordinal).Skip(suspect?10:20).ToArray();}
        public static void SaveScreenshot(Bitmap pixels,bool suspect=false,string folder=null){folder=folder??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"海克斯识别诊断");Directory.CreateDirectory(folder);pixels.Save(Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+(suspect?"-suspect":"")+".png"),ImageFormat.Png);foreach(var path in ExpiredScreenshots(Directory.GetFiles(folder,"*.png"),suspect))File.Delete(path);}
        DateTime failed=DateTime.MinValue,last=DateTime.MinValue;bool saved,success;string successKey;
        public void Reset(){failed=last=DateTime.MinValue;saved=success=false;successKey=null;lastBranch=null;}
        static void Append(string text){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"海克斯识别诊断.log");if(File.Exists(path)&&new FileInfo(path).Length>200000){string backup=Path.Combine(Path.GetDirectoryName(path),"海克斯识别诊断.1.log");if(File.Exists(backup))File.Delete(backup);File.Move(path,backup);}File.AppendAllText(path,text+Environment.NewLine,Encoding.UTF8);}
        string lastBranch;public void Branch(string branch){if(branch==lastBranch)return;lastBranch=branch;try{Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" branch "+branch);}catch{}}
        public void Observe(CardReading reading,List<TextLine> lines,Dictionary<string,Dictionary<string,object>> resources,Bitmap pixels,IEnumerable<Rectangle> masked=null,int skippedBelow=0){try{
            var now=DateTime.UtcNow;if(reading.Cards.Count==3){string key=string.Join(",",reading.Cards.Select(c=>c.Id).OrderBy(id=>id));if(!success||key!=successKey)Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" success "+string.Join(" / ",reading.Cards.Select(c=>c.Name+" "+c.MatchMethod)));failed=last=DateTime.MinValue;saved=false;success=true;successKey=key;return;}
            success=false;if(reading.Hits.Count==0){if(SuspectRow(lines,reading.Width,reading.Height)&&(now-suspectAt).TotalSeconds>=30){suspectAt=now;WriteState("ocr:suspect-row","lines="+lines.Count+",hits=0 | "+string.Join(" / ",lines.Where(l=>TitleColumn(l,reading.Width,reading.Height)>=0).Take(5).Select(l=>(l.Text.Length>20?l.Text.Substring(0,20):l.Text)+" "+l.Bounds)));SaveScreenshot(pixels,true);}failed=last=DateTime.MinValue;saved=false;return;}if(failed==DateTime.MinValue)failed=now;if(!(reading.Cards.Count==2&&!saved)&&((now-failed).TotalSeconds<2||(now-last).TotalSeconds<3))return;last=now;
            var report=new StringBuilder();report.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" "+reading.Width+"x"+reading.Height+" "+reading.FailureReason);
            report.AppendLine("masked "+string.Join(" / ",(masked??Enumerable.Empty<Rectangle>()).Select(r=>r.ToString())));
            if(skippedBelow>0)report.AppendLine("skipped-below="+skippedBelow);
            foreach(var hit in reading.Hits)report.AppendLine("hit "+hit.Id+" "+hit.Name+" "+hit.MatchMethod+" "+hit.Title);
            var names=resources["augments"].Values.Select(v=>Json.Str(Json.Get(v,"name"))).Where(n=>n.Length>1).ToList();
            foreach(var line in lines.Where(l=>l.Bounds.Height>=12&&l.Bounds.Y<reading.Height*.85)){string clean=CardMatcher.NormalizeName(line.Text);var nearest=names.Select(n=>new{Name=n,Distance=CardMatcher.Distance(clean,CardMatcher.NormalizeName(n))}).OrderBy(n=>n.Distance).FirstOrDefault();report.AppendLine("ocr "+line.Text+" | "+clean+" | "+line.Bounds+" | nearest="+(nearest==null?"":nearest.Name+" "+nearest.Distance));}Append(report.ToString());
            if(!saved){saved=true;SaveScreenshot(pixels);}
        }catch{}}
    }
    public class BadgeWindow:Form {
        public CardResult Result;
        // Defer topmost positioning to Present, which uses SWP_NOACTIVATE.
        readonly bool pixelAlpha;
        public BadgeWindow():this(false){}
        public BadgeWindow(bool usePixelAlpha){ThemeManager.Changed+=ThemeChanged;pixelAlpha=usePixelAlpha;StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;DoubleBuffered=!pixelAlpha;BackColor=BadgePaint.TransparentColor;if(!pixelAlpha)TransparencyKey=BadgePaint.TransparentColor;Opacity=1;}
        void ThemeChanged(object sender,EventArgs args){if(IsDisposed)return;if(pixelAlpha&&Visible&&Result!=null){using(var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb)){using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Transparent);BadgePaint.Draw(graphics,ClientRectangle,Result);}NativeOverlay.PresentAlpha(Handle,bitmap,Location);}}else Invalidate();}
        protected override void Dispose(bool disposing){if(disposing)ThemeManager.Changed-=ThemeChanged;base.Dispose(disposing);}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000020|0x00080000|0x00000080;return p;}}
        protected override void WndProc(ref Message message){if(message.Msg==0x84){message.Result=new IntPtr(-1);return;}base.WndProc(ref message);}
        protected override void OnPaint(PaintEventArgs e){if(!pixelAlpha&&Result!=null)BadgePaint.Draw(e.Graphics,ClientRectangle,Result);}
        public void PresentClipped(CardResult result,Point origin,Rectangle work){var bounds=result.Bounds;bounds.Offset(origin);var visible=Rectangle.Intersect(bounds,work);if(visible.Width<=0||visible.Height<=0){Hide();return;}visible.Offset(-bounds.X,-bounds.Y);var old=Region;Region=new Region(visible);if(old!=null)old.Dispose();Present(result,origin);}
        public static bool SameCombinations(CardResult first,CardResult second){return first.Combinations.SequenceEqual(second.Combinations)&&first.CombinationTexts.SequenceEqual(second.CombinationTexts);}
        public void Present(CardResult result,Point origin){var old=Result;var r=result.Bounds;var bounds=new Rectangle(origin.X+r.X,origin.Y+r.Y,r.Width,r.Height);bool same=old!=null&&old.Card!=null&&result.Card!=null&&old.Card.Id==result.Card.Id&&old.Card.Name==result.Card.Name&&ReferenceEquals(old.Row,result.Row)&&old.Context==result.Context&&old.SampleNote==result.SampleNote&&old.HiddenCombinations==result.HiddenCombinations&&old.CombinationText==result.CombinationText&&ReferenceEquals(old.Combination,result.Combination)&&SameCombinations(old,result);bool moved=Bounds!=bounds;bool shown=Visible;Result=result;if(result.PortraitArc){using(var path=BadgePaint.TextArcPath(result,new Rectangle(0,0,r.Width,r.Height))){var oldRegion=Region;Region=new Region(path);if(oldRegion!=null)oldRegion.Dispose();}}else if(old!=null&&old.PortraitArc){var oldRegion=Region;Region=null;if(oldRegion!=null)oldRegion.Dispose();}if(moved)Bounds=bounds;if(!shown)Show();if(moved||!shown)NativeOverlay.SetWindowPos(Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0010|0x0040);if(!same||!shown||moved||result.BenchSlot){if(pixelAlpha)using(var bitmap=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb)){using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Transparent);BadgePaint.Draw(graphics,new Rectangle(Point.Empty,bitmap.Size),result);}NativeOverlay.PresentAlpha(Handle,bitmap,bounds.Location);}else Invalidate();}}
    }
    public static class NativeOverlay {
        [StructLayout(LayoutKind.Sequential,Pack=1)]struct AlphaBlend{public byte Operation,Flags,Opacity,Format;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destination,ref Point position,ref Size size,IntPtr source,ref Point sourcePosition,int key,ref AlphaBlend blend,int flags);
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr item);
        public static void PresentAlpha(IntPtr window,Bitmap bitmap,Point position){
            IntPtr screen=GetDC(IntPtr.Zero),memory=IntPtr.Zero,image=IntPtr.Zero,previous=IntPtr.Zero;
            try{
                memory=CreateCompatibleDC(screen);image=bitmap.GetHbitmap(Color.FromArgb(0));previous=SelectObject(memory,image);
                var size=bitmap.Size;var source=new Point();var blend=new AlphaBlend{Opacity=255,Format=1};
                if(!UpdateLayeredWindow(window,screen,ref position,ref size,memory,ref source,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }finally{if(previous!=IntPtr.Zero)SelectObject(memory,previous);if(image!=IntPtr.Zero)DeleteObject(image);if(memory!=IntPtr.Zero)DeleteDC(memory);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}
        }
        public delegate void LocationEvent(IntPtr hook,uint evt,IntPtr window,int obj,int child,uint thread,uint time);
        [DllImport("user32.dll")]public static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,LocationEvent callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")]public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")]static extern IntPtr DeferWindowPos(IntPtr batch,IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]static extern bool EndDeferWindowPos(IntPtr batch);
        public static void MoveTogether(List<Tuple<IntPtr,Rectangle>> windows){if(windows.Count==0)return;IntPtr batch=BeginDeferWindowPos(windows.Count);foreach(var w in windows){if(batch==IntPtr.Zero)break;var r=w.Item2;batch=DeferWindowPos(batch,w.Item1,IntPtr.Zero,r.X,r.Y,r.Width,r.Height,0x0015);}if(batch!=IntPtr.Zero&&EndDeferWindowPos(batch))return;foreach(var w in windows){var r=w.Item2;SetWindowPos(w.Item1,IntPtr.Zero,r.X,r.Y,r.Width,r.Height,0x0015);}}
        [StructLayout(LayoutKind.Sequential)]public struct NativeRect{public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
        [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out NativeRect r);
        [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref Point p);
        [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out NativeRect r);
        delegate bool EnumWindowCallback(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindowCallback callback,IntPtr parameter);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
        [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out int value,int size);
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
        public static bool SelectionTarget(int clientPid,out Rectangle bounds,out IntPtr handle){
            bounds=Rectangle.Empty;handle=IntPtr.Zero;if(clientPid<=0)return false;
            var foreground=GetForegroundWindow();uint active,root;GetWindowThreadProcessId(foreground,out active);GetWindowThreadProcessId(GetAncestor(foreground,3),out root);
            if(!SelectionState.AllowsForeground((int)active,(int)root,clientPid,Process.GetCurrentProcess().Id))return false;
            var candidates=new List<CaptureTarget>();
            EnumWindows((window,p)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner!=(uint)clientPid||!IsWindowVisible(window)||IsIconic(window))return true;
                NativeRect r;var point=new Point();if(GetClientRect(window,out r)&&ClientToScreen(window,ref point)&&r.Right>=640&&r.Bottom>=400)candidates.Add(new CaptureTarget{Handle=window,Bounds=new Rectangle(point.X,point.Y,r.Right-r.Left,r.Bottom-r.Top)});return true;},IntPtr.Zero);
            var chosen=candidates.FirstOrDefault(c=>c.Handle==foreground)??(candidates.Count==1?candidates[0]:null);if(chosen==null)return false;
            handle=chosen.Handle;bounds=chosen.Bounds;return true;
        }
        [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
        public static float WindowDpiScale(IntPtr window){try{uint dpi=GetDpiForWindow(window);return dpi==0?1:dpi/96f;}catch{return 1;}}
        [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        public static void Dpi(){try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}}
        public static CaptureTarget ExistingGame(){foreach(var process in Process.GetProcessesByName("League of Legends"))using(process){try{var window=process.MainWindowHandle;NativeRect r;var origin=new Point();if(window!=IntPtr.Zero&&GetClientRect(window,out r)&&ClientToScreen(window,ref origin)&&r.Right-r.Left>=640&&r.Bottom-r.Top>=400)return new CaptureTarget{Handle=window,Bounds=new Rectangle(origin.X,origin.Y,r.Right-r.Left,r.Bottom-r.Top)};}catch{}}return null;}
        public static int WindowProcessId(IntPtr window){uint pid;GetWindowThreadProcessId(window,out pid);return (int)pid;}
        static bool GameShown(IntPtr window){int cloak;return IsWindowVisible(window)&&!(DwmGetWindowAttribute(window,14,out cloak,sizeof(int))==0&&cloak!=0);}
        public static void HandoffGameFocus(CaptureTarget game,FocusHandoffDecision decision){
            int own=Process.GetCurrentProcess().Id;IntPtr window=game==null?IntPtr.Zero:game.Handle;
            if(!decision.Observe(window,DateTime.UtcNow,window!=IntPtr.Zero&&GameShown(window),window!=IntPtr.Zero&&IsIconic(window),WindowProcessId(GetForegroundWindow()),own))return;
            // Recheck immediately before the native call, so a user's switch wins.
            if(WindowProcessId(GetForegroundWindow())!=own){AugmentOcrDiagnostics.WriteState("focus:handoff","skipped: foreground changed before handoff");return;}
            uint gamePid=(uint)WindowProcessId(window);bool allowed=AllowSetForegroundWindow(gamePid);
            bool called=false;if(WindowProcessId(GetForegroundWindow())==own)called=SetForegroundWindow(window);
            bool success=WindowProcessId(GetForegroundWindow())==(int)gamePid;
            AugmentOcrDiagnostics.WriteState("focus:handoff",(success?"success":"failed")+": game="+window+",pid="+gamePid+",allow="+allowed+",set="+called+(success?"":",reason=foreground changed or Windows rejected activation"));
        }
        public static bool GameVisible(CaptureTarget game){
            if(game==null)return false;int gamePid=WindowProcessId(game.Handle),ownPid=Process.GetCurrentProcess().Id;
            var hits=GameVisibleGate.CardCenters(game.Bounds).Select(point=>{
                IntPtr hit=GetAncestor(WindowFromPoint(point),2);int pid=WindowProcessId(hit);
                if(hit==IntPtr.Zero)return GameCardHit.None;
                if(gamePid>0&&pid==gamePid)return GameCardHit.Game;
                if(pid==ownPid&&Application.OpenForms.Cast<Form>().OfType<BadgeWindow>().Any(f=>f.IsHandleCreated&&f.Handle==hit))return GameCardHit.OwnOverlay;
                return GameCardHit.Other;
            }).ToArray();
            return GameVisibleGate.Allows(GameShown(game.Handle),IsIconic(game.Handle),hits);
        }
        public static bool VisibleGame(out Rectangle bounds,out IntPtr handle){var game=ExistingGame();bool visible=GameVisible(game);bounds=visible?game.Bounds:Rectangle.Empty;handle=visible?game.Handle:IntPtr.Zero;return visible;}
        public static bool ForegroundGame(out Rectangle bounds,out IntPtr handle){return ForegroundProcess("League of Legends",out bounds,out handle);}
        public static bool ForegroundClient(out Rectangle bounds,out IntPtr handle){return ForegroundProcess("LeagueClientUx",out bounds,out handle);}
        static bool ForegroundProcess(string processName,out Rectangle bounds,out IntPtr handle){bounds=Rectangle.Empty;handle=GetForegroundWindow();uint pid;GetWindowThreadProcessId(handle,out pid);try{using(var p=Process.GetProcessById((int)pid)){if(!p.ProcessName.Equals(processName,StringComparison.OrdinalIgnoreCase))return false;}}catch{return false;}
            NativeRect rect;var origin=new Point();if(!GetClientRect(handle,out rect)||!ClientToScreen(handle,ref origin))return false;
            bounds=new Rectangle(origin.X,origin.Y,rect.Right-rect.Left,rect.Bottom-rect.Top);return bounds.Width>=640&&bounds.Height>=400;
        }
        public static List<Rectangle> MaskRegions(Bitmap image,Rectangle captureBounds,IEnumerable<Rectangle> screenRects){var masked=new List<Rectangle>();using(var g=Graphics.FromImage(image))foreach(var rect in screenRects){var clipped=Rectangle.Intersect(captureBounds,rect);if(clipped.Width<=0||clipped.Height<=0)continue;clipped.Offset(-captureBounds.X,-captureBounds.Y);clipped=Rectangle.Intersect(clipped,new Rectangle(Point.Empty,image.Size));if(clipped.Width<=0||clipped.Height<=0)continue;g.FillRectangle(Brushes.Black,clipped);masked.Add(clipped);}return masked;}
        // Only augment OCR masks own windows; selection and HUD pixel matching keep unmasked captures.
        public static Bitmap Capture(Rectangle bounds){List<Rectangle> masked;return Capture(bounds,out masked,false);}
        public sealed class MaskWindow {public IntPtr Handle;public bool OwnProcess,Visible,Minimized,Cloaked;public Rectangle Bounds;}
        // Input order is top to bottom. Return clipped screen rectangles; MaskRegions
        // converts them to capture-local coordinates when painting the bitmap.
        public static List<Rectangle> SelectMaskRegions(IEnumerable<MaskWindow> windows,IntPtr target,Rectangle captureBounds,out int skippedBelow){
            var ordered=windows.ToList();int targetIndex=target==IntPtr.Zero?-1:ordered.FindIndex(w=>w.Handle==target);skippedBelow=0;var regions=new List<Rectangle>();
            for(int i=0;i<ordered.Count;i++){var w=ordered[i];if(!w.OwnProcess||!w.Visible||w.Minimized||w.Cloaked||w.Handle==target)continue;var clipped=Rectangle.Intersect(captureBounds,w.Bounds);if(clipped.Width<=0||clipped.Height<=0)continue;if(targetIndex>=0&&i>targetIndex){skippedBelow++;continue;}regions.Add(clipped);}
            return regions;
        }
        static bool maskFallbackLogged;
        public static Bitmap Capture(Rectangle bounds,out List<Rectangle> masked,bool maskOwnWindows=true){Rectangle gameBounds;IntPtr target=IntPtr.Zero;int skipped;if(maskOwnWindows&&!ForegroundGame(out gameBounds,out target))target=IntPtr.Zero;return Capture(bounds,target,out masked,out skipped,maskOwnWindows);}
        public static Bitmap Capture(Rectangle bounds,IntPtr target,out List<Rectangle> masked,out int skippedBelow,bool maskOwnWindows=true){
            var image=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);try{
                using(var g=Graphics.FromImage(image))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
                var regions=new List<Rectangle>();skippedBelow=0;
                if(maskOwnWindows){uint pid=(uint)Process.GetCurrentProcess().Id;var windows=new List<MaskWindow>();
                    EnumWindows((window,p)=>{uint owner;NativeRect r;GetWindowThreadProcessId(window,out owner);var w=new MaskWindow{Handle=window,OwnProcess=owner==pid};if(w.OwnProcess){w.Visible=IsWindowVisible(window);w.Minimized=IsIconic(window);int cloak;w.Cloaked=DwmGetWindowAttribute(window,14,out cloak,sizeof(int))==0&&cloak!=0;if(GetWindowRect(window,out r))w.Bounds=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}windows.Add(w);return true;},IntPtr.Zero);
                    if((target==IntPtr.Zero||!windows.Any(w=>w.Handle==target))&&!maskFallbackLogged){maskFallbackLogged=true;AugmentOcrDiagnostics.WriteState("capture:mask-fallback",target==IntPtr.Zero?"target game handle unavailable; masking all eligible own windows":"target game handle absent from Z order; masking all eligible own windows");}
                    regions=SelectMaskRegions(windows,target,bounds,out skippedBelow);
                }
                masked=MaskRegions(image,bounds,regions);return image;
            }catch{image.Dispose();throw;}
        }
    }
    public static class InGameHero {
        static string Get(string endpoint){
            if(endpoint!="activeplayername"&&endpoint!="playerlist"&&endpoint!="activeplayer")throw new ArgumentException();
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var req=(HttpWebRequest)WebRequest.Create("https://127.0.0.1:2999/liveclientdata/"+endpoint);req.Method="GET";req.Proxy=null;req.Timeout=1200;req.ReadWriteTimeout=1200;req.AllowAutoRedirect=false;
            req.ServerCertificateValidationCallback=(s,c,ch,e)=>c!=null&&req.RequestUri.Host=="127.0.0.1"&&req.RequestUri.Port==2999;
            using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return reader.ReadToEnd();
        }
        public static int ParseLevel(object active){double level=Json.Num(Json.Get(active,"level"));return !double.IsNaN(level)&&level>=1&&level<=30&&level==Math.Floor(level)?(int)level:0;}
        public static int StageFromLevel(int level){if(level<3||level>30)return 0;return level>=15?4:level>=11?3:level>=7?2:1;}
        public static int MergeLevel(int previous,int observed){return observed>=1&&observed<=30?observed:previous;}
        public static int ReadLevel(){try{return ParseLevel(Json.Parse(Get("activeplayer")));}catch{return 0;}}
        public static int Read(DataStore store){try{string name=Json.Str(Json.Parse(Get("activeplayername")));if(name.Length==0)return 0;
            var matching=Json.Arr(Json.Parse(Get("playerlist"))).Where(p=>Json.Str(Json.Get(p,"riotId"))==name||Json.Str(Json.Get(p,"summonerName"))==name).ToList();
            if(matching.Count!=1)return 0;var self=matching[0];string key=Json.Str(Json.Get(self,"rawChampionName")).Replace("game_character_displayname_","");
            foreach(var entry in store.Resources["champions"]){if(string.Equals(Json.Str(Json.Get(entry.Value,"riotId")),key,StringComparison.OrdinalIgnoreCase)||Json.Str(Json.Get(entry.Value,"title"))==Json.Str(Json.Get(self,"championName")))return int.Parse(entry.Key);}return 0;
        }catch{return 0;}}
    }
    public class OverlayController:IDisposable {
        readonly Func<DataStore> store;readonly Func<int> hero;readonly Func<string> scope;readonly Func<int> stage;readonly Func<bool> follow;readonly Action<string> status;readonly Action<int> detected;
        readonly Func<CaptureTarget> target;readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly System.Windows.Forms.Timer visibility=new System.Windows.Forms.Timer();readonly List<BadgeWindow> badges=new List<BadgeWindow>();readonly List<List<BadgeWindow>> combinationBadges=new List<List<BadgeWindow>>();
        readonly PartialCardGate partialGate=new PartialCardGate();readonly AugmentOcrDiagnostics diagnostics=new AugmentOcrDiagnostics();string readingContext;string pendingMerge;DateTime lastOcr=DateTime.MinValue;
        DateTime[] lastSeen=new DateTime[3];Task<LocalOcr> preparedOcr;DateTime lastCards=DateTime.MinValue;List<CardResult> stableResults;string stableContext;
        Task<int> levelRequest,heroRequest;int cachedLevel;DateTime nextLevel=DateTime.MinValue;int stableStage=-1;TitleStamp[] titleStamps;Rectangle titleBand;Size titleFrameSize;DateTime nextFullScan=DateTime.MinValue;LocalOcr ocr;bool busy,enabled,disposed;DateTime nextHero=DateTime.MinValue;int verifiedHero;IntPtr lastWindow;
        public string LastReport {get;private set;}public string ReportKey {get;private set;}
        public Func<bool> ModeAllowed=()=>true;
        public bool Enabled{get{return enabled;}set{enabled=value;if(!value)Hide();}}
        public IList<BadgeWindow> Windows {get{return badges.AsReadOnly();}}
        readonly FocusHandoffDecision focusHandoff=new FocusHandoffDecision();
        bool visibleNotForeground;DateTime lastVisibleState=DateTime.MinValue;
        CaptureTarget GameTarget(){var game=NativeOverlay.ExistingGame();NativeOverlay.HandoffGameFocus(game,focusHandoff);bool visible=NativeOverlay.GameVisible(game);bool relaxed=visible&&NativeOverlay.WindowProcessId(NativeOverlay.GetForegroundWindow())!=NativeOverlay.WindowProcessId(game.Handle);if(relaxed&&(!visibleNotForeground||DateTime.UtcNow-lastVisibleState>=TimeSpan.FromSeconds(30))){lastVisibleState=DateTime.UtcNow;AugmentOcrDiagnostics.WriteState("gate:visible-not-foreground","game="+game.Handle+",bounds="+game.Bounds+",three card centers exposed; OCR allowed");}visibleNotForeground=relaxed;return visible?game:null;}
        public OverlayController(Func<DataStore> s,Func<int> h,Func<string> sc,Func<int> st,Func<bool> autoFollow,Action<string> message,Action<int> champion,Func<CaptureTarget> testTarget=null){store=s;hero=h;scope=sc;stage=st;follow=autoFollow;status=message;detected=champion;target=testTarget??GameTarget;for(int i=0;i<3;i++){badges.Add(new BadgeWindow());combinationBadges.Add(new List<BadgeWindow>());}preparedOcr=Task.Run(async()=>{try{var ready=new LocalOcr();using(var warm=new Bitmap(64,64)){using(var g=Graphics.FromImage(warm))g.Clear(Color.Black);await ready.Read(warm);}AugmentOcrDiagnostics.WriteState("ocr-engine:ready","Windows zh-Hans OCR ready");return ready;}catch(Exception e){AugmentOcrDiagnostics.WriteState("ocr-engine:failed",e.GetType().Name+": "+e.Message);throw;}});timer.Interval=350;timer.Tick+=async(sender,e)=>await Tick();timer.Start();visibility.Interval=150;visibility.Tick+=(sender,e)=>{var frame=target();if((!enabled||!ModeAllowed())||frame==null){Hide("visibility-gate:disabled/mode/visible");}};visibility.Start();}
        public static bool TitleStillVisible(List<TextLine> text,Candidate card,int width){return text.Any(line=>CardMatcher.Normalize(line.Text)==CardMatcher.Normalize(card.Name)&&Math.Abs(line.Bounds.Left+line.Bounds.Width/2-card.Title.Left-card.Title.Width/2)<width*.045&&Math.Abs(line.Bounds.Top-card.Title.Top)<Math.Max(25,card.Title.Height*1.5));}
        public static Candidate AlignedCard(IEnumerable<Candidate> read,Candidate old,int width){return read.Where(c=>Math.Abs(CardMatcher.CenterX(c)-CardMatcher.CenterX(old))<width*.045).OrderBy(c=>Math.Abs(CardMatcher.CenterX(c)-CardMatcher.CenterX(old))).FirstOrDefault();}
        public static List<Candidate> MergeSlots(IList<Candidate> old,IEnumerable<Candidate> read,bool[] visible,int width){
            if(old==null||old.Count!=3||visible==null||visible.Length!=3||width<=0)return null;
            var current=read.ToList();var merged=new List<Candidate>();
            for(int i=0;i<3;i++){var card=AlignedCard(current,old[i],width)??(visible[i]?old[i]:null);if(card==null)return null;merged.Add(card);}
            return merged.Select(c=>c.Id).Distinct().Count()==3?merged:null;
        }
        public static bool SlotsChanged(IList<Candidate> old,IList<Candidate> merged){return merged!=null&&!old.Select(c=>c.Id).SequenceEqual(merged.Select(c=>c.Id));}
        string diagnosticHideReason;
        void Hide(string reason="unspecified",[System.Runtime.CompilerServices.CallerLineNumber]int line=0){diagnosticHideReason=reason;foreach(var b in badges)b.Hide();foreach(var stack in combinationBadges)foreach(var b in stack)b.Hide();lastCards=DateTime.MinValue;stableResults=null;pendingMerge=null;lastOcr=DateTime.MinValue;partialGate.Reset();diagnostics.Reset();readingContext=null;}
        public Func<int> ConfirmedHero=()=>0;
        public int CurrentHero {get{return verifiedHero>0?verifiedHero:ConfirmedHero();}}
        async Task Tick(){if(disposed||busy)return;string frameState="ocr:pending";CaptureTarget diagnosticGame=null;bool gameChecked=false;List<TextLine> frameLines=null;int frameHits=0,frameCards=0;try{if((!enabled||!ModeAllowed())){frameState=!enabled?"gate:disabled":"gate:mode";Hide("disabled-or-mode-gate");return;}var frame=target();if(frame==null){frameState=lastForegroundGate;if(diagnostics.StateCheckDue(frameState,DateTime.UtcNow)){diagnosticGame=NativeOverlay.ExistingGame();gameChecked=true;frameState=lastForegroundGate=diagnosticGame==null?"gate:no-game":NativeOverlay.GetForegroundWindow()==diagnosticGame.Handle?"gate:covered":"gate:foreground";}Hide("game-not-visible");return;}diagnosticGame=frame;gameChecked=true;Rectangle area=frame.Bounds;IntPtr window=frame.Handle;
            busy=true;try{
                if(lastWindow!=window){Hide("game-window-changed");titleBand=Rectangle.Empty;titleStamps=null;nextFullScan=DateTime.MinValue;heroRequest=null;levelRequest=null;verifiedHero=0;cachedLevel=0;lastWindow=window;nextHero=DateTime.MinValue;}
                if(titleFrameSize!=area.Size){titleFrameSize=area.Size;titleBand=Rectangle.Empty;titleStamps=null;nextFullScan=DateTime.MinValue;Hide("game-size-changed");}
                var snapshot=store();string sample=scope();int overrideStage=stage();
                bool automatic=follow();if(automatic&&verifiedHero==0)verifiedHero=ConfirmedHero();if(automatic&&DateTime.UtcNow>=nextHero){nextHero=DateTime.UtcNow.AddSeconds(5);if(heroRequest==null)heroRequest=Task.Run(()=>InGameHero.Read(snapshot));if(verifiedHero==0)await heroRequest;if(disposed)return;}
                if(heroRequest!=null&&heroRequest.IsCompleted){int found=heroRequest.Result;heroRequest=null;if(found>0){verifiedHero=found;detected(found);}else nextHero=DateTime.UtcNow.AddMilliseconds(500);}
                int champion=automatic?verifiedHero:hero();if(champion<=0){frameState="gate:hero";Hide("hero-unavailable");status("等待本机英雄识别");return;}
                var beforeCapture=target();if((!enabled||!ModeAllowed())||beforeCapture==null||beforeCapture.Handle!=window||beforeCapture.Bounds!=area){frameState="gate:target-changed";Hide("pre-capture-target-changed");return;}
                if(levelRequest!=null&&levelRequest.IsCompleted){cachedLevel=InGameHero.MergeLevel(cachedLevel,levelRequest.Result);levelRequest=null;}if(overrideStage==-2&&levelRequest==null&&DateTime.UtcNow>=nextLevel){nextLevel=DateTime.UtcNow.AddSeconds(1);levelRequest=Task.Run(()=>InGameHero.ReadLevel());}
                string captureContext=snapshot.Fingerprint+":"+champion+":"+sample+":"+area+":"+overrideStage;if(readingContext!=captureContext){Hide("ocr-context-changed");readingContext=captureContext;}
                List<TextLine> text;CardReading reading;bool[] visualMatches=null;List<Rectangle> masked;int skippedBelow;using(var pixels=NativeOverlay.Capture(area,window,out masked,out skippedBelow)){if(titleStamps!=null)visualMatches=titleStamps.Select(stamp=>stamp.Matches(pixels)).ToArray();if(stableResults!=null&&stableContext==snapshot.Fingerprint+":"+champion+":"+sample+":"+area&&visualMatches!=null&&visualMatches.All(v=>v)&&pendingMerge==null&&(DateTime.UtcNow-lastOcr).TotalMilliseconds<=1000&&(overrideStage!=-2||stableStage==InGameHero.StageFromLevel(cachedLevel))){for(int i=0;i<stableResults.Count;i++){lastSeen[i]=DateTime.UtcNow;PresentCard(i,stableResults[i],area.Location);}frameState="skip-by-stamp";return;}text=await Task.Run(async()=>{if(ocr==null)ocr=await preparedOcr;List<TextLine> lines=await ocr.ReadCandidates(pixels,titleBand,snapshot.Resources);if(CardMatcher.Find(lines,snapshot.Resources,area.Width,area.Height).Cards.Count!=3&&DateTime.UtcNow>=nextFullScan){nextFullScan=DateTime.UtcNow.AddSeconds(2);lines=LocalOcr.MergeReadings(lines,await ocr.Read(pixels));lines=await ocr.RecoverSlots(pixels,lines,snapshot.Resources);}return lines;});lastOcr=DateTime.UtcNow;var matchedCards=CardMatcher.Find(text,snapshot.Resources,area.Width,area.Height);reading=matchedCards;frameLines=text;frameHits=matchedCards.Hits.Count;frameCards=matchedCards.Cards.Count;frameState="ocr:lines="+text.Count+",hits="+frameHits+",cards="+frameCards;
                    if(matchedCards.Cards.Count!=3&&stableResults!=null&&stableContext==snapshot.Fingerprint+":"+champion+":"+sample+":"+area){
                        var old=stableResults.Select(r=>r.Card).ToList();var visible=old.Select((c,i)=>TitleStillVisible(text,c,area.Width)||(visualMatches!=null&&i<visualMatches.Length&&visualMatches[i])).ToArray();
                        var merged=MergeSlots(old,matchedCards.Cards,visible,area.Width);
                        if(SlotsChanged(old,merged)){string key=string.Join(",",merged.Select(c=>c.Id));if(pendingMerge==key){reading=new CardReading{Width=area.Width,Height=area.Height,Cards=merged,Hits=matchedCards.Hits};pendingMerge=null;diagnostics.Branch("merge-commit");}else{pendingMerge=key;diagnostics.Branch("merge-pending");}}
                        else {pendingMerge=null;diagnostics.Branch("partial-keep-old");}
                    }else {pendingMerge=null;if(stableResults!=null&&matchedCards.Cards.Count==3)diagnostics.Branch("full-read");}
                    var diagnosticTarget=target();if(diagnosticTarget!=null&&diagnosticTarget.Handle==window&&diagnosticTarget.Bounds==area&&enabled&&ModeAllowed())diagnostics.Observe(matchedCards,text,snapshot.Resources,pixels,masked,skippedBelow);if(reading.Cards.Count==3){titleStamps=reading.Cards.Select(c=>new TitleStamp(pixels,c.Title)).ToArray();float top=reading.Cards.Min(c=>c.Title.Top),bottom=reading.Cards.Max(c=>c.Title.Bottom);titleBand=Rectangle.Intersect(new Rectangle(0,Math.Max(0,(int)top-12),area.Width,(int)(bottom-top)+24),new Rectangle(Point.Empty,pixels.Size));}}
                if(disposed)return;var current=target();if((!enabled||!ModeAllowed())||current==null||window!=current.Handle||area!=current.Bounds){frameState="gate:target-changed";Hide("post-ocr-target-changed");return;}
                string context=snapshot.Fingerprint+":"+champion+":"+sample+":"+area;
                bool partial=reading.Cards.Count!=3;bool partialReady=partialGate.Observe(reading,captureContext);
                if(partial){
                    if(stableResults==null||stableContext!=context){if(!partialReady){foreach(var b in badges)b.Hide();foreach(var stack in combinationBadges)foreach(var b in stack)b.Hide();return;}}else{
                    for(int i=0;i<stableResults.Count;i++){
                        var old=stableResults[i];var replacement=AlignedCard(reading.Cards,old.Card,area.Width);if(replacement!=null&&replacement.Id!=old.Card.Id){lastSeen[i]=DateTime.MinValue;HideCard(i);continue;}bool matched=TitleStillVisible(text,old.Card,area.Width)||(visualMatches!=null&&i<visualMatches.Length&&visualMatches[i]);
                        if(matched)lastSeen[i]=DateTime.UtcNow;
                        if(DateTime.UtcNow-lastSeen[i]<TimeSpan.FromMilliseconds(1200))PresentCard(i,old,area.Location);else {HideCard(i);}
                    }
                    if(lastSeen.All(t=>DateTime.UtcNow-t>TimeSpan.FromMilliseconds(1200)))Hide("all-title-evidence-expired");
                    return;
                    }
                }
                int selectedStage;string provenance;
                if(overrideStage==-2){
                    if(levelRequest!=null&&levelRequest.IsCompleted){cachedLevel=InGameHero.MergeLevel(cachedLevel,levelRequest.Result);levelRequest=null;}if(levelRequest==null&&DateTime.UtcNow>=nextLevel){nextLevel=DateTime.UtcNow.AddSeconds(1);levelRequest=Task.Run(()=>InGameHero.ReadLevel());}int level=cachedLevel;if(disposed)return;
                    var afterLevel=target();if((!enabled||!ModeAllowed())||afterLevel==null||afterLevel.Handle!=window||afterLevel.Bounds!=area||store()!=snapshot||scope()!=sample){frameState="gate:target-changed";Hide("post-level-context-changed");return;}
                    selectedStage=InGameHero.StageFromLevel(level);
                    provenance=selectedStage>0?"":"使用总体数据";
                }else{
                    int inferred=CardMatcher.InferStage(reading,snapshot.ReadHero(champion,sample));
                    selectedStage=overrideStage>=0?overrideStage:Math.Max(0,inferred);
                    provenance=overrideStage>=0?"总体数据":(inferred>=0?"已核对阶段":"阶段未识别");
                }
                if(!automatic)provenance+=" / 手动英雄";
                var results=CardMatcher.Rank(reading,snapshot,champion,sample,selectedStage,provenance);foreach(var result in results)CardMatcher.AddCombination(result,snapshot,champion,sample);foreach(var result in results)if(result.Row!=null&&result.Row.UsesOverallAugment&&selectedStage>=1&&selectedStage<=4){result.SampleNote="※ "+new[]{"","一选","二选","三选","四选"}[selectedStage]+"数据不足，用总体数据代替";}
                if(stableResults!=null&&stableContext==context&&results.Select(r=>r.Card.Id).SequenceEqual(stableResults.Select(r=>r.Card.Id)))for(int i=0;i<results.Count;i++)if(results[i].Bounds.Height==stableResults[i].Bounds.Height)results[i].Bounds=stableResults[i].Bounds;
                if(!partial){stableResults=results;stableStage=selectedStage;stableContext=context;lastCards=DateTime.UtcNow;for(int i=0;i<3;i++)lastSeen[i]=lastCards;}else{titleStamps=null;titleBand=Rectangle.Empty;HideCard(2);}
                for(int i=0;i<results.Count;i++)PresentCard(i,results[i],area.Location);
                ReportKey=snapshot.Fingerprint+":"+champion+":"+sample+":"+selectedStage+":"+string.Join(",",reading.Cards.Select(c=>c.Id));
                LastReport=snapshot.Report(new[]{new Group{Title="屏幕候选 · "+results[0].Context,Path="candidates",Rows=results.Where(r=>r.Row!=null).Select(r=>r.Row).OrderBy(r=>r.Rank==0?int.MaxValue:r.Rank).ToList()}},sample,snapshot.Lookup("champions",champion));
                foreach(var missing in results.Where(r=>r.Row==null))LastReport+="\r\n"+missing.Card.Name+"：当前条件下无对应数据。";
                status("识别到："+string.Join(" / ",reading.Cards.Select(c=>c.Name))+(string.IsNullOrEmpty(provenance)?"":" · "+provenance));
            }catch(Exception e){frameState="error:"+e.GetType().Name;if(!RetainVisibleCards(area,window))Hide("exception-retention-failed");status("浮层暂停本帧："+e.GetType().Name+" · "+(ocr==null?"请检查 Windows 中文 OCR 组件":"下一帧将重试"));}finally{busy=false;}}finally{RecordState(frameState,frameLines,frameHits,frameCards,diagnosticGame,gameChecked);}}
        string lastForegroundGate="gate:no-game";
        void RecordState(string state,List<TextLine> lines,int hits,int cards,CaptureTarget game,bool gameChecked){try{Func<bool> exists=()=>{if(!gameChecked){game=NativeOverlay.ExistingGame();gameChecked=true;}return game!=null;};diagnostics.State(state,()=>{exists();Rectangle r;IntPtr h;bool foreground=NativeOverlay.ForegroundGame(out r,out h);string engine=preparedOcr==null||!preparedOcr.IsCompleted?"未就绪":preparedOcr.IsFaulted?"失败: "+preparedOcr.Exception.GetBaseException().Message:preparedOcr.IsCanceled?"失败: canceled":"就绪";return "enabled="+enabled+",mode="+ModeAllowed()+",foreground="+foreground+",bounds="+(game==null?Rectangle.Empty:game.Bounds)+",verifiedHero="+verifiedHero+",confirmedHero="+ConfirmedHero()+",hero="+hero()+",follow="+follow()+",cachedLevel="+cachedLevel+",ocr="+engine+",lines="+(lines==null?0:lines.Count)+",hits="+hits+",cards="+cards+",titleBand="+titleBand+",hide="+diagnosticHideReason;},exists,lines,hits);}catch{}}
        bool RetainVisibleCards(Rectangle area,IntPtr window){try{var current=target();if(!enabled||!ModeAllowed()||current==null||current.Handle!=window||current.Bounds!=area||stableResults==null||titleStamps==null)return false;bool any=false;List<Rectangle> masked;int skippedBelow;using(var pixels=NativeOverlay.Capture(area,window,out masked,out skippedBelow))for(int i=0;i<stableResults.Count;i++){if(i<titleStamps.Length&&titleStamps[i].Matches(pixels)){PresentCard(i,stableResults[i],area.Location);lastSeen[i]=DateTime.UtcNow;any=true;}else {HideCard(i);}}return any;}catch{return false;}}
        void HideCard(int index){badges[index].Hide();foreach(var b in combinationBadges[index])b.Hide();}
        void PresentCard(int index,CardResult card,Point origin){
            badges[index].Present(card,origin);
            if(card.Combination==null){foreach(var b in combinationBadges[index])b.Hide();return;}
            var layout=ComboStackLayout.Calculate(card.CardFrame,card.Bounds,card.CaptureSize,card.Combinations.Count,ComboStackLayout.RowHeight,ComboStackLayout.RefreshBounds(card.CardFrame,card.CaptureSize),ComboStackLayout.HudBounds(card.CaptureSize));
            var windows=combinationBadges[index];
            while(windows.Count<layout.Rectangles.Length)windows.Add(new BadgeWindow());
            for(int i=0;i<windows.Count;i++){
                if(i>=layout.Rectangles.Length){windows[i].Hide();continue;}
                bool summary=layout.HiddenCount>0&&i==layout.Rectangles.Length-1;
                windows[i].Present(new CardResult{Card=card.Card,CombinationOnly=true,
                    Combination=summary?null:card.Combinations[i],CombinationText=summary?null:card.CombinationTexts[i],
                    HiddenCombinations=summary?layout.HiddenCount:0,Context=card.Context,Bounds=layout.Rectangles[i]},origin);
            }
        }
        public void Dispose(){disposed=true;timer.Stop();timer.Dispose();visibility.Stop();visibility.Dispose();foreach(var b in badges)b.Dispose();foreach(var stack in combinationBadges)foreach(var b in stack)b.Dispose();}
    }
}


