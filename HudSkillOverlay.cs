using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ConfidenceDesktop {
    public static class HudSkillSettings {
        public static string PathName {get{return Path.Combine(AppVersion.LocalDataDirectory,"局内加点设置.json");}}
        public static int Load(string path=null){try{var value=Json.Num(Json.Get(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path??PathName)),"mode"),2);return value==0||value==1||value==2?(int)value:2;}catch{return 2;}}
        public static void Save(int mode,string path=null){if(mode<0||mode>2)throw new ArgumentOutOfRangeException("mode");path=path??PathName;Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new{mode=mode}));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
    public static class HudAnchor {
        public static Rectangle Find(Bitmap bottom,int fullHeight,Rectangle excluded=default(Rectangle)){
            if(!excluded.IsEmpty)excluded.Inflate(3,3);int w=bottom.Width,h=bottom.Height;var bits=bottom.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);var gold=new bool[w*h];
            try{var row=new byte[w*4];for(int y=0;y<h;y++){Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),row,0,row.Length);for(int x=0;x<w;x++){int b=row[x*4],g=row[x*4+1],r=row[x*4+2];gold[y*w+x]=r>45&&g>35&&r>g*1.03&&g>b*1.15;}}}finally{bottom.UnlockBits(bits);}
            int horizontal=Math.Max(30,(int)(fullHeight*.045));double best=1.7;Rectangle found=Rectangle.Empty;
            for(int x=(int)(w*.15);x<Math.Min(w-horizontal-2,w*.52);x+=2)for(int y=0;y<h-fullHeight*.05;y+=2){if(excluded.Contains(x,y))continue;int height=h-y;if(height>fullHeight*.17||height<fullHeight*.055)continue;
                int vertical=0;for(int j=1;j<20;j++){int yy=Math.Min(h-1,y+height*j/20);if(gold[yy*w+x]||gold[yy*w+x+1])vertical++;}if(vertical<16)continue;
                int across=0;for(int j=5;j<horizontal;j++)if(gold[y*w+x+j]||gold[(y+1)*w+x+j])across++;
                double score=vertical/19.0+across/(double)(horizontal-5);if(score>best){best=score;found=new Rectangle(x,y,horizontal,height);}
            }return found;
        }
        public static Rectangle Recommendation(Rectangle stats,int gameWidth,int columns=1,int gameHeight=0){int height=gameHeight>0?Math.Max(80,Math.Min(140,(int)Math.Round(gameHeight*.0868))):stats.Height;int gap=Math.Max(6,height/15),width=Math.Min(170,Math.Max(140,(int)Math.Round(height*1.4)));width=Math.Max(1,Math.Min(width*columns,stats.Left-gap));return new Rectangle(Math.Max(0,stats.Left-width-gap),stats.Bottom-height,width,height);}
    }
    public sealed class HudAnchorStabilizer {
        Rectangle anchor;Size size;IntPtr window;
        public Rectangle Get(Size viewport,IntPtr handle){return size==viewport&&window==handle?anchor:Rectangle.Empty;}
        public Rectangle Apply(Rectangle observed,Size viewport,IntPtr handle){
            if(size!=viewport||window!=handle){anchor=Rectangle.Empty;size=viewport;window=handle;}
            if(anchor.IsEmpty&&!observed.IsEmpty)anchor=observed;
            return anchor;
        }
    }
    public sealed class HudSkillBackground : BadgeWindow {
        public HudSkillBackground(){Opacity=.90;}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=DesktopTheme.Rounded(new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),0))
            using(var brush=new SolidBrush(ThemeManager.Current.Overlay["panel"]))e.Graphics.FillPath(brush,path);
        }
        public void PresentPanel(Rectangle area){bool moved=Bounds!=area,shown=Visible;if(moved)Bounds=area;if(!shown)Show();if(moved||!shown)NativeOverlay.SetWindowPos(Handle,new IntPtr(-1),area.X,area.Y,area.Width,area.Height,0x0010|0x0040);}
    }
    public sealed class HudSkillOverlay : IDisposable {
        readonly Func<DataStore> store;readonly Func<int> hero;readonly Func<string> scope;readonly Func<bool> allowed;readonly Func<int> displayMode;
        readonly Timer timer=new Timer();readonly BadgeWindow badge=new BadgeWindow();readonly HudSkillBackground background=new HudSkillBackground();
        readonly HudAnchorStabilizer anchorPosition=new HudAnchorStabilizer();
        bool busy,disposed;string key;CardResult result;DateTime nextScan=DateTime.MinValue;
        public HudSkillOverlay(Func<DataStore> data,Func<int> champion,Func<string> sample,Func<bool> active,Func<int> preference=null){displayMode=preference??(()=>2);store=data;hero=champion;scope=sample;allowed=active;timer.Interval=100;timer.Tick+=async(s,e)=>await Tick();timer.Start();}
        async Task Tick(){if(disposed)return;Rectangle area;IntPtr handle;if(displayMode()==0||!allowed()||!NativeOverlay.VisibleGame(out area,out handle)){Hide();nextScan=DateTime.MinValue;return;}
            var data=store();int champion=hero();string sample=scope();int mode=displayMode();string current=data.Fingerprint+":"+champion+":"+sample+":"+area.Size+":"+mode;
            if(key!=current){key=current;result=null;Hide();nextScan=DateTime.MinValue;}
            if(result!=null)Present(result,area.Location);
            if(busy||champion<=0||DateTime.UtcNow<nextScan)return;busy=true;nextScan=DateTime.UtcNow.AddSeconds(2);
            try{int top=(int)(area.Height*.78);var anchor=anchorPosition.Get(area.Size,handle);
                if(anchor.IsEmpty)using(var pixels=NativeOverlay.Capture(new Rectangle(area.X,area.Y+top,area.Width,area.Height-top))){
                    var excluded=result==null?Rectangle.Empty:result.Bounds;excluded.Offset(0,-top);anchor=await Task.Run(()=>HudAnchor.Find(pixels,area.Height,excluded));
                }
                var group=await Task.Run(()=>data.AugmentGroups(champion,sample).FirstOrDefault(g=>g.Path=="champions/skills"));
                if(disposed)return;Rectangle now;IntPtr nowHandle;if(displayMode()!=mode||!allowed()||!NativeOverlay.VisibleGame(out now,out nowHandle)||nowHandle!=handle||now.Size!=area.Size||hero()!=champion||scope()!=sample||store()!=data){Hide();return;}
                anchor=anchorPosition.Apply(anchor,area.Size,handle);
                if(anchor.IsEmpty||group==null||group.Rows.Count==0){result=null;Hide();return;}
                anchor.Offset(0,top);var rows=SelectionMatcher.ReasonableSkillOrders(group.Rows,mode==1?1:3);var row=rows.FirstOrDefault();if(row==null){result=null;Hide();return;}
                result=new CardResult{Card=new Candidate{Name=row.Name},Row=row,SkillRecommendations=rows,HudSkill=true,Context="",Bounds=HudAnchor.Recommendation(anchor,area.Width,rows.Count,area.Height)};Present(result,now.Location);
            }catch{result=null;Hide();}finally{busy=false;}
        }
        void Hide(){badge.Hide();background.Hide();}
        void Present(CardResult card,Point origin){var bounds=card.Bounds;bounds.Offset(origin);background.PresentPanel(bounds);badge.Present(card,origin);}
        public void Dispose(){disposed=true;timer.Stop();timer.Dispose();badge.Dispose();background.Dispose();}
    }
}
