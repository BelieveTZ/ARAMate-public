using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConfidenceDesktop {
    public sealed class BenchBadgePlacement {
        public Rectangle Bounds;
        public bool Outside;
        public float FontSize;
    }
    public static class BenchBadgeLayout {
        public static int Width(int clientWidth,int slot,float dpiScale){return Math.Max(1,(int)Math.Round(clientWidth*SelectionMatcher.BenchWidths[Math.Max(0,Math.Min(9,slot))])-2);}
        public static float FontLimit(int clientHeight){return Math.Max(9,10*clientHeight/720f);}
        // Bench badges stay inside the portrait, regardless of desktop position or DPI.
        public static BenchBadgePlacement[] Calculate(Rectangle client,Rectangle work,int[] slots,float[] fonts,float dpiScale,int bottom,int slotHeight){
            return slots.Select(slot=>{var bounds=SelectionMatcher.AlignedBenchBadgeBounds(client.Width,client.Height,slot,bottom,slotHeight);bounds.Offset(client.Location);return new BenchBadgePlacement{Bounds=bounds,Outside=false,FontSize=0};}).ToArray();
        }
    }

    public static class SelectionMatcher {
        // Leave the one-pixel frame uncovered so pixel detection can still read its bottom edge.
        public static Rectangle BenchBadgeBounds(double slotX,int slotWidth,double stride,int slotBottom,int slotHeight){
            int width=Math.Max(1,slotWidth-2),height=Math.Min(Math.Max(1,slotHeight-2),Math.Max(13,(int)Math.Ceiling(slotHeight*15.0/49)));
            return new Rectangle((int)Math.Round(slotX)+1,slotBottom-1-height,width,height);
        }
        public static Rectangle BenchAvatarBounds(int width,int height,int slot,int bottom,int slotHeight){
            slot=Math.Max(0,Math.Min(9,slot));int frameWidth=(int)Math.Round(width*BenchWidths[slot]);
            return new Rectangle((int)Math.Round(width*BenchCenters[slot]-frameWidth/2.0)+1,bottom-slotHeight+1,frameWidth-2,slotHeight-2);
        }
        // Measured frame centers and widths in the 1280x720 client; each slot is independent.
        public static readonly double[] BenchCenters={0.294928093774,0.340179357394,0.386348611419,0.431816257366,0.477959480932,0.523855696378,0.569291528698,0.615234375000,0.661442201327,0.706629354602};
        public static readonly double[] BenchWidths={0.038332587963,0.038391285211,0.038868637377,0.038648790774,0.038731461864,0.039003527587,0.038583057395,0.038281250000,0.038509402655,0.038375313682};
        public static Rectangle AlignedBenchBadgeBounds(int width,int height,int slot,int bottom,int slotHeight){
            slot=Math.Max(0,Math.Min(9,slot));double center=width*BenchCenters[slot];int slotWidth=(int)Math.Round(width*BenchWidths[slot]);
            var bounds=BenchBadgeBounds(center-slotWidth/2.0,slotWidth,width*58.6/1280,bottom,slotHeight);
            return bounds;
        }
        public static Rectangle FixedBenchBadgeBounds(int width,int height,int slot){return AlignedBenchBadgeBounds(width,height,slot,(int)Math.Round(height*64.0/720),(int)Math.Round(height*49.0/720));}
        public static Rectangle KeepBench(Rectangle previous,double previousStride,Rectangle found,double foundStride,bool reset,out double stride){
            if(!found.IsEmpty){stride=foundStride;return found;}stride=reset?0:previousStride;return reset?Rectangle.Empty:previous;
        }
        public static CardResult FromClient(DataStore data,int hero,string scope,int width,int height){
            if(width<640||height<400)return null;
            var board=data.Groups(0,scope).FirstOrDefault(g=>g.Path=="champions/rows");if(board==null)return null;
            var row=board.Rows.FirstOrDefault(r=>(int)Json.Num(Json.Get(r.Raw,"id"),0)==hero);
            object resource;if(!data.Resources["champions"].TryGetValue(hero.ToString(),out resource))return null;
            var card=new CardResult{Compact=true,PortraitArc=true,Card=new Candidate{Id=hero,Name=data.Lookup("champions",hero)},Row=row,Context="统计学胜率"};
            float radius=height*29f/720;
            AlignPortrait(card,new RectangleF(width*.067f-radius,height*.407f-radius,radius*2,radius*2),width,height);
            return card;
        }
        public static List<CardResult> FromSession(DataStore data,ClientStatus state,string scope,int width,int height){
            var results=new List<CardResult>();var benchBoard=data.Groups(0,scope).FirstOrDefault(g=>g.Path=="champions/rows");if(benchBoard!=null)BadgePaint.ConfigureBenchMetrics(benchBoard.Rows);
            if(state.Team.Length>0){for(int i=0;i<Math.Min(5,state.Team.Length);i++){var teammate=FromClient(data,state.Team[i],scope,width,height);if(teammate==null)continue;float radius=height*29f/720;float cx=width*.067f,cy=height*(.407f+(i-(Math.Min(5,state.Team.Length)-1)/2f)*80f/720);AlignPortrait(teammate,new RectangleF(cx-radius,cy-radius,radius*2,radius*2),width,height);results.Add(teammate);}}else{var selected=FromClient(data,state.Champion,scope,width,height);if(selected!=null)results.Add(selected);}
            for(int i=0;i<Math.Min(10,state.Bench.Length);i++){
                var card=FromClient(data,state.Bench[i],scope,width,height);if(card==null)continue;
                card.PortraitArc=false;card.BenchSlot=true;card.BenchIndex=i;card.Bounds=FixedBenchBadgeBounds(width,height,i);results.Add(card);
            }
            return results;
        }

        static IEnumerable<string> HeroNames(KeyValuePair<string,object> hero){
            yield return CardMatcher.Normalize(Json.Str(Json.Get(hero.Value,"name")));
            yield return CardMatcher.Normalize(Json.Str(Json.Get(hero.Value,"title")));

        }
        static bool OneCharacterDifferent(string a,string b){
            if(a.Length<3||b.Length<4||Math.Abs(a.Length-b.Length)>1)return false;
            if(a.Length==b.Length){int different=0;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])different++;return different==1;}
            string shorter=a.Length<b.Length?a:b,longer=a.Length>b.Length?a:b;
            for(int i=0;i<longer.Length;i++)if(longer.Remove(i,1)==shorter)return true;return false;
        }
        public static List<CardResult> InitialChoices(List<TextLine> lines,DataStore data,string scope,int width,int height){
            var empty=new List<CardResult>();
            if(!lines.Any(l=>CardMatcher.Normalize(l.Text).Contains("选择你的英雄")))return empty;
            var hits=new List<CardResult>();
            foreach(var line in lines.Where(l=>l.Bounds.Left>width*.18&&l.Bounds.Right<width*.86&&l.Bounds.Top>height*.40&&l.Bounds.Bottom<height*.76)){
                string text=CardMatcher.Normalize(line.Text);
                var matches=data.Resources["champions"].Where(p=>HeroNames(p).Contains(text)).ToList();
                if(matches.Count==0&&text.Length>=2)matches=data.Resources["champions"].Where(p=>HeroNames(p).Any(name=>name.Length>=4&&name.StartsWith(text,StringComparison.Ordinal))).ToList();
                if(matches.Count==0)matches=data.Resources["champions"].Where(p=>HeroNames(p).Any(name=>OneCharacterDifferent(text,name))).ToList();
                if(matches.Count!=1)continue;var card=FromClient(data,int.Parse(matches[0].Key),scope,width,height);if(card==null)continue;
                card.Compact=false;card.PortraitArc=false;card.PlainChoice=true;card.Card.Name=ConfidenceDesktop.HeroNames.Name(card.Card.Name).Trim();card.Context="";int w=line.Frame.IsEmpty?Math.Max(180,(int)(width*.14)):Math.Max(180,(int)(line.Frame.Width*.85));int y=line.Frame.IsEmpty?(int)(height*.72)+36:(int)line.Frame.Bottom+36;card.Bounds=new Rectangle((int)((line.Frame.IsEmpty?line.Bounds.Left+line.Bounds.Width/2:line.Frame.Left+line.Frame.Width/2f)-w/2),Math.Min(y,height-100),w,95);hits.Add(card);
            }
            hits=hits.GroupBy(c=>c.Card.Id).Select(g=>g.First()).ToList();
            if(hits.Count>3)return empty;
            return hits.OrderBy(c=>c.Bounds.X).ToList();
        }
        public static List<Rectangle> FindCardFrames(Bitmap image){
            var edges=new List<Rectangle>();int top=(int)(image.Height*.19),bottom=(int)(image.Height*.72);
            for(int x=(int)(image.Width*.2);x<(int)(image.Width*.8);x++){
                int start=-1,miss=0,bestStart=0,bestLength=0;
                for(int y=top;y<bottom;y++){
                    var c=image.GetPixel(x,y);int hi=Math.Max(c.R,Math.Max(c.G,c.B)),lo=Math.Min(c.R,Math.Min(c.G,c.B));
                    bool border=lo>100&&hi-lo<65;
                    if(border){if(start<0)start=y;miss=0;}else if(start>=0&&++miss>3){if(y-start>bestLength){bestStart=start;bestLength=y-start;}start=-1;miss=0;}
                }
                if(start>=0&&bottom-start>bestLength){bestStart=start;bestLength=bottom-start;}
                if(bestLength>image.Height*.29)edges.Add(new Rectangle(x,bestStart,1,bestLength));
            }
            var clusters=new List<Rectangle>();foreach(var edge in edges){if(clusters.Count>0&&edge.X-clusters[clusters.Count-1].Right<9){var last=clusters[clusters.Count-1];clusters[clusters.Count-1]=Rectangle.Union(last,edge);}else clusters.Add(edge);}
            var cards=new List<Rectangle>();for(int i=0;i+1<clusters.Count;i++){
                var a=clusters[i];var b=clusters[i+1];int w=b.Right-a.Left;
                if(w<image.Width*.12||w>image.Width*.19||Math.Abs(a.Top-b.Top)>image.Height*.06)continue;
                var card=Rectangle.FromLTRB(a.Left,Math.Min(a.Top,b.Top)-5,b.Right,Math.Max(a.Bottom,b.Bottom)+4);
                if(cards.Count==0||card.Left>cards[cards.Count-1].Right){cards.Add(card);i++;}
            }
            return cards.Count>=2&&cards.Count<=3?cards:new List<Rectangle>();
        }
        public static Rectangle FindBenchSlot(Bitmap image){int ignored;return FindBenchSlot(image,out ignored);}
        public static Rectangle FindBenchSlot(Bitmap image,out int measuredStride){double precise;var r=FindBenchSlot(image,out precise);measuredStride=(int)Math.Round(precise);return r;}
        public static Rectangle FindBenchSlot(Bitmap image,out double measuredStride){ measuredStride=0;
            var rows=new List<Tuple<int,List<Rectangle>>>();int left=(int)(image.Width*.25),right=(int)(image.Width*.74);
            for(int y=2;y<(int)(image.Height*.10);y++){
                var runs=new List<Rectangle>();int start=-1;
                for(int x=left;x<=right;x++){
                    var c=image.GetPixel(Math.Min(x,image.Width-1),y);bool border=x<right&&c.R>=35&&c.R<=135&&Math.Abs(c.R-c.G)<5&&Math.Abs(c.G-c.B)<9;
                    if(border&&start<0)start=x;
                    if(!border&&start>=0){int length=x-start;if(length>=image.Width*.027&&length<=image.Width*.047)runs.Add(new Rectangle(start,y,length,1));start=-1;}
                }
                if(runs.Count>=5)rows.Add(Tuple.Create(y,runs));
            }
            foreach(var top in rows)foreach(var bottom in rows){int height=bottom.Item1-top.Item1;if(height<image.Height*.05||height>image.Height*.085)continue;
                var aligned=top.Item2.Where(r=>bottom.Item2.Any(b=>Math.Abs(b.X-r.X)<=2&&Math.Abs(b.Width-r.Width)<=3)).OrderBy(r=>r.X).ToList();if(aligned.Count<5)continue;
                int stride=aligned.Zip(aligned.Skip(1),(a,b)=>b.X-a.X).OrderBy(v=>v).First();
                if(stride<image.Width*.037||stride>image.Width*.055)continue;
                var indices=aligned.Select(r=>(double)Math.Round((double)(r.X-aligned[0].X)/stride)).ToArray();double mean=indices.Average(),meanX=aligned.Average(r=>(double)r.X);double slope=aligned.Select((r,i)=>(indices[i]-mean)*(r.X-meanX)).Sum()/indices.Sum(i=>(i-mean)*(i-mean));double first=meanX-slope*mean;while(first-slope>image.Width*.26)first-=slope;
                measuredStride=slope;return new Rectangle((int)Math.Round(first),top.Item1,aligned[0].Width,height+1);
            }return Rectangle.Empty;
        }
        static bool GoldPixel(Color c){return c.R>115&&c.G>75&&c.B<c.G*.78&&c.R>c.G*1.03&&c.R<c.G*2;}
        public static RectangleF FindPortrait(Bitmap image){
            double best=0;RectangleF found=RectangleF.Empty;double scale=image.Height/720.0;
            int expectedX=(int)(image.Width*.067),expectedY=(int)(image.Height*.407);
            for(int radius=(int)(26*scale);radius<=(int)(33*scale);radius++)for(int cy=expectedY-12;cy<=expectedY+12;cy+=2)for(int cx=expectedX-12;cx<=expectedX+12;cx+=2){
                int hits=0;for(int a=195;a<=345;a+=10){double angle=a*Math.PI/180;int x=(int)Math.Round(cx+radius*Math.Cos(angle)),y=(int)Math.Round(cy+radius*Math.Sin(angle));bool gold=false;
                    for(int dy=-1;dy<=1&&!gold;dy++)for(int dx=-1;dx<=1;dx++)if(x+dx>=0&&y+dy>=0&&x+dx<image.Width&&y+dy<image.Height&&GoldPixel(image.GetPixel(x+dx,y+dy))){gold=true;break;}
                    if(gold)hits++;
                }
                double score=hits-Math.Abs(radius-29*scale)*.10-Math.Abs(cx-expectedX)*.01-Math.Abs(cy-expectedY)*.01;
                if(hits>=12&&score>best){best=score;found=new RectangleF(cx-radius,cy-radius,radius*2,radius*2);}
            }return found;
        }
        public static void AlignPortrait(CardResult card,RectangleF circle){AlignPortrait(card,circle,(int)Math.Round((circle.Left+circle.Width/2)/.067),(int)Math.Round(circle.Height*720/58));}
        public static void AlignPortrait(CardResult card,RectangleF circle,int clientWidth,int clientHeight){
            if(card==null||circle.IsEmpty)return;
            float cy=circle.Top+circle.Height/2,step=clientHeight*80f/720;
            int top=(int)Math.Round(cy-step/2),bottom=(int)Math.Round(cy+step/2);
            card.ArcCenterX=(float)(clientWidth*0.498127627098191);card.ArcCenterY=(float)(clientHeight*0.416108654643941);
            card.ArcRadius=(float)(clientHeight*0.443888775207369+clientHeight*2.0/720);card.ArcDepth=clientHeight*58f/720;
            double near=Math.Max(top,Math.Min(bottom,card.ArcCenterY)),far=Math.Abs(top-card.ArcCenterY)>Math.Abs(bottom-card.ArcCenterY)?top:bottom;
            int left=(int)Math.Floor(ArcLeft(card,near,true)-BadgePaint.TeamTextShift(card)),right=(int)Math.Ceiling(ArcLeft(card,far,false));
            card.Bounds=new Rectangle(left,top,right-left,bottom-top);

        }
        public static double PosterEdgeX(CardResult card,double y){
            // Skin/background boundary measured independently from the gold decoration.
            double width=card.ArcCenterX/0.498127627098191,height=card.ArcDepth*720/58;
            double cx=width*0.4939348006930161,cy=height*0.41633005293262404,radius=height*0.45274444678396664;
            return cx-Math.Sqrt(Math.Max(0,radius*radius-(y-cy)*(y-cy)));
        }
        public static double GoldArcX(CardResult card,double y){double radius=card.ArcRadius-card.ArcDepth*2/58;return card.ArcCenterX-Math.Sqrt(Math.Max(0,radius*radius-(y-card.ArcCenterY)*(y-card.ArcCenterY)));}
        public static double ArcLeft(CardResult card,double y,bool outer){double r=card.ArcRadius;return card.ArcCenterX-Math.Sqrt(Math.Max(0,r*r-(y-card.ArcCenterY)*(y-card.ArcCenterY)))-(outer?card.ArcDepth:0);}
        public static bool KeepChoices(bool sameScene,int newCount,bool header,TimeSpan age){return sameScene&&newCount<2&&age<TimeSpan.FromSeconds(header?3:1.5);}
        public static List<Row> ReasonableSkillOrders(IEnumerable<Row> source){
            var ranked=source.Where(r=>r.Rank>0).OrderBy(r=>r.Rank).ToList();
            if(ranked.Count==0)return ranked;
            return ranked.Where((row,index)=>index==0||(!double.IsNaN(row.Pick)&&row.Pick>=.10-1e-12&&row.GainLower>=0)).Take(4).ToList();
        }
        public static List<Row> NearBest(IEnumerable<Row> source){
            var ranked=source.Where(r=>r.Rank>0).ToList();if(ranked.Count==0)return ranked;
            var best=ranked[0];var selected=new List<Row>{best};
            if(double.IsNaN(best.Lower)||double.IsInfinity(best.Lower))return selected;
            double bestDisplay=Math.Round(best.Lower*1000,0,MidpointRounding.AwayFromZero);
            foreach(var row in ranked.Skip(1)){
                if(double.IsNaN(row.Lower)||double.IsInfinity(row.Lower))continue;
                if(bestDisplay-Math.Round(row.Lower*1000,0,MidpointRounding.AwayFromZero)<=5)selected.Add(row);
                if(selected.Count==3)break;
            }return selected;
        }
        public static CardResult Summoners(DataStore data,int hero,string scope,Rectangle area,Rectangle work){
            if(hero<=0)return null;
            var groups=data.AugmentGroups(hero,scope);var group=groups.FirstOrDefault(g=>g.Path=="champions/summoners");if(group==null)return null;
            var rows=NearBest(group.Rows);if(rows.Count==0)return null;
            var skills=groups.FirstOrDefault(g=>g.Path=="champions/skills");var skillRows=skills==null?new List<Row>():NearBest(skills.Rows);
            int width=Math.Min(160*(rows.Count+skillRows.Count)+(skillRows.Count>0?8:0),area.Width-30),height=108;
            int y=area.Height;
            return new CardResult{Card=new Candidate{Name="召唤师技能"},Recommendations=rows,SkillRecommendations=skillRows,Bounds=new Rectangle((area.Width-width)/2,y,width,height),Context=("")};
        }
        public static CardResult Match(List<TextLine> lines,DataStore data,int hero,string scope,int width,int height){
            object resource;if(!data.Resources["champions"].TryGetValue(hero.ToString(),out resource))return null;
            string name=CardMatcher.Normalize(Json.Str(Json.Get(resource,"name"))),title=CardMatcher.Normalize(Json.Str(Json.Get(resource,"title")));
            var label=lines.Where(l=>{string text=CardMatcher.Normalize(l.Text);return (text==name||text==title)&&l.Bounds.Left<width*.45&&l.Bounds.Top>height*.1&&l.Bounds.Bottom<height*.85;}).OrderBy(l=>l.Bounds.Left).FirstOrDefault();
            if(label==null)return null;
            var board=data.Groups(0,scope).FirstOrDefault(g=>g.Path=="champions/rows");if(board==null)return null;
            var row=board.Rows.FirstOrDefault(r=>(int)Json.Num(Json.Get(r.Raw,"id"),0)==hero);if(row==null)return null;
            int boxWidth=Math.Min(305,Math.Max(215,(int)(width*.235))),boxHeight=78;
            int x=Math.Max(5,Math.Min(width-boxWidth-5,(int)label.Bounds.Left-4));
            int y=Math.Min(height-boxHeight-5,(int)label.Bounds.Bottom+28);
            return new CardResult{Card=new Candidate{Id=hero,Name=data.Lookup("champions",hero),Title=label.Bounds},Row=row,Bounds=new Rectangle(x,y,boxWidth,boxHeight),Context="英雄全榜 · 原 #"+row.OldRank};
        }
    }
    public static class SelectionState {
        public static bool AllowsForeground(int foreground,int root,int client,int controller){return client>0&&(foreground==client||root==client||foreground==controller);}
        public static bool Eligible(ClientStatus state,bool enabled){return enabled&&state!=null&&state.IsMayhem&&state.Phase=="ChampSelect";}
    }
    public class SelectionOverlayController:IDisposable {
        readonly Func<DataStore> store;readonly Func<string> scope;readonly Func<ClientStatus> client;readonly Func<bool> enabled;
        readonly Timer positionTimer=new Timer();readonly Timer timer=new Timer();readonly List<BadgeWindow> badges=new List<BadgeWindow>();readonly BadgeWindow summoners=new BadgeWindow();readonly Action<string> status;
        Func<CaptureTarget> positionTarget=()=>{Rectangle area;IntPtr handle;return NativeOverlay.ForegroundClient(out area,out handle)?new CaptureTarget{Bounds=area,Handle=handle}:null;};
        NativeOverlay.LocationEvent locationCallback;IntPtr locationHook;bool moving;
        IntPtr presentedHandle;Size presentedSize;Point presentedOrigin;List<CardResult> presentedItems;bool presentedSpells;
        Rectangle benchSlot;double benchStride;Size benchSize;RectangleF portraitCircle;string choiceScope;
        string key,spellKey;CardResult spellResult;List<CardResult> results,choices=new List<CardResult>();
        bool disposed,busy;LocalOcr ocr;DateTime nextScan=DateTime.MinValue,choiceTime=DateTime.MinValue;Rectangle choiceArea;IntPtr choiceHandle;
        public SelectionOverlayController(Func<DataStore> data,Func<string> sample,Func<ClientStatus> state,Func<bool> active,Action<string> message){store=data;scope=sample;client=state;enabled=active;status=message;for(int i=0;i<15;i++)badges.Add(new BadgeWindow(true));timer.Interval=250;timer.Tick+=async(s,e)=>await Tick();timer.Start();positionTimer.Interval=16;positionTimer.Tick+=(s,e)=>FollowPosition();positionTimer.Start();locationCallback=(hook,evt,window,obj,child,thread,time)=>{if(!disposed&&window==presentedHandle&&obj==0&&child==0)FollowPosition();};locationHook=NativeOverlay.SetWinEventHook(0x800B,0x800B,IntPtr.Zero,locationCallback,0,0,2);}
        void UpdateBench(Rectangle found,double stride,Size size){if(benchSize!=size)key=null;benchSlot=SelectionMatcher.KeepBench(benchSlot,benchStride,found,stride,benchSize!=size,out benchStride);benchSize=size;}
        void ResetBench(){benchSlot=SelectionMatcher.KeepBench(benchSlot,benchStride,Rectangle.Empty,0,true,out benchStride);benchSize=Size.Empty;key=null;}
        bool LayoutBench(IntPtr handle,Rectangle area,List<CardResult> items){
            var cards=items.Where(c=>c.BenchSlot).ToArray();if(cards.Length==0)return false;
            int bottom=benchSlot.IsEmpty?(int)Math.Round(area.Height*64.0/720):benchSlot.Bottom;
            int height=benchSlot.IsEmpty?(int)Math.Round(area.Height*49.0/720):benchSlot.Height;
            var placements=BenchBadgeLayout.Calculate(area,Screen.FromHandle(handle).WorkingArea,cards.Select(c=>c.BenchIndex).ToArray(),null,1,bottom,height);
            bool changed=false;
            for(int i=0;i<cards.Length;i++){
                var r=placements[i].Bounds;r.Offset(-area.X,-area.Y);
                changed|=cards[i].Bounds!=r||cards[i].BenchOutside!=placements[i].Outside||cards[i].BenchFont!=placements[i].FontSize;
                cards[i].Bounds=r;cards[i].BenchOutside=placements[i].Outside;cards[i].BenchFont=placements[i].FontSize;
            }
            return changed;
        }
        void TrackPosition(IntPtr handle,Rectangle area,List<CardResult> items,bool spells){presentedHandle=handle;presentedSize=area.Size;presentedOrigin=area.Location;presentedItems=items;presentedSpells=spells;}
        void FollowPosition(){if(disposed||moving)return;moving=true;try{if(!enabled()){Hide();return;}var target=positionTarget();if(target==null){Hide();return;}Rectangle area=target.Bounds;IntPtr handle=target.Handle;UpdateBench(Rectangle.Empty,0,area.Size);if(presentedItems==null)return;if(handle!=presentedHandle||area.Size!=presentedSize){Hide();return;}bool layoutChanged=LayoutBench(handle,area,presentedItems);if(area.Location==presentedOrigin&&badges.Any(b=>b.Visible)){if(layoutChanged)Present(presentedItems,area.Location);return;}presentedOrigin=area.Location;var moves=new List<Tuple<IntPtr,Rectangle>>();for(int i=0;i<presentedItems.Count&&i<badges.Count;i++){var badge=badges[i];badge.Present(presentedItems[i],area.Location);var r=presentedItems[i].Bounds;r.Offset(area.Location);moves.Add(Tuple.Create(badge.Handle,r));}if(presentedSpells&&spellResult!=null){var r=spellResult.Bounds;r.Offset(area.Location);var visible=Rectangle.Intersect(r,Screen.FromRectangle(area).WorkingArea);if(visible.Width>0&&visible.Height>0){visible.Offset(-r.X,-r.Y);var old=summoners.Region;summoners.Region=new Region(visible);if(old!=null)old.Dispose();if(!summoners.Visible)summoners.PresentClipped(spellResult,area.Location,Screen.FromRectangle(area).WorkingArea);moves.Add(Tuple.Create(summoners.Handle,r));}else summoners.Hide();}else summoners.Hide();NativeOverlay.MoveTogether(moves);}finally{moving=false;}}
        public static bool ShouldScanInitial(bool enabled,bool foregroundClient,bool busy,DateTime now,DateTime next){return enabled&&foregroundClient&&!busy&&now>=next;}
        DateTime nextTrace=DateTime.MinValue;
        void Trace(string message){
            if(DateTime.UtcNow<nextTrace)return;nextTrace=DateTime.UtcNow.AddSeconds(3);
            try{string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"选人诊断.log");if(File.Exists(file)&&new FileInfo(file).Length>200000)File.WriteAllText(file,"");File.AppendAllText(file,DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}
        }
        async Task Tick(){
            if(disposed)return;if(!enabled()){Hide();choices.Clear();return;}
            Rectangle visibleArea;IntPtr visibleHandle;bool foreground=NativeOverlay.ForegroundClient(out visibleArea,out visibleHandle);if(foreground)UpdateBench(Rectangle.Empty,0,visibleArea.Size);
            if(busy){if(!foreground)Hide();return;}
            try{
                var data=store();string sample=scope();
                if(ShouldScanInitial(enabled(),foreground,busy,DateTime.UtcNow,nextScan)){
                    busy=true;nextScan=DateTime.UtcNow.AddMilliseconds(450);
                    try{
                        List<TextLine> lines;using(var pixels=NativeOverlay.Capture(visibleArea)){portraitCircle=SelectionMatcher.FindPortrait(pixels);double foundStride;var found=SelectionMatcher.FindBenchSlot(pixels,out foundStride);UpdateBench(found,foundStride,visibleArea.Size);lines=await Task.Run(async()=>{if(ocr==null)ocr=new LocalOcr();return await ocr.ReadInitial(pixels);});}
                        if(disposed)return;Rectangle afterArea=Rectangle.Empty;IntPtr afterHandle;
                        if(!enabled()||!NativeOverlay.ForegroundClient(out afterArea,out afterHandle)||afterArea.Size!=visibleArea.Size||afterHandle!=visibleHandle||store()!=data||scope()!=sample){if(!afterArea.IsEmpty&&afterArea.Size!=visibleArea.Size)UpdateBench(Rectangle.Empty,0,afterArea.Size);Hide();choices.Clear();return;}
                        visibleArea=afterArea;var observed=SelectionMatcher.InitialChoices(lines,data,sample,visibleArea.Width,visibleArea.Height);
                        bool header=lines.Any(l=>CardMatcher.Normalize(l.Text).Contains("选择你的英雄"));
                        bool same=choiceArea.Size==visibleArea.Size&&choiceHandle==visibleHandle&&choiceScope==sample;
                        if(observed.Count>=2){
                            bool retain=same&&choices.Count>observed.Count&&observed.All(c=>choices.Any(old=>old.Card.Id==c.Card.Id))&&DateTime.UtcNow-choiceTime<TimeSpan.FromSeconds(3);
                            if(!retain){
                                if(!same||!choices.Select(c=>c.Card.Id).SequenceEqual(observed.Select(c=>c.Card.Id)))choices=observed;
                                choiceArea=visibleArea;choiceHandle=visibleHandle;choiceScope=sample;choiceTime=DateTime.UtcNow;
                            }
                        }else if(observed.Count==1&&(!same||choices.Count<2||DateTime.UtcNow-choiceTime>TimeSpan.FromSeconds(3))){
                            choices=observed;choiceArea=visibleArea;choiceHandle=visibleHandle;choiceScope=sample;choiceTime=DateTime.UtcNow;
                        }else if(!SelectionMatcher.KeepChoices(same,observed.Count,header,DateTime.UtcNow-choiceTime))choices.Clear();
                        Trace("foregroundClient=true; header="+header+"; candidates="+observed.Count+"; displayed="+choices.Count);

                    }finally{busy=false;}
                }
                var state=client();if(!SelectionState.Eligible(state,enabled()))ResetBench();
                if(foreground&&choices.Count>0&&choiceHandle==visibleHandle&&choiceArea.Size==visibleArea.Size&&DateTime.UtcNow-choiceTime<TimeSpan.FromSeconds(3)){
                    TrackPosition(visibleHandle,visibleArea,choices,false);summoners.Hide();Present(choices,visibleArea.Location);status("初始候选 · 统计学胜率");return;
                }
                if(!SelectionState.Eligible(state,enabled())){Hide();Trace("foregroundClient="+foreground+"; phase="+(state==null?"unavailable":state.Phase));return;}
                Rectangle area;IntPtr handle;if(!NativeOverlay.SelectionTarget(state.ProcessId,out area,out handle)){Hide();return;}UpdateBench(Rectangle.Empty,0,area.Size);
                string currentKey=data.Fingerprint+":"+state.Champion+":"+sample+":"+area.Size+":"+string.Join(",",state.Bench)+":"+string.Join(",",state.Team);
                if(key!=currentKey){results=SelectionMatcher.FromSession(data,state,sample,area.Width,area.Height);key=currentKey;}
                if(results==null||results.Count==0){Hide();status("选人标注 · 等待识别候选卡片或当前英雄");return;}
                if(foreground&&visibleArea==area&&state.Champion>0&&state.Team.Length<=1&&results.Count>0)SelectionMatcher.AlignPortrait(results[0],portraitCircle,area.Width,area.Height);
                LayoutBench(handle,area,results);
                TrackPosition(handle,area,results,true);Present(results,area.Location);
                var work=Screen.FromRectangle(area).WorkingArea;string nextSpellKey=currentKey+":"+area+":"+work;
                if(spellKey!=nextSpellKey){spellResult=SelectionMatcher.Summoners(data,state.Champion,sample,area,work);spellKey=nextSpellKey;}
                if(spellResult==null)summoners.Hide();else summoners.PresentClipped(spellResult,area.Location,work);
                status("选人标注 · "+results.Count+" 项 · 统计学胜率");
            }catch(Exception e){Hide();choices.Clear();status("选人识别暂不可用："+e.GetType().Name);Trace("error="+e.GetType().Name);}
        }
        void Present(List<CardResult> items,Point origin){for(int i=0;i<badges.Count;i++){if(i<items.Count)badges[i].Present(items[i],origin);else badges[i].Hide();}}
        void Hide(){presentedItems=null;summoners.Hide();foreach(var badge in badges)badge.Hide();}
        public void Dispose(){disposed=true;if(locationHook!=IntPtr.Zero){NativeOverlay.UnhookWinEvent(locationHook);locationHook=IntPtr.Zero;}positionTimer.Stop();positionTimer.Dispose();summoners.Dispose();timer.Stop();timer.Dispose();foreach(var badge in badges)badge.Dispose();}
    }
}




