using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace ConfidenceDesktop {
    public static class BadgePaint {
        public static readonly Color TransparentColor=Color.Black;
        public static readonly Color Gold=DesktopTheme.Accent;
        public static readonly Color Jade=Color.FromArgb(0,182,159);

        public static string SkillPickText(double pick){
            return "选取 "+(double.IsNaN(pick)?"暂无数据":pick<.01?"<1%":Math.Round(pick*100,0,MidpointRounding.AwayFromZero).ToString("0")+"%");
        }
        public static Color RateColor(double value){
            if(double.IsNaN(value))return DesktopTheme.Muted;
            var red=Color.FromArgb(230,92,100);var yellow=DesktopTheme.Accent;var green=Jade;
            double t=Math.Max(0,Math.Min(1,(value-.45)/.10));var from=t<=.5?red:yellow;var to=t<=.5?yellow:green;
            double weight=t<=.5?t*2:(t-.5)*2;
            return Color.FromArgb((int)Math.Round(from.R+(to.R-from.R)*weight),(int)Math.Round(from.G+(to.G-from.G)*weight),(int)Math.Round(from.B+(to.B-from.B)*weight));
        }

        static void Text(Graphics g,string value,Font font,Color color,RectangleF area,StringAlignment align=StringAlignment.Center){
            using(var format=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
            using(var brush=new SolidBrush(color))g.DrawString(value??"",font,brush,area,format);
        }
        static void Line(Graphics g,float x1,float y1,float x2,float y2){using(var pen=new Pen(DesktopTheme.Line))g.DrawLine(pen,x1,y1,x2,y2);}
        static void Panel(Graphics g,Rectangle bounds,int headerHeight=0){
            var outer=new Rectangle(bounds.X,bounds.Y,Math.Max(1,bounds.Width-1),Math.Max(1,bounds.Height-1));
            using(var path=DesktopTheme.Rounded(outer,0)){
                using(var brush=new SolidBrush(DesktopTheme.Surface))g.FillPath(brush,path);
                if(headerHeight>0){var state=g.Save();g.SetClip(path);using(var brush=new SolidBrush(DesktopTheme.Raised))g.FillRectangle(brush,bounds.X,bounds.Y,bounds.Width,headerHeight);g.Restore(state);}
                using(var pen=new Pen(DesktopTheme.Line))g.DrawPath(pen,path);
            }
            if(headerHeight>0)Line(g,bounds.Left+1,bounds.Top+headerHeight,bounds.Right-2,bounds.Top+headerHeight);
        }
        static void Frame(Graphics g,Rectangle bounds){
            using(var path=DesktopTheme.Rounded(new Rectangle(bounds.X,bounds.Y,Math.Max(1,bounds.Width-1),Math.Max(1,bounds.Height-1)),0))
            using(var pen=new Pen(DesktopTheme.Line))g.DrawPath(pen,path);
        }
        static void Metric(Graphics g,string label,string value,Color color,RectangleF area){
            using(var small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var number=new Font("Microsoft YaHei UI",16,FontStyle.Bold,GraphicsUnit.Pixel)){
                Text(g,label,small,DesktopTheme.Muted,new RectangleF(area.X,area.Y,area.Width,15));
                Text(g,value,number,color,new RectangleF(area.X,area.Y+15,area.Width,Math.Max(18,area.Height-15)));
            }
        }
        public static string ContextTitle(string title,string context){return string.IsNullOrEmpty(context)?title:title+"  ·  "+context;}
        public static string SkillContext(string context,double pick){return string.IsNullOrEmpty(context)?SkillPickText(pick):context+" · "+SkillPickText(pick);}
        static void RecommendationSection(Graphics g,Rectangle bounds,string title,string context,List<Row> rows){
            if(rows==null||rows.Count==0)return;
            float y=bounds.Top,column=bounds.Width/(float)rows.Count;
            using(var label=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var heading=new Font("Microsoft YaHei UI",15,FontStyle.Bold,GraphicsUnit.Pixel)){
                Text(g,ContextTitle(title,context),label,DesktopTheme.Muted,new RectangleF(bounds.X+12,y+5,bounds.Width-24,20));
                Line(g,bounds.X+12,y+27,bounds.Right-12,y+27);
                for(int i=0;i<rows.Count;i++){
                    var row=rows[i];float x=bounds.X+i*column;
                    if(i>0)Line(g,x,y+35,x,y+97);
                    Text(g,"#"+row.Rank+"  "+row.Name,heading,i==0?Gold:DesktopTheme.Text,new RectangleF(x+5,y+31,column-10,23));
                    Metric(g,"统计学胜率",Statistics.Pct(row.Lower),RateColor(row.Lower),new RectangleF(x+2,y+58,column/2-4,38));
                    Metric(g,"胜率提升",Statistics.GainText(row.GainLower),RateColor(double.IsNaN(row.GainLower)?double.NaN:.5+row.GainLower),new RectangleF(x+column/2+2,y+58,column/2-4,38));
                }
            }
        }
        public static string BenchName(Graphics g,string fullName,float width,float rowHeight,out float fontSize){
            var parts=(fullName??"").Split('·');string title=parts[0].Trim(),name=parts.Length>1?parts[parts.Length-1].Trim():title;float start=Math.Min(13,rowHeight-3);
            using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}){
                foreach(var text in new[]{name,title})for(float size=start;size>=9;size-=.5f)using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))if(g.MeasureString(text,font,int.MaxValue,format).Width<=width){fontSize=size;return text;}
                string shorter=title.Length<=name.Length?title:name;
                for(float size=start;size>8;size-=.5f)using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))if(g.MeasureString(shorter,font,int.MaxValue,format).Width<=width){fontSize=size;return shorter;}
                fontSize=8;return shorter;
            }
        }
        public static GraphicsPath ArcPath(CardResult card,Rectangle bounds){
            var path=new GraphicsPath();float dx=bounds.X-card.Bounds.X,dy=bounds.Y-card.Bounds.Y,cx=card.ArcCenterX+dx,cy=card.ArcCenterY+dy;
            float inner=card.ArcRadius,top=card.Bounds.Top,bottom=card.Bounds.Bottom;
            float innerTop=(float)(180-Math.Asin((top-card.ArcCenterY)/inner)*180/Math.PI),innerBottom=(float)(180-Math.Asin((bottom-card.ArcCenterY)/inner)*180/Math.PI);
            // Both edges follow the same arc, separated by a fixed horizontal width.
            path.AddArc(cx-card.ArcDepth-inner,cy-inner,2*inner,2*inner,innerTop,innerBottom-innerTop);
            path.AddLine((float)SelectionMatcher.ArcLeft(card,bottom,true)+dx,bottom+dy,(float)SelectionMatcher.ArcLeft(card,bottom,false)+dx,bottom+dy);
            path.AddArc(cx-inner,cy-inner,2*inner,2*inner,innerBottom,innerTop-innerBottom);path.CloseFigure();return path;
        }
        public static float TeamTextShift(CardResult card){return card.ArcDepth*10f/58;}
        public static GraphicsPath TextArcPath(CardResult card,Rectangle bounds){return ArcPath(new CardResult{Bounds=card.Bounds,ArcCenterX=card.ArcCenterX,ArcCenterY=card.ArcCenterY,ArcRadius=card.ArcRadius,ArcDepth=card.ArcDepth+TeamTextShift(card)},bounds);}
        public static float BenchScale(Rectangle bounds){return bounds.Height/15f;}
        public static float BenchPadding(Rectangle bounds){return BenchScale(bounds);}
        public static float BenchGap(Rectangle bounds){return 3*BenchScale(bounds);}
        static void OutlinedText(Graphics g,string text,Font font,Color color,RectangleF area,StringAlignment align=StringAlignment.Center){
            using(var format=new StringFormat(StringFormat.GenericTypographic)){format.Alignment=align;format.LineAlignment=StringAlignment.Center;format.FormatFlags|=StringFormatFlags.NoWrap;
                using(var path=new GraphicsPath()){
                    path.AddString(text??"",font.FontFamily,(int)font.Style,font.Size,area,format);
                    using(var outline=new Pen(Color.FromArgb(235,6,10,14),Math.Max(1,font.Size/13f)){LineJoin=LineJoin.Round}){outline.LineJoin=LineJoin.Round;g.DrawPath(outline,path);}
                    using(var brush=new SolidBrush(color))g.FillPath(brush,path);
                }
            }
        }
        static void ArcOutlinedText(Graphics g,string text,Font font,Color color,RectangleF area,CardResult card,Rectangle bounds){
            using(var path=new GraphicsPath())using(var format=new StringFormat(StringFormat.GenericTypographic))using(var outline=new Pen(Color.FromArgb(235,6,10,14),Math.Max(1,font.Size/13f)){LineJoin=LineJoin.Round}){
                format.Alignment=StringAlignment.Center;format.LineAlignment=StringAlignment.Center;format.FormatFlags|=StringFormatFlags.NoWrap;path.AddString(text,font.FontFamily,(int)font.Style,font.Size,area,format);
                var ink=path.GetBounds();ink.Inflate(outline.Width/2,outline.Width/2);double globalY=(ink.Top+ink.Bottom)/2+card.Bounds.Y-bounds.Y;
                float target=(float)(SelectionMatcher.PosterEdgeX(card,globalY)-card.ArcDepth*8/58)+bounds.X-card.Bounds.X;
                using(var move=new Matrix()){move.Translate(target-ink.Right,0);path.Transform(move);}g.DrawPath(outline,path);using(var brush=new SolidBrush(color))g.FillPath(brush,path);
            }
        }
        static void ArcBadge(Graphics g,Rectangle bounds,CardResult card){
            using(var path=TextArcPath(card,bounds)){var saved=g.Save();g.SetClip(path,CombineMode.Intersect);
                double y=card.Bounds.Top+card.Bounds.Height/2.0;float center=(float)((SelectionMatcher.ArcLeft(card,y,true)+SelectionMatcher.ArcLeft(card,y,false))/2)+bounds.X-card.Bounds.X-TeamTextShift(card);
                float textWidth=card.ArcDepth-4,cy=bounds.Y+bounds.Height/2f,size=Math.Min(13,card.Bounds.Height/4f);
                using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})while(size>8){using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))if(new[]{"#888","100.0%",card.Row==null?"暂无":"#"+card.Row.Rank,card.Row==null?"数据":Statistics.Pct(card.Row.Lower)}.All(text=>g.MeasureString(text,font,int.MaxValue,format).Width<=textWidth))break;size=Math.Max(8,size-.5f);}
                using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel)){
                    ArcOutlinedText(g,card.Row==null?"暂无":"#"+card.Row.Rank,font,card.Row==null?DesktopTheme.Muted:Gold,new RectangleF(center-textWidth/2,cy-24,textWidth,24),card,bounds);
                    ArcOutlinedText(g,card.Row==null?"数据":Statistics.Pct(card.Row.Lower),font,card.Row==null?DesktopTheme.Muted:RateColor(card.Row.Lower),new RectangleF(center-textWidth/2,cy,textWidth,24),card,bounds);
                }g.Restore(saved);
            }
        }
        public static float BenchTextWidth(Graphics g,string text,Font font){
            if(string.IsNullOrEmpty(text))return 0;
            using(var path=new GraphicsPath())using(var format=new StringFormat(StringFormat.GenericTypographic))using(var outline=new Pen(Color.Black,Math.Max(1,font.Size/13f)){LineJoin=LineJoin.Round}){
                format.FormatFlags|=StringFormatFlags.NoWrap;path.AddString(text,font.FontFamily,(int)font.Style,font.Size,PointF.Empty,format);return path.GetBounds().Width+outline.Width;
            }
        }
        static void BenchOutlinedText(Graphics g,string text,Font font,Color color,RectangleF area,StringAlignment align){
            using(var path=new GraphicsPath())using(var format=new StringFormat(StringFormat.GenericTypographic))using(var outline=new Pen(Color.FromArgb(235,6,10,14),Math.Max(1,font.Size/13f)){LineJoin=LineJoin.Round}){
                format.FormatFlags|=StringFormatFlags.NoWrap;path.AddString(text??"",font.FontFamily,(int)font.Style,font.Size,PointF.Empty,format);var ink=path.GetBounds();ink.Inflate(outline.Width/2,outline.Width/2);
                float x=align==StringAlignment.Near?area.Left:align==StringAlignment.Far?area.Right-ink.Width:area.Left+(area.Width-ink.Width)/2;
                using(var move=new Matrix()){move.Translate(x-ink.Left,area.Top+(area.Height-ink.Height)/2-ink.Top);path.Transform(move);}outline.LineJoin=LineJoin.Round;g.DrawPath(outline,path);using(var brush=new SolidBrush(color))g.FillPath(brush,path);
            }
        }
        static void BenchText(Graphics g,string text,Font font,Color color,RectangleF area){
            using(var format=new StringFormat(StringFormat.GenericTypographic)){format.Alignment=StringAlignment.Center;format.LineAlignment=StringAlignment.Center;format.FormatFlags|=StringFormatFlags.NoWrap;
                using(var brush=new SolidBrush(color))g.DrawString(text,font,brush,area,format);
            }
        }
        public static float BenchOutsideFontSize(Graphics g,int width,float limit,string rank,string rate){
            float size=Math.Max(12,limit);
            while(size>12){using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel)){
                if(Math.Max(BenchTextWidth(g,rank,font),BenchTextWidth(g,rate,font))<=width-4)break;
            }size=Math.Max(12,size-.25f);}return size;
        }
        static object benchRows;static string worstBenchRank="#104",worstBenchRate="49.9%";
        public static void ConfigureBenchMetrics(IEnumerable<Row> rows){
            if(ReferenceEquals(benchRows,rows))return;benchRows=rows;
            using(var bitmap=new Bitmap(1,1))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",8,FontStyle.Bold,GraphicsUnit.Pixel)){
                var actual=rows.Where(r=>r.Rank>0&&!double.IsNaN(r.Lower)).ToArray();
                worstBenchRank=actual.Select(r=>"#"+r.Rank).OrderByDescending(t=>BenchTextWidth(g,t,font)).FirstOrDefault()??"#173";
                worstBenchRate=actual.Select(r=>Statistics.Pct(r.Lower)).OrderByDescending(t=>BenchTextWidth(g,t,font)).FirstOrDefault()??"53.2%";
            }
        }
        public static string BenchRateText(Graphics g,Rectangle bounds,string rank,string rate){return rate??"";}
        public static bool BenchKeepsHash(Graphics g,Rectangle bounds){
            float scale=BenchScale(bounds),width=(float)Math.Round(1280*scale*SelectionMatcher.BenchWidths.Min())-2-2*BenchPadding(bounds);
            using(var font=new Font("Microsoft YaHei UI",8*scale,FontStyle.Bold,GraphicsUnit.Pixel))return BenchTextWidth(g,worstBenchRank,font)+BenchTextWidth(g,worstBenchRate,font)+BenchGap(bounds)<=width;
        }
        public static string BenchRankText(Graphics g,Rectangle bounds,int rank){return (BenchKeepsHash(g,bounds)?"#":"")+rank;}
        public static float BenchFontSize(Graphics g,Rectangle bounds,string rank,string rate){
            float scale=BenchScale(bounds),minimum=8*scale,available=(float)Math.Round(1280*scale*SelectionMatcher.BenchWidths.Min())-2-2*BenchPadding(bounds);
            string worstRank=BenchKeepsHash(g,bounds)?worstBenchRank:worstBenchRank.TrimStart('#');float size=10*scale;
            while(true){using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel)){
                if(font.GetHeight(g)<=bounds.Height-2&&BenchTextWidth(g,worstRank,font)+BenchTextWidth(g,worstBenchRate,font)+BenchGap(bounds)<=available)return size;
            }if(size<=minimum)return minimum;size=Math.Max(minimum,size-.25f);}
        }
        static void Compact(Graphics g,Rectangle bounds,CardResult result){
            if(result.BenchSlot){
                string rank=result.Row==null?"":BenchRankText(g,bounds,result.Row.Rank),rate=result.Row==null?"暂无数据":Statistics.Pct(result.Row.Lower);
                rate=BenchRateText(g,bounds,rank,rate);
                float benchSize=BenchFontSize(g,bounds,rank,rate);
                using(var font=new Font("Microsoft YaHei UI",benchSize,FontStyle.Bold,GraphicsUnit.Pixel)){
                    var area=new RectangleF(bounds.X+BenchPadding(bounds),bounds.Y+1,bounds.Width-2*BenchPadding(bounds),bounds.Height-2);
                    if(result.Row==null)BenchOutlinedText(g,rate,font,DesktopTheme.Muted,area,StringAlignment.Center);
                    else{
                        float rankWidth=BenchTextWidth(g,rank,font),rateWidth=BenchTextWidth(g,rate,font);
                        BenchOutlinedText(g,rank,font,Gold,new RectangleF(area.Left,area.Y,rankWidth,area.Height),StringAlignment.Near);
                        BenchOutlinedText(g,rate,font,RateColor(result.Row.Lower),new RectangleF(area.Right-rateWidth,area.Y,rateWidth,area.Height),StringAlignment.Far);
                    }
                }return;
            }
            const int rateHeight=17;
            float size=Math.Max(10,Math.Min(12,bounds.Height/3.2f));
            RectangleF rateArea=new RectangleF(bounds.X,bounds.Y,bounds.Width,rateHeight);
            using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel)){
                if(result.PortraitArc){
                    using(var brush=new SolidBrush(DesktopTheme.Surface))g.FillRectangle(brush,bounds);
                    Line(g,bounds.Left,bounds.Top,bounds.Right-1,bounds.Top);
                    Line(g,bounds.Left,bounds.Top,bounds.Left,bounds.Bottom);
                    Line(g,bounds.Right-1,bounds.Top,bounds.Right-1,bounds.Bottom);
                    float rowHeight=(bounds.Height-2)/2f,middle=bounds.Y+1+rowHeight;
                    float portraitSize=Math.Min(13,rowHeight-3);
                    using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}){
                        while(portraitSize>8){using(var number=new Font("Microsoft YaHei UI",portraitSize,FontStyle.Bold,GraphicsUnit.Pixel))if(g.MeasureString("#888",number,int.MaxValue,format).Width<=bounds.Width-4&&g.MeasureString("88.8%",number,int.MaxValue,format).Width<=bounds.Width-4)break;portraitSize=Math.Max(8,portraitSize-.5f);}
                        using(var number=new Font("Microsoft YaHei UI",portraitSize,FontStyle.Bold,GraphicsUnit.Pixel)){
                            Text(g,result.Row==null?"暂无":"#"+result.Row.Rank,number,result.Row==null?DesktopTheme.Muted:Gold,new RectangleF(bounds.X+2,bounds.Y+1,bounds.Width-4,rowHeight));
                            Text(g,result.Row==null?"数据":Statistics.Pct(result.Row.Lower),number,result.Row==null?DesktopTheme.Muted:RateColor(result.Row.Lower),new RectangleF(bounds.X+2,middle,bounds.Width-4,rowHeight));
                        }
                    }
                }else{
                    Panel(g,bounds,rateHeight);
                    Text(g,result.Row==null?"—":"#"+result.Row.Rank,font,Gold,new RectangleF(bounds.X,bounds.Y+rateHeight,bounds.Width,bounds.Height-rateHeight));
                    Text(g,result.Row==null?"暂无数据":Statistics.Pct(result.Row.Lower),font,RateColor(result.Row==null?double.NaN:result.Row.Lower),rateArea);
                }
            }
        }
        public static float CombinationFontSize(int height){return height<ComboStackLayout.RowHeight?13f*height/ComboStackLayout.RowHeight:Math.Max(13f,height*.62f);}
        public static void Draw(Graphics g,Rectangle bounds,CardResult result){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            if(result.Compact){if(result.PortraitArc)ArcBadge(g,bounds,result);else Compact(g,bounds,result);return;}
            if(result.CombinationOnly){
                var state=g.Save();g.TranslateTransform(bounds.X,bounds.Y);
                var block=new Rectangle(0,0,bounds.Width,bounds.Height);
                try{
                    Panel(g,block);
                    using(var font=new Font("Microsoft YaHei UI",CombinationFontSize(bounds.Height),FontStyle.Regular,GraphicsUnit.Pixel)){
                        if(result.HiddenCombinations>0)Text(g,"…还有 "+result.HiddenCombinations+" 条",font,Gold,new RectangleF(6,0,block.Width-12,block.Height),StringAlignment.Near);
                        else if(result.Combination!=null){
                            var row=result.Combination;
                            string rate="胜率 "+Statistics.Pct(row.Lower),gain="提升 "+Statistics.GainText(row.GainLower),prefix="联动 · ";
                            // Reserve the full numeric strings first; only the partner name may ellipsize.
                            using(var format=new StringFormat(StringFormat.GenericTypographic)){
                                float rateWidth=g.MeasureString(rate,font,int.MaxValue,format).Width+2;
                                float gainWidth=g.MeasureString(gain,font,int.MaxValue,format).Width+2;
                                float prefixWidth=g.MeasureString(prefix,font,int.MaxValue,format).Width+2;
                                float gainX=block.Width-6-gainWidth,rateX=gainX-6-rateWidth;
                                float nameX=6+prefixWidth,nameWidth=Math.Max(0,rateX-6-nameX);
                                format.LineAlignment=StringAlignment.Center;format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.NoClip;
                                string partner=result.CombinationText??"";
                                if(g.MeasureString(partner,font,int.MaxValue,format).Width>nameWidth){
                                    while(partner.Length>0&&g.MeasureString(partner+"…",font,int.MaxValue,format).Width>nameWidth)partner=partner.Substring(0,partner.Length-1);
                                    partner=g.MeasureString("…",font,int.MaxValue,format).Width<=nameWidth?partner+"…":"";
                                }
                                using(var brush=new SolidBrush(Gold)){
                                    g.DrawString(prefix,font,brush,new RectangleF(6,0,prefixWidth,block.Height),format);
                                    if(partner.Length>0)g.DrawString(partner,font,brush,new RectangleF(nameX,0,nameWidth,block.Height),format);
                                }
                                using(var brush=new SolidBrush(RateColor(row.Lower)))g.DrawString(rate,font,brush,new RectangleF(rateX,0,rateWidth,block.Height),format);
                                using(var brush=new SolidBrush(RateColor(.5+row.GainLower)))g.DrawString(gain,font,brush,new RectangleF(gainX,0,gainWidth,block.Height),format);
                            }
                        }
                    }
                }finally{g.Restore(state);}return;
            }
            if(result.HudSkill){
                Frame(g,bounds);
                var rows=result.SkillRecommendations!=null&&result.SkillRecommendations.Count>0?result.SkillRecommendations:new List<Row>{result.Row};
                float column=bounds.Width/(float)rows.Count,divider=bounds.Top+bounds.Height*.49f;
                using(var title=new Font("Microsoft YaHei UI",Math.Min(18,column/10f),FontStyle.Bold,GraphicsUnit.Pixel))
                using(var label=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel)){
                    for(int i=0;i<rows.Count;i++){
                        var row=rows[i];float x=bounds.X+column*i;
                        if(i>0)Line(g,x,bounds.Y+8,x,bounds.Bottom-8);
                        Text(g,row.Name.Replace(">"," > "),title,i==0?Gold:DesktopTheme.Text,new RectangleF(x+6,bounds.Y+5,column-12,25));
                        string sub=SkillContext(result.Context,row.Pick);float subWidth=g.MeasureString(sub,label).Width;if(subWidth<=column-12)Text(g,sub,label,DesktopTheme.Muted,new RectangleF(x+6,bounds.Y+29,column-12,17));else using(var small=new Font("Microsoft YaHei UI",Math.Max(8,11*(column-12)/subWidth),FontStyle.Regular,GraphicsUnit.Pixel))Text(g,sub,small,DesktopTheme.Muted,new RectangleF(x+6,bounds.Y+29,column-12,17));
                        Line(g,x+10,divider,x+column-10,divider);
                        float metricY=divider+Math.Max(2,(bounds.Bottom-divider-40)/2);
                        Metric(g,"统计学胜率",Statistics.Pct(row.Lower),RateColor(row.Lower),new RectangleF(x+2,metricY,column/2-4,37));
                        Metric(g,"胜率提升",Statistics.GainText(row.GainLower),RateColor(double.IsNaN(row.GainLower)?double.NaN:.5+row.GainLower),new RectangleF(x+column/2+2,metricY,column/2-4,37));
                    }
                }return;
            }
            if(result.Recommendations!=null){
                Panel(g,bounds);
                var skillRows=result.SkillRecommendations;
                if(skillRows==null||skillRows.Count==0){RecommendationSection(g,bounds,"召唤师技能",result.Context,result.Recommendations);return;}
                const int gap=8;
                int available=Math.Max(2,bounds.Width-gap);
                int summonerWidth=(int)Math.Round(available*(double)result.Recommendations.Count/(result.Recommendations.Count+skillRows.Count));
                var summonerBounds=new Rectangle(bounds.X,bounds.Y,summonerWidth,bounds.Height);
                var skillBounds=new Rectangle(bounds.X+summonerWidth+gap,bounds.Y,available-summonerWidth,bounds.Height);
                RecommendationSection(g,summonerBounds,"召唤师技能",result.Context,result.Recommendations);
                RecommendationSection(g,skillBounds,"技能加点",result.Context,skillRows);
                int divider=bounds.X+summonerWidth+gap/2;
                Line(g,divider,bounds.Y+8,divider,bounds.Bottom-8);
                return;
            }
            if(result.PlainChoice){
                Panel(g,bounds,31);
                using(var title=new Font("Microsoft YaHei UI",15,FontStyle.Bold,GraphicsUnit.Pixel))
                using(var context=new Font("Microsoft YaHei UI",10,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var label=new Font("Microsoft YaHei UI",10,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var value=new Font("Microsoft YaHei UI",13,FontStyle.Bold,GraphicsUnit.Pixel)){
                    Text(g,result.Card.Name,title,Gold,new RectangleF(bounds.X+9,bounds.Y+4,bounds.Width-93,23),StringAlignment.Near);
                    Text(g,(result.Context??"").Replace("对局",""),context,DesktopTheme.Muted,new RectangleF(bounds.Right-82,bounds.Y+5,73,21),StringAlignment.Far);
                    var row=result.Row;
                    string[] labels={"排名","统计学胜率"};
                    string[] values={row==null?"—":"#"+row.Rank,row==null?"暂无数据":Statistics.Pct(row.Lower)};
                    Color[] colors={Gold,RateColor(row==null?double.NaN:row.Lower)};
                    for(int i=0;i<labels.Length;i++){
                        float y=bounds.Y+33+i*29;
                        if(i>0)Line(g,bounds.X+9,y-2,bounds.Right-9,y-2);
                        Text(g,labels[i],label,DesktopTheme.Muted,new RectangleF(bounds.X+10,y,bounds.Width-90,25),StringAlignment.Near);
                        Text(g,values[i],value,colors[i],new RectangleF(bounds.Right-78,y,68,25),StringAlignment.Far);
                    }
                }return;
            }
            Panel(g,bounds,62);
            using(var title=new Font("Microsoft YaHei UI",17,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var label=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel)){
                Text(g,(result.Row==null?"暂无排名":"#"+result.Row.Rank)+"  "+result.Card.Name,title,Gold,new RectangleF(bounds.X+10,bounds.Y+8,bounds.Width-20,28));
                Text(g,result.Context,label,DesktopTheme.Muted,new RectangleF(bounds.X+10,bounds.Y+37,bounds.Width-20,19));
                bool note=!string.IsNullOrEmpty(result.SampleNote);float metricY=bounds.Y+(note?67:75);
                float half=bounds.Width/2f;
                Metric(g,"统计学胜率",result.Row==null?"数据不足":Statistics.Pct(result.Row.Lower),RateColor(result.Row==null?double.NaN:result.Row.Lower),new RectangleF(bounds.X+2,metricY,half-4,37));
                Metric(g,"统计学胜率提升",result.Row==null?"数据不足":Statistics.GainText(result.Row.GainLower),RateColor(result.Row==null||double.IsNaN(result.Row.GainLower)?double.NaN:.5+result.Row.GainLower),new RectangleF(bounds.X+half+2,metricY,half-4,37));
                if(note)Text(g,result.SampleNote,label,DesktopTheme.Muted,new RectangleF(bounds.X+8,bounds.Y+106,bounds.Width-16,16));
            }
        }
    }
}
