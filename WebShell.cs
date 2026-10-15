using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConfidenceDesktop {
    partial class MainForm {
        WebView2 web;
        bool webReady, webScheduled, classicActive;
        Task<CoreWebView2Environment> webEnvironment;
        Control classicLayout;
        string lastWebState;int webImageRevision;
        string webUpdatePhase="";bool webUpdateManual;int? webUpdateProgress;
        string webBoard="heroes";
        string webGainView="single";
        Task<IReadOnlyList<AugmentGainEntry>> watchedGainBoard;
        async void WatchGainBoard(Task<IReadOnlyList<AugmentGainEntry>> task){
            watchedGainBoard=task;
            try{await task;}catch(Exception){}
            if(!IsDisposed&&!Disposing&&watchedGainBoard==task)ScheduleWeb();
        }
        async void PrepareWebGainBoards(){
            var store=data;
            try{await store.PrepareGainBoardsAsync();}catch(Exception){}
            if(!IsDisposed&&!Disposing&&ReferenceEquals(data,store))ScheduleWeb();
        }
        internal string WebState(){
            bool home=SelectedHero==null||SelectedHero.Id==0;
            if(!home||webBoard!="gain")return CoreWebState();
            var state=Json.Obj(Json.Parse(CoreWebState()));
            bool combination=webGainView=="combination";
            var task=combination?data.CombinationGainBoardAsync():data.AugmentGainBoardAsync();if(watchedGainBoard!=task)WatchGainBoard(task);
            state["group"]=combination?"combination-gain-board":"augment-gain-board";
            state["columns"]=(combination?new[]{"名次","英雄","组合","统计学胜率提升","统计学胜率"}:new[]{"名次","英雄","海克斯","统计学胜率提升","统计学胜率"}).Select((text,index)=>new{text=text,index=index}).ToArray();
            state["empty"]=task.IsFaulted?"统计失败，请重新载入数据":task.IsCompleted?"暂无达标海克斯":"正在统计…";
            // Membership stays fixed; values, order and places follow the chosen level (advanced) or the observed gain (simple).
            state["rows"]=task.Status==TaskStatus.RanToCompletion?ArrangeRows(task.Result,entry=>StatsAt(entry.Win,entry.Count,entry.Pick,Statistics.WinBounds(entry.WinCounts,WinLevel),
                    Statistics.ComparativeBounds(entry.GainSelected,entry.GainPopulation,Statistics.GainAlpha(GainLevel))),true).Select(item=>{var entry=item.Item;return new{
                heroId=entry.HeroId,title=entry.HeroTitle,name=entry.HeroName,icon=entry.HeroIcon,augmentId=entry.AugmentId,augmentIds=entry.AugmentIds,rarity=entry.Rarity,
                cells=new[]{WebCell(Cell(item.Place)),WebCell(Cell(entry.HeroName)),WebCell(Cell(entry.AugmentName,"augments",bold:true,iconId:combination?null:entry.AugmentId)),GainCell(item.Stats),RateCell(item.Stats)},
                stats=item.Stats};}).ToArray():(object)new object[0];
            state["icons"]=WebIcons();
            return Json.Write(state);
        }

        // Hero pages open on the web overview (2.0.0); the classic view keeps its tab buttons and never shows it.
        const string OverviewSection="概览";
        object cachedOverview;List<Group> overviewGroups;int overviewRevision=-1;string overviewMode;
        bool EquipmentLoaded(){return groups.Any(g=>g.Path==RouteRecommendations.GroupPath||g.Path.StartsWith(EquipmentRecommendations.Prefix,StringComparison.Ordinal));}
        object WebOverview(){
            if(SelectedHero==null||SelectedHero.Id==0||section!=OverviewSection)return null;
            if(cachedOverview!=null&&ReferenceEquals(overviewGroups,groups)&&overviewRevision==webImageRevision&&overviewMode==StatisticsMode)return cachedOverview;
            Func<string,object[]> all=path=>{var g=groups.FirstOrDefault(x=>x.Path==path);return g==null?new object[0]:ArrangeRows(DataRows(g,false,false,false),r=>StatsAt(r.Source),false).Where(x=>x.Place!="—").Select(x=>{var r=x.Item;
                var entry=new Dictionary<string,object>{{"cells",new[]{WebCell(Cell(x.Place)),WebCell(r.Cells[1]),RateCell(x.Stats),GainCell(x.Stats)}},{"stats",x.Stats}};if(r.Stages!=null)entry["stages"]=r.Stages;return (object)entry;}).ToArray();};
            var plans=groups.FirstOrDefault(g=>g.Path==EquipmentRecommendations.Prefix+"plans");
            // Whole lists: each overview card scrolls in place.
            cachedOverview=new {augments=all("champions/augments/all"),combinations=all("champions/augmentCombinations"),summoners=all("champions/summoners"),skills=all("champions/skills"),
                plans=plans==null?new object[0]:EquipmentRows(plans,true,false).Select(r=>(object)new {cells=r.Cells.Take(2).Select(WebCell).Concat(new[]{BestLater(5,r.Source.Name),BestLater(6,r.Source.Name)}).ToArray()}).ToArray(),plansPending=!EquipmentLoaded()};
            overviewGroups=groups;overviewRevision=webImageRevision;overviewMode=StatisticsMode;return cachedOverview;
        }
        // Overview core build card: the plan's best fifth / sixth item (first S-grade row of that plan); empty when the core already fixes the slot.
        object BestLater(int slot,string plan){var group=groups.FirstOrDefault(g=>g.Path==EquipmentRecommendations.Prefix+"later/"+slot);var best=group==null?null:group.Rows.FirstOrDefault(r=>r.Name==plan&&r.ShoeAdvice=="S");
            return WebCell(best==null?Cell(""):Cell(best.StarterName,"items",tooltip:best.StarterName));}
        // The number-line gradient ends in the high end's own rate color (same scale as the shown values).
        void SaveStatisticsMode(){StatisticsSettings.Save(advancedMode);cachedWebRows=null;cachedOverview=null;ScheduleWeb();}
        // Theme swatch background: halfway between the bottom layer (bg) and the table (panel). bg alone makes every theme look black; panel blends into the menu.
        static string SwatchBackground(ThemePalette p){Color a=p["bg"],b=p["panel"];return "#"+((a.R+b.R)/2).ToString("x2")+((a.G+b.G)/2).ToString("x2")+((a.B+b.B)/2).ToString("x2");}
        static object RangeColor(double value){return double.IsNaN(value)?null:ColorTranslator.ToHtml(BadgePaint.RateColor(value));}
        static object WebNumber(double value){return double.IsNaN(value)||double.IsInfinity(value)?null:(object)value;}
        // Main-window statistics mode (2.0.0): advanced shows the statistical values with number lines, simple shows observed values only.
        // The levels are the fixed ones recommendations and overlays use, so every number matches the game (no user choice: the user decided
        // a level picker that leaves in-game recommendations unchanged would disappoint).
        bool advancedMode=true;
        const double WinLevel=.99,GainLevel=.99;
        string StatisticsMode{get{return advancedMode?"advanced":"simple";}}
        Dictionary<string,object> StatsAt(Row row){
            if(row==null||row.Raw==null)return null;
            return StatsAt(row.Win,row.Count,row.Pick,Statistics.WinBounds(row.Raw,WinLevel),row.GainBounds(Statistics.GainAlpha(GainLevel)));
        }
        static Dictionary<string,object> StatsAt(double win,double count,double pick,Statistics.Interval rate,Statistics.Interval gain){
            return new Dictionary<string,object>{{"win",WebNumber(win)},{"count",WebNumber(count)},{"pick",WebNumber(pick)},{"lower",WebNumber(rate.Lower)},{"upper",WebNumber(rate.Upper)},
                {"gain",WebNumber(gain.Lower)},{"gainUpper",WebNumber(gain.Upper)},{"gainPoint",WebNumber(gain.Point)},{"upperColor",RangeColor(rate.Upper)},{"gainUpperColor",RangeColor(.5+gain.Upper)}};
        }
        static double Stat(Dictionary<string,object> stats,string key){object value;return stats!=null&&stats.TryGetValue(key,out value)&&value is double?(double)value:double.NaN;}
        static double Ordered(double value){return double.IsNaN(value)?double.NegativeInfinity:value;}
        // The hero's place on the win-rate board in the current mode; falls back to the loaded rank before the board is cached.
        string HeroPlace(){
            var board=data.CachedBoardGroups(Scope);var rows=board==null?null:board.Where(g=>g.Path=="champions/rows").Select(g=>g.Rows).FirstOrDefault();
            if(rows==null||SelectedHero==null)return rankValue.Text.Trim();
            var place=ArrangeRows(rows,r=>StatsAt(r.Win,r.Count,r.Pick,Statistics.WinBounds(r.Raw,WinLevel),Statistics.Interval.Empty),false).FirstOrDefault(x=>(int)Json.Num(Json.Get(x.Item.Raw,"id"),0)==SelectedHero.Id);
            return place==null||place.Place=="—"?"#—":"#"+place.Place;
        }
        object RateCell(Dictionary<string,object> stats){double v=Stat(stats,"lower");return WebCell(Cell(Statistics.Pct(v),color:BadgePaint.RateColor(v)));}
        object GainCell(Dictionary<string,object> stats){double v=Stat(stats,"gain");return WebCell(Cell(Statistics.GainText(v),color:BadgePaint.RateColor(double.IsNaN(v)?double.NaN:.5+v)));}
        sealed class Placed<T>{internal T Item;internal Dictionary<string,object> Stats;internal string Place;}
        // Advanced: statistical win rate (one decimal) -> gain -> games, or gain first on the gain boards. Simple: observed win rate (or observed gain) -> games.
        List<Placed<T>> ArrangeRows<T>(IEnumerable<T> items,Func<T,Dictionary<string,object>> stats,bool gainFirst){
            var list=items.Select(item=>new Placed<T>{Item=item,Stats=stats(item)}).ToList();
            string key=advancedMode?(gainFirst?"gain":"lower"):(gainFirst?"gainPoint":"win");
            var ordered=advancedMode&&!gainFirst?Statistics.OrderDisplayed(list,x=>Stat(x.Stats,"lower"),x=>Stat(x.Stats,"gain"),x=>Stat(x.Stats,"count")).ToList()
                :list.OrderByDescending(x=>Ordered(Stat(x.Stats,key))).ThenByDescending(x=>Ordered(Stat(x.Stats,advancedMode?"lower":"count"))).ThenByDescending(x=>Ordered(Stat(x.Stats,"count"))).ToList();
            int place=0;foreach(var x in ordered)x.Place=double.IsNaN(Stat(x.Stats,key))?"—":(++place).ToString();
            return ordered;
        }
        readonly Dictionary<string,string> tooltipIconUris=new Dictionary<string,string>();
        HashSet<string> webIconAssets=new HashSet<string>();
        readonly HashSet<string> sentImages=new HashSet<string>();
        Task webSendQueue=Task.FromResult(0);int webPageGeneration;
        void ResetWebImages(){sentImages.Clear();webPageGeneration++;webSendQueue=Task.FromResult(0);lastWebState=null;}
        void RecordWebImages(IEnumerable<string> paths){foreach(string path in paths)sentImages.Add(path);}
        static void CollectWebImagePaths(object node,HashSet<string> paths){
            var map=Json.Obj(node);if(map!=null){foreach(var pair in map){if(pair.Key=="icon"||pair.Key=="heroIcon"){string path=Json.Str(pair.Value);if(WebsiteData.IsImageAsset("assets/"+path))paths.Add(path);}CollectWebImagePaths(pair.Value,paths);}}
            else foreach(var item in Json.Arr(node))CollectWebImagePaths(item,paths);
        }
        Dictionary<string,string> ReadWebImages(string message,string root,HashSet<string> alreadySent){
            var paths=new HashSet<string>();CollectWebImagePaths(Json.Parse(message),paths);var images=new Dictionary<string,string>();
            if(string.IsNullOrEmpty(root))return images;
            foreach(string path in paths.Where(p=>!alreadySent.Contains(p)))try{string file=WebsiteData.PathUnder(root,"assets/"+path);if(!File.Exists(file))continue;images[path]="data:image/"+(path.EndsWith(".png")?"png":"webp")+";base64,"+Convert.ToBase64String(File.ReadAllBytes(file));}catch(IOException){}catch(UnauthorizedAccessException){}
            return images;
        }
        async Task SendWebMessage(string message,Task previous,int generation){
            try{
                await previous;if(generation!=webPageGeneration||!webReady||web==null||web.IsDisposed)return;
                var parsed=Json.Parse(message);string type=Json.Str(Json.Get(parsed,"type"));
                if(type=="state"||type=="search"){
                    string root=data.Root;var known=new HashSet<string>(sentImages);
                    var images=await Task.Run(()=>ReadWebImages(message,root,known));
                    if(generation!=webPageGeneration||IsDisposed||!webReady||web==null||web.IsDisposed)return;
                    if(images.Count>0){web.CoreWebView2.PostWebMessageAsJson(Json.Write(new{type="images",images=images}));RecordWebImages(images.Keys);}
                }
                web.CoreWebView2.PostWebMessageAsJson(message);
            }catch(Exception){if(generation==webPageGeneration)lastWebState=null;}
        }
        void SendWebJson(string message){if(webReady&&web!=null&&!web.IsDisposed)webSendQueue=SendWebMessage(message,webSendQueue,webPageGeneration);}
        async void FillWebIcons(){await FillWebIconsAsync();}
        async Task FillWebIconsAsync(){var snapshot=data;if(string.IsNullOrEmpty(snapshot.Root))return;try{var downloaded=await Task.Run(()=>{var tooltip=WebsiteData.FillTooltipIcons(snapshot);var images=WebsiteData.FillImageIcons(snapshot);return tooltip.Downloaded+images.Downloaded;});if(IsDisposed||Disposing||snapshot!=data||downloaded==0)return;webImageRevision++;cachedWebRows=null;lastWebState=null;ScheduleWeb();}catch{}}
        Dictionary<string,string> WebIcons(){var result=new Dictionary<string,string>();foreach(string asset in webIconAssets){string uri;if(!tooltipIconUris.TryGetValue(asset,out uri)){try{string path=WebsiteData.PathUnder(data.Root,asset);if(!File.Exists(path))continue;uri="data:image/"+(asset.EndsWith(".png")?"png":"webp")+";base64,"+Convert.ToBase64String(File.ReadAllBytes(path));tooltipIconUris[asset]=uri;}catch{continue;}}result[asset]=uri;}return result;}
        static string ThemeCss(string html){return Regex.Replace(html,@":root\{[^}]*\}",":root{color-scheme:dark;"+string.Join(";",ThemeManager.Current.Css().Select(p=>"--"+p.Key+":"+p.Value))+";--teal:#00b69f}",RegexOptions.None);}
        void PrepareWeb(Control layout){
            classicLayout=layout;layout.Visible=false;BackColor=DesktopTheme.Canvas;
            if(StartupTrace.ForceClassic)return;
            webEnvironment=CreateWebEnvironment();
            Load+=(s,e)=>InitializeWeb(layout);
        }
        async Task<CoreWebView2Environment> CreateWebEnvironment(){return await CoreWebView2Environment.CreateAsync(null,StartupTrace.UserData??Path.Combine(AppVersion.LocalDataDirectory,"界面缓存"));}
        async void InitializeWeb(Control layout) {
            classicLayout=layout;
            web=new WebView2 {Dock=DockStyle.Fill,DefaultBackgroundColor=DesktopTheme.Canvas};
            Controls.Add(web);web.BringToFront();
            try {
                var environment=await webEnvironment;
                StartupTrace.Mark("webview-env-created");if(IsDisposed)return;
                await web.EnsureCoreWebView2Async(environment);
                StartupTrace.Mark("webview-core-ready");web.CoreWebView2.NavigationCompleted+=(s,e)=>StartupTrace.Mark("navigation-completed");
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
                web.CoreWebView2.Settings.AreDevToolsEnabled=false;
                web.CoreWebView2.Settings.IsStatusBarEnabled=false;
                web.CoreWebView2.Settings.IsZoomControlEnabled=false;
                web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled=false;
                // The embedded document is the only navigation allowed in this view.
                bool firstNavigation=true;
                web.CoreWebView2.NavigationStarting+=(s,e)=>{e.Cancel=!firstNavigation;firstNavigation=false;};
                web.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
                web.CoreWebView2.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
                web.CoreWebView2.WebMessageReceived+=WebMessage;
                web.CoreWebView2.ProcessFailed+=(s,e)=>RestoreClassic("网页界面暂时不可用，已恢复基础界面。");
                using(var stream=GetType().Assembly.GetManifestResourceStream("WebShell.html"))
                using(var reader=new StreamReader(stream))web.NavigateToString(ThemeCss(reader.ReadToEnd()));
            } catch(Exception) {RestoreClassic("网页界面未能启动，已恢复基础界面。请确认 Microsoft Edge WebView2 运行时已安装。");}
        }
        void RestoreClassic(string message) {
            if(IsDisposed)return;webReady=false;classicActive=true;
            if(web!=null){Controls.Remove(web);web.Dispose();web=null;}
            if(section==OverviewSection){section="海克斯";if(SelectedHero!=null&&SelectedHero.Id!=0)BuildFilters();}
            ShowRows();classicLayout.Visible=true;classicLayout.BringToFront();
            if(!StartupTrace.ForceClassic)MessageBox.Show(this,message,"界面提示",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        void SendWeb(object value) {SendWebJson(Json.Write(value));}
        void ScheduleWeb() {
            if(!webReady||webScheduled||IsDisposed||!IsHandleCreated)return;
            webScheduled=true;BeginInvoke((Action)(()=>{webScheduled=false;if(!IsDisposed&&webReady)PublishWeb();}));
        }
        object WebCell(DisplayCell cell) {
            string text=cell.Text,kind=cell.Kind??"";
   var parts=Regex.Split(text??"",@"( → | \+ |>|\n)").Where(p=>p.Length>0).Select(p=>{
                string name=Regex.Replace(p.Trim(),@"\s*×\s*\d+$","");
                var rich=kind.Length>0?EffectText.Rich(data,kind,name):null;
                foreach(var block in Json.Arr(Json.Get(Json.Get(EffectText.Find(data,kind,name),"tooltip"),"blocks")))foreach(var span in Json.Arr(Json.Get(block,"spans"))){string asset=Json.Str(Json.Get(span,"asset"));if(WebsiteData.IsTooltipAsset(asset))webIconAssets.Add(asset);}
                var part=new Dictionary<string,object>{{"text",p},{"tip",kind.Length>0?EffectText.Single(data,kind,name):""},{"rich",rich}};string icon=WebsiteData.ImageIcon(data,kind,name,cell.IconId);if(icon!=null&&!Regex.IsMatch(p,@"\A[ →+>\s]+\z"))part["icon"]=icon;return part;
            }).ToArray();
            Color color=cell.Color;
            return new {text=text,parts=parts,color=color.IsEmpty?null:ColorTranslator.ToHtml(color),bold=cell.Bold};
        }
        string CoreWebState() {
            var group=categories.SelectedItem as Group;bool home=SelectedHero==null||SelectedHero.Id==0;
            var columns=grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).ToArray();
            bool statistical=group!=null&&!group.Path.StartsWith(EquipmentRecommendations.Prefix,StringComparison.Ordinal)&&group.Path!=RouteRecommendations.GroupPath;
            var heroStats=home?null:groups.Where(g=>g.Path=="champions/champion/stats"&&g.Rows.Count==1).Select(g=>g.Rows[0]).FirstOrDefault();
            double? heroRate=heroStats==null?(double?)null:Statistics.WinBounds(heroStats.Raw,WinLevel).Lower;
            if(cachedWebRows==null){webIconAssets.Clear();cachedOverview=null;bool ranked=columns.Length>0&&columns[0].Index==0&&columns[0].HeaderText=="排名";
                var shownRows=!home&&section==OverviewSection?new List<DisplayRow>():displayedRows;
                var placed=statistical?ArrangeRows(shownRows,r=>StatsAt(r.Source),false):shownRows.Select(r=>new Placed<DisplayRow>{Item=r}).ToList();
                cachedWebRows=placed.Select(x=>{var r=x.Item;var entry=new Dictionary<string,object>{{"heroId",r.HeroId},{"icon",WebsiteData.ImageIcon(data,"champions",id:r.HeroId.ToString())},
                    {"cells",columns.Select(c=>!statistical?WebCell(r.Cells[c.Index]):c.Index==0&&ranked?WebCell(Cell(x.Place)):c.Index==2?RateCell(x.Stats):c.Index==3?GainCell(x.Stats):WebCell(r.Cells[c.Index])).ToArray()}};
                    if(r.HeroId>0){entry["title"]=HeroNames.Title(r.Source.Name);entry["name"]=HeroNames.Name(r.Source.Name);}if(r.Stages!=null){entry["stages"]=r.Stages;entry["overall"]=r.Source!=null&&r.Source.UsesOverallAugment;}if(x.Stats!=null)entry["stats"]=x.Stats;return entry;}).ToArray();}
            return Json.Write(new {
                type="state",board=webBoard,gainView=webGainView,heroIcon=home?null:WebsiteData.ImageIcon(data,"champions",id:SelectedHero.Id.ToString()),home=home,hero=home?"英雄总榜":ShortName(SelectedHero.Name),title=home?"":HeroNames.Title(SelectedHero.Name),name=home?"":HeroNames.Name(SelectedHero.Name),heroId=home?0:SelectedHero.Id,
                section=section,group=group==null?"":group.Path,overview=WebOverview(),
                rank=home?"":HeroPlace(),rate=heroRate==null?"":Statistics.Pct(heroRate.Value),heroWin=heroStats==null?null:WebNumber(heroStats.Win),heroCount=heroStats==null?null:WebNumber(heroStats.Count),rateColor=ColorTranslator.ToHtml(BadgePaint.RateColor(heroRate??double.NaN)),
                advanced=advancedMode,
                tabs=tabs.Controls.OfType<NavigationButton>().Select(b=>b.Text).ToArray(),
                filters=filters.Controls.OfType<NavigationButton>().Select(b=>new {text=b.Text,path=(string)b.Tag,enabled=b.Enabled}).ToArray(),
                flows=laterFlow.Items.Cast<object>().Select(Convert.ToString).ToArray(),flow=laterFlow.SelectedIndex,
                flowIcons=laterFlow.Items.Cast<object>().Select(item=>Convert.ToString(item).Split(new[]{" + "},StringSplitOptions.None).Select(name=>WebsiteData.ImageIcon(data,"augments",name.Trim())).Where(icon=>icon!=null).Select(icon=>new {icon=icon}).ToArray()).ToArray(),
                rarity=rarity.SelectedIndex,query=augmentSearch.Text,
                columns=columns.Select(c=>new {text=group!=null&&c.Index==2&&group.RateTitle!=null?group.RateTitle:group!=null&&c.Index==3&&group.GainTitle!=null?group.GainTitle:c.HeaderText,index=c.Index}).ToArray(),
                rows=cachedWebRows,icons=WebIcons(),imageRevision=webImageRevision,
                empty=data.Heroes.Count==0?WebsiteUpdateStatus.EmptyText(webUpdatePhase):group!=null&&group.Path=="champions/augmentCombinations"?"暂无值得推荐的海克斯组合":emptyRoutes.Text,version=AppVersion.Display,gameVersion=data.Version,date=data.Date,
                connection=clientStatus==null?"未检测到客户端":ConnectionText(clientStatus,data),updating=updating,updateStatus=WebsiteUpdateStatus.Text(webUpdatePhase,webUpdateManual,data.Version,data.Date,webUpdateProgress),progress=webUpdateProgress,
                shopStatus=new {kind=shopSession.DisplayKind,count=shopSession.ImportedCount,error=shopSession.Error},autoMinimize=autoMinimize,theme=ThemeManager.Current.Id,palette=ThemeManager.Current.Css(),themes=ThemePalette.All.Select(p=>{var css=p.Css();return new {id=p.Id,name=p.Name,bg=SwatchBackground(p),accent=css["gold"]};}).ToArray(),skillMode=hudSkillMode.SelectedIndex,closeToTray=minimizeToTray,status=status.Text
            });
        }
        void PublishWeb() {
            if(!startupApplied)return;
            string state=WebState();if(state==lastWebState)return;lastWebState=state;
            StartupTrace.State(state);SendWebJson(state);
        }
        void SetWebUpdate(string phase,int? progress=null){webUpdatePhase=phase;webUpdateProgress=phase=="syncing"?progress:null;SendWeb(new {type="updateStatus",busy=updating,empty=data.Heroes.Count==0?WebsiteUpdateStatus.EmptyText(phase):null,text=WebsiteUpdateStatus.Text(phase,webUpdateManual,data.Version,data.Date,webUpdateProgress),progress=webUpdateProgress});ScheduleWeb();}
        async Task UpdateWebWebsite(bool manual){
            if(updating){if(manual){webUpdateManual=true;if(webUpdatePhase!="syncing")SetWebUpdate(webUpdatePhase,webUpdateProgress);}return;}
            updating=true;webUpdateManual=manual;checkDataButton.Enabled=false;SetWebUpdate("checking");bool syncing=false;
            try{
                var info=await Task.Run(()=>WebsiteData.Check(DataStore.DefaultRoot));if(IsDisposed||Disposing)return;
                if(!info.NeedsSync){SetWebUpdate("current");return;}
                syncing=true;SetWebUpdate("syncing");
                int pendingProgress=-1;
                using(var timer=new Timer{Interval=200}){
                    timer.Tick+=(s,e)=>{int value=System.Threading.Volatile.Read(ref pendingProgress);if(!IsDisposed&&!Disposing&&value>=0&&webUpdateProgress!=value)SetWebUpdate("syncing",value);};
                    timer.Start();
                    try{await Task.Run(()=>WebsiteData.Update(DataStore.DefaultRoot,message=>{},fileProgress:(finished,total)=>System.Threading.Volatile.Write(ref pendingProgress,WebsiteUpdateStatus.Percent(finished,total))));}
                    finally{timer.Stop();}
                }
                if(IsDisposed||Disposing)return;SetWebUpdate("loading");
                await ReloadAsync(true);PrepareWebGainBoards();if(IsDisposed||Disposing)return;await FillWebIconsAsync();if(IsDisposed||Disposing)return;source.Text="程序版本："+AppVersion.Display+"　游戏版本："+data.Version+"　大数据更新日期："+data.Date;SetWebUpdate("updated");
            }catch(Exception){if(!IsDisposed&&!Disposing)SetWebUpdate(syncing?"syncFailed":"checkFailed");}
            finally{updating=false;if(!IsDisposed&&!Disposing){checkDataButton.Enabled=true;SetWebUpdate(webUpdatePhase,webUpdateProgress);if(overlay!=null)overlay.Enabled=data.Heroes.Count>0;}}
        }
        async void WebMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
            if(e.Source!="about:blank")return;
            try {
                var message=Json.Parse(e.WebMessageAsJson);string action=Json.Str(Json.Get(message,"action"));
                string value=Json.Str(Json.Get(message,"value"));int number;
                switch(action) {
                    case "rendered":if(StartupTrace.Render()){var close=new Timer{Interval=1500};close.Tick+=(s,a)=>{close.Stop();close.Dispose();exitRequested=true;Close();};close.Start();}break;
                    case "ready":ResetWebImages();StartupTrace.Mark("web-ready-message");webReady=true;classicLayout.Visible=false;grid.Rows.Clear();PublishWeb();break;
                    case "board":webBoard=value=="gain"?"gain":"heroes";heroes.SelectedItem=data.Heroes.FirstOrDefault(h=>h.Id==0);break;
                    case "gainView":webGainView=value=="combination"?"combination":"single";break;
                    case "hero":if(int.TryParse(value,out number)){var hero=data.Heroes.FirstOrDefault(h=>h.Id==number);if(hero!=null){if(number==0){webBoard="heroes";}heroes.SelectedItem=hero;}}break;
                    case "search":SendWeb(new {type="search",query=value,heroes=HeroAliases.Search(data.Heroes,value).Where(h=>h.Id>0).Take(12).Select(h=>new {id=h.Id,title=HeroNames.Title(h.Name),name=HeroNames.Name(h.Name),full=h.Name,icon=WebsiteData.ImageIcon(data,"champions",id:h.Id.ToString())}).ToArray()});break;
                    case "section":if(value==OverviewSection||tabs.Controls.OfType<NavigationButton>().Any(b=>b.Text==value)){section=value;if(section=="装备")LoadGroups(true);else{BuildFilters();if(section==OverviewSection&&!EquipmentLoaded())LoadGroups(true);}}break;
                    case "group":if(filters.Controls.OfType<NavigationButton>().Any(b=>(string)b.Tag==value&&b.Enabled))SelectGroup(value);break;
                    case "rarity":if(int.TryParse(value,out number)&&number>=0&&number<4)rarity.SelectedIndex=number;break;
                    case "augmentSearch":augmentSearch.Text=value;break;
                    case "flow":if(int.TryParse(value,out number)&&number>=0&&number<laterFlow.Items.Count)laterFlow.SelectedIndex=number;break;
                    case "theme":SelectTheme(value);break;
                    case "advanced":advancedMode=value=="1";SaveStatisticsMode();break;
                    case "skillMode":if(int.TryParse(value,out number)&&number>=0&&number<3)hudSkillMode.SelectedIndex=number;break;
                    case "closeMode":minimizeToTray=value=="1";CloseBehaviorSettings.Save(minimizeToTray);break;
                    
                    case "autoMinimize":autoMinimize=value=="1";AutoMinimizeSettings.Save(autoMinimize);break;
                    case "gameDirectory":string setup=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"设置游戏目录.exe");if(!File.Exists(setup))throw new FileNotFoundException("未找到设置游戏目录.exe，请重新安装程序。",setup);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup){UseShellExecute=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory});break;
                    case "check":await UpdateWebWebsite(true);break;
                }
                ScheduleWeb();
            }catch(Exception error){SendWeb(new {type="error",message=error.GetBaseException().Message});}
        }
    }
    public static class WebsiteUpdateStatus {
        public static string EmptyText(string phase){return phase=="checkFailed"||phase=="syncFailed"?"数据下载失败，请点右下角「检查数据更新」重试":"首次使用，正在下载数据…";}
        public static int Percent(int finished,int total){return total<=0?0:(int)Math.Max(0,Math.Min(99,(long)finished*100/total));}
        public static string Text(string phase,bool manual,string version="",string date="",int? progress=null){
            switch(phase){
                case "checking":return manual?"· 检查中…":"";
                case "syncing":return progress.HasValue?"· 正在同步最新数据 "+progress.Value+"%":"· 正在同步最新数据…";
                case "loading":return "· 正在载入新数据…";
                case "current":return manual?"· 已是最新":"";
                case "updated":var match=Regex.Match(date??"",@"^\d{4}-(\d{2}-\d{2})");return "· 已更新到 "+version+"（"+(match.Success?match.Groups[1].Value:date)+"）";
                case "checkFailed":return manual?"· 检查失败，请稍后重试":"";
                case "syncFailed":return "· 同步失败，请稍后重试";
                default:return "";
            }
        }
    }

}
