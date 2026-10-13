using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ConfidenceDesktop {
    public class ReplayForm:Form {
        Bitmap scene;OverlayController controller;int clicks,frames;bool blank;
        public ReplayForm(string image){Text="截图回放验证 · 正在识别";scene=new Bitmap(image);DoubleBuffered=true;StartPosition=FormStartPosition.CenterScreen;
            double ratio=Math.Min(1,Math.Min((Screen.PrimaryScreen.WorkingArea.Width-100.0)/scene.Width,(Screen.PrimaryScreen.WorkingArea.Height-100.0)/scene.Height));
            ClientSize=new Size((int)(scene.Width*ratio),(int)(scene.Height*ratio));FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
            Shown+=(s,e)=>{try{var store=new DataStore();store.Load(DataStore.DefaultRoot);controller=new OverlayController(()=>store,()=>31,()=>"high",()=>-1,()=>false,message=>{
                if(message.StartsWith("识别到")){frames++;bool locations=true,through=true;foreach(var badge in controller.Windows){var expected=badge.Result.Bounds;expected.Offset(PointToScreen(Point.Empty));NativeOverlay.NativeRect actual;NativeOverlay.GetWindowRect(badge.Handle,out actual);locations&=expected==Rectangle.FromLTRB(actual.Left,actual.Top,actual.Right,actual.Bottom);through&=NativeOverlay.WindowFromPoint(new Point(expected.X+expected.Width/2,expected.Y+expected.Height/2))==Handle;}Text="实时回放 · "+frames+" 帧 · 位置 "+locations+" · 点击穿透 "+through+" · 收到点击 "+clicks;}
                else Text="实时回放 · "+message+" · 可见浮层 "+controller.Windows.Count(w=>w.Visible);
            },id=>{},()=>NativeOverlay.GetForegroundWindow()==Handle?new CaptureTarget{Handle=Handle,Bounds=new Rectangle(PointToScreen(Point.Empty),ClientSize)}:null);controller.Enabled=true;}catch(Exception ex){Text="回放失败："+ex.Message;}};
            MouseDown+=(s,e)=>{clicks++;};KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Space){blank=!blank;Invalidate();}};FormClosed+=(s,e)=>{if(controller!=null)controller.Dispose();scene.Dispose();};
        }
        protected override void OnPaint(PaintEventArgs e){if(blank)e.Graphics.Clear(Color.FromArgb(30,35,40));else if(scene!=null)e.Graphics.DrawImage(scene,ClientRectangle);}
    }
}
