using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Windows.Forms;
namespace ConfidenceDesktop {
 public static class DesktopTheme {
  public static Color Canvas {get{return ThemeManager.Current["bg"];}}
  public static Color Surface {get{return ThemeManager.Current["panel"];}}
  public static Color Raised {get{return ThemeManager.Current["header"];}}
  public static Color Line {get{return ThemeManager.Current["line"];}}
  public static Color Text {get{return ThemeManager.Current["desktop-text"];}}
  public static Color Muted {get{return ThemeManager.Current["desktop-muted"];}}
  public static Color Accent {get{return ThemeManager.Current["accent-ink"];}}
  public static Color SelectedText {get{return ThemeManager.Current["selected-text"];}}
  public static Color Selected {get{return ThemeManager.Current["selected"];}}
  public static readonly Font Body=new Font("Microsoft YaHei UI",10),Strong=new Font("Microsoft YaHei UI",10,FontStyle.Bold),Title=new Font("Microsoft YaHei UI",13,FontStyle.Bold),Small=new Font("Microsoft YaHei UI",9);
  public static GraphicsPath Rounded(Rectangle bounds,int radius){var p=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));if(d<2){p.AddRectangle(bounds);return p;}p.AddArc(bounds.Left,bounds.Top,d,d,180,90);p.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);p.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  public static void Box(Graphics g,Rectangle bounds,Color fill,Color border,int radius=8){g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Rounded(bounds,radius)){using(var b=new SolidBrush(fill))g.FillPath(b,p);using(var pen=new Pen(border))g.DrawPath(pen,p);}}
 }
 public class ModernButton:Button {
  bool hovered,pressed;
  public ModernButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Font=DesktopTheme.Body;}
  protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?DesktopTheme.Canvas:Parent.BackColor);var selected=(pressed||(this is NavigationButton&&((NavigationButton)this).Selected));var fill=selected?DesktopTheme.Selected:pressed?DesktopTheme.Selected:hovered?DesktopTheme.Raised:BackColor;var bounds=new Rectangle(1,2,Math.Max(1,Width-3),Math.Max(1,Height-5));DesktopTheme.Box(e.Graphics,bounds,fill,Focused?DesktopTheme.Accent:FlatAppearance.BorderSize>0?DesktopTheme.Line:fill);TextRenderer.DrawText(e.Graphics,Text,Font,bounds,Enabled?(selected?DesktopTheme.SelectedText:ForeColor):DesktopTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
 }
 public class ModernComboBox:ComboBox {
  public ModernComboBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=24;}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?DesktopTheme.Canvas:Parent.BackColor);var r=new Rectangle(0,0,Width-1,Height-1);DesktopTheme.Box(e.Graphics,r,DesktopTheme.Surface,DesktopTheme.Line,6);TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(10,0,Math.Max(1,Width-34),Height),Enabled?DesktopTheme.Text:DesktopTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);using(var p=new Pen(DesktopTheme.Muted,1.5f))e.Graphics.DrawLines(p,new[]{new Point(Width-20,Height/2-2),new Point(Width-15,Height/2+3),new Point(Width-10,Height/2-2)});}
  protected override void OnSelectedIndexChanged(EventArgs e){Invalidate();base.OnSelectedIndexChanged(e);}protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
 }
 public class ThemeComboBox:ModernComboBox {
  void PaintChoice(Graphics g,Rectangle bounds,int index,bool selected){
   if(index<0||index>=ThemePalette.All.Length)return;
   var theme=ThemePalette.All[index];
   using(var brush=new SolidBrush(selected?DesktopTheme.Selected:DesktopTheme.Surface))g.FillRectangle(brush,bounds);
   var swatch=new Rectangle(bounds.Left+10,bounds.Top+(bounds.Height-20)/2,20,20);
   g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var circle=new GraphicsPath()){
    circle.AddEllipse(swatch);var saved=g.Save();g.SetClip(circle);
    using(var background=new SolidBrush(theme["panel"]))g.FillRectangle(background,swatch);
    using(var accent=new SolidBrush(theme["gold"]))g.FillPolygon(accent,new[]{new Point(swatch.Left,swatch.Bottom),new Point(swatch.Right,swatch.Top),new Point(swatch.Right,swatch.Bottom)});
    g.Restore(saved);
   }
   TextRenderer.DrawText(g,theme.Name,Font,new Rectangle(bounds.Left+40,bounds.Top,bounds.Width-100,bounds.Height),selected?DesktopTheme.SelectedText:DesktopTheme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
   if(theme.Id=="piltover"){
    var badge=new Rectangle(bounds.Right-50,bounds.Top+(bounds.Height-20)/2,42,20);
    DesktopTheme.Box(g,badge,DesktopTheme.Selected,DesktopTheme.SelectedText,10);
    TextRenderer.DrawText(g,"默认",DesktopTheme.Small,badge,DesktopTheme.SelectedText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
   }
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);PaintChoice(e.Graphics,new Rectangle(4,3,Math.Max(1,Width-34),Math.Max(1,Height-6)),SelectedIndex,false);}
  protected override void OnDrawItem(DrawItemEventArgs e){base.OnDrawItem(e);PaintChoice(e.Graphics,e.Bounds,e.Index,(e.State&DrawItemState.Selected)!=0);}
 }
 public class InputSurface:Panel {
  readonly TextBox input;
  public InputSurface(TextBox box){input=box;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Size=new Size(box.Width,34);Margin=box.Margin;box.BorderStyle=BorderStyle.None;box.BackColor=DesktopTheme.Surface;box.ForeColor=DesktopTheme.Text;box.Location=new Point(10,7);box.Width=Width-20;box.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;Controls.Add(box);box.GotFocus+=(s,e)=>Invalidate();box.LostFocus+=(s,e)=>Invalidate();Click+=(s,e)=>box.Focus();}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?DesktopTheme.Canvas:Parent.BackColor);DesktopTheme.Box(e.Graphics,new Rectangle(0,0,Width-1,Height-1),DesktopTheme.Surface,DesktopTheme.Line,6);}
 }
}

