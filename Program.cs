using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using Timer=System.Windows.Forms.Timer;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConfidenceDesktop {
    static class Program {
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        [STAThread] static void Main(string[] args) {
            if(args.Length>0&&args[0]=="--build-gain-board"){
                try{
                    if(args.Length!=3)throw new ArgumentException("Usage: --build-gain-board <website-data-directory> <output-file>");
                    string root=Path.GetFullPath(args[1]);
                    if(!File.Exists(Path.Combine(root,"website-state.json")))throw new InvalidDataException("Website data is missing; sync data before building gain boards.");
                    var data=new DataStore();data.Load(root);
                    if(!data.Heroes.Any(h=>h.Id>0))throw new InvalidDataException("Website data has no heroes.");
                    var boards=data.CalculateGainBoards();GainBoardCache.WriteFile(args[2],data.GainBoardKey,boards);
                    Console.WriteLine("PASS gain boards rows="+boards.Single.Count+"/"+boards.Combination.Count+" key="+data.GainBoardKey+" output="+Path.GetFullPath(args[2]));
                }catch(Exception error){Environment.ExitCode=1;Console.Error.WriteLine("Gain board build failed: "+error.GetBaseException().Message);}
                return;
            }
            if(args.Length>0&&args[0]=="--sync-data") {
                string log=null;
                try {
                    if(args.Length!=2)throw new ArgumentException("Usage: --sync-data <root>");
                    string root=Path.GetFullPath(args[1]);log=Path.Combine(Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)),"sync-data.log");
                    string result=WebsiteData.Update(root,message=>{});
                    WebsiteData.PruneSnapshots(root);
                    var images=new DataStore();images.Load(root);var icons=WebsiteData.FillImageIcons(images);var tips=WebsiteData.FillTooltipIcons(images);foreach(var failure in tips.Failures)icons.Failures[failure.Key]=failure.Value;icons.Downloaded+=tips.Downloaded;icons.Bytes+=tips.Bytes;
                    result+="\r\n图像下载："+icons.Downloaded+" · 字节："+icons.Bytes+" · 失败："+icons.Failures.Count+"\r\n"+Json.Write(icons.Failures);if(icons.Failures.Count>0)Environment.ExitCode=1;
                    File.WriteAllText(log,result,Encoding.UTF8);
                }catch(Exception error){Environment.ExitCode=1;try{if(log!=null)File.WriteAllText(log,error.ToString(),Encoding.UTF8);else Console.Error.WriteLine(error);}catch{Console.Error.WriteLine(error);}}
                return;
            }
            if(args.Length>=2&&args[0]=="--startup-trace") {
                StartupTrace.Start(args[1],args.Length>2&&args[2]!="--force-classic"?args[2]:null,Array.IndexOf(args,"--force-classic")>=0);
                NativeOverlay.Dpi();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var form=new MainForm()){StartupTrace.Mark("form-ctor-done");Application.Run(form);}return;
            }
            NativeOverlay.Dpi();
            if(args.Length==0||(args.Length==1&&args[0]=="--start-minimized")){RunSingleInstance(args.Length>0);return;}
            if(args.Length==2&&args[0]=="--preview"){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new ReplayForm(args[1]));return;}
            if(args.Length>0) {
                string result;
                try {
                    var data=new DataStore();data.Load(DataStore.DefaultRoot);
                    if(args[0]=="--selection-test") {result=OverlayTests.RunSelection(args[1],args[2]).GetAwaiter().GetResult();}
                    else if(args[0]=="--hero-check") {int id=InGameHero.Read(data);result=id>0?"当前英雄："+data.Lookup("champions",id)+" | id="+id:"当前游戏接口未返回可识别英雄";}
                    else if(args[0]=="--ocr-test") {result=OverlayTests.Run(args[1],args[2]).GetAwaiter().GetResult();}
                    else if(args[0]=="--check") {
                        if(Math.Abs(Statistics.Wilson(.5,100)-.386715)<.00001==false)throw new Exception("Wilson reference failed");
                        if(Statistics.Wilson(0,10)!=0||!double.IsNaN(Statistics.Wilson(.5,0)))throw new Exception("Wilson boundaries failed");
                        var groups=data.Groups(122,"high");var board=data.Groups(0,"high");
                        foreach(var g in groups.Concat(board))for(int i=1;i<g.Rows.Count;i++)if(g.Rows[i].Lower>g.Rows[i-1].Lower)throw new Exception("Rank order failed");
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"示例导出-诺克萨斯之手.txt"),data.Report(groups,"high",data.Lookup("champions",122)),new UTF8Encoding(true));
                        result="PASS: Wilson boundaries/reference, real cache parsing, sorted groups="+groups.Count+", champion rows="+board.Sum(g=>g.Rows.Count)+"\r\n"+new LocalClient().Poll().Message;
                    } else throw new ArgumentException("Unknown command");
                    if(args.Length>2)File.WriteAllText(args[2],result,Encoding.UTF8);
                }catch(Exception e){if(args.Length>2)File.WriteAllText(args[2],"FAIL: "+e.Message,Encoding.UTF8);Environment.ExitCode=1;}
                return;
            }
        }
        static void RunSingleInstance(bool startMinimized){
            using(var activate=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\ARAMate-Activate")){
                bool createdNew;
                using(var instance=new Mutex(false,@"Local\ARAMate-SingleInstance",out createdNew)){
                    if(!createdNew){if(!startMinimized){AllowSetForegroundWindow(-1);activate.Set();}return;}
                    AppVersion.MigrateLocalData();
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var stop=new ManualResetEvent(false))using(var form=new MainForm{WindowState=startMinimized?FormWindowState.Minimized:FormWindowState.Normal}){
                        var listener=new Thread(()=>{while(WaitHandle.WaitAny(new WaitHandle[]{stop,activate})==1){try{form.BeginInvoke((Action)(()=>{if(!form.IsDisposed)form.ActivateFromOtherInstance();}));}catch(ObjectDisposedException){}catch(InvalidOperationException){}}}){IsBackground=true};
                        form.Load+=(sender,args)=>listener.Start();
                        try{Application.Run(form);}finally{stop.Set();if(listener.IsAlive)listener.Join();}
                    }
                }
            }
        }
    }
    static class StartupTrace {
        static readonly object gate=new object();static string path;static System.Diagnostics.Stopwatch watch;
        internal static string UserData;internal static bool ForceClassic;static bool stateWritten,rendered;
        internal static bool Enabled {get{return path!=null;}}
        internal static int TimeoutDelay {get{return (int)Math.Max(1,15000-watch.ElapsedMilliseconds);}}
        internal static bool HasRendered {get{return rendered;}}
        internal static void Start(string output,string userData,bool force){path=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(path));UserData=userData;ForceClassic=force;watch=System.Diagnostics.Stopwatch.StartNew();File.WriteAllText(path,"# milliseconds relative to Main entry Stopwatch\r\n");Mark("main");}
        internal static void Mark(string name){if(!Enabled)return;lock(gate)File.AppendAllText(path,watch.ElapsedMilliseconds+" "+name+"\r\n");}
        internal static void State(string json){if(!Enabled||stateWritten)return;stateWritten=true;File.WriteAllText(path+".state.json",json,Encoding.UTF8);Mark("first-state-posted");}
        internal static bool Render(){if(!Enabled||rendered)return false;rendered=true;Mark("first-render");return true;}
    }
    public static class CloseBehaviorSettings {
        public static string PathName {get{return Path.Combine(AppVersion.LocalDataDirectory,"窗口设置.json");}}
        public static bool Load(string path=null){try{return object.Equals(Json.Get(Json.Read(path??PathName),"minimizeToTray"),true);}catch{return false;}}
        public static void Save(bool value,string path=null){path=path??PathName;Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{var config=File.Exists(path)?Json.Read(path) as Dictionary<string,object>:null;config=config??new Dictionary<string,object>();config["minimizeToTray"]=value;File.WriteAllText(temp,Json.Write(config),Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
    class SmoothGrid:DataGridView {public SmoothGrid(){DoubleBuffered=true;}}
    class NavigationButton:ModernButton {public bool Selected,Primary;}
    partial class MainForm:Form {
        bool minimizeToTray=CloseBehaviorSettings.Load(),exitRequested;
        bool autoMinimize=AutoMinimizeSettings.Load(),changingGameWindow;AutoMinimizeState gameWindow=new AutoMinimizeState();
        NotifyIcon trayIcon;FormWindowState restoreState=FormWindowState.Normal;
        void InitializeTray(){var menu=new ContextMenuStrip();menu.Items.Add("打开",null,(sender,args)=>RestoreFromTray());menu.Items.Add("退出",null,(sender,args)=>{exitRequested=true;Close();});trayIcon=new NotifyIcon{Icon=Icon,Text=AppVersion.ProductName,ContextMenuStrip=menu,Visible=true};trayIcon.DoubleClick+=(sender,args)=>RestoreFromTray();}
        void RestoreFromTray(){Show();if(WindowState==FormWindowState.Minimized)WindowState=restoreState==FormWindowState.Minimized?FormWindowState.Normal:restoreState;Activate();}
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        internal void ActivateFromOtherInstance(){if(!Visible)RestoreFromTray();if(WindowState==FormWindowState.Minimized)WindowState=restoreState==FormWindowState.Minimized?FormWindowState.Normal:restoreState;Activate();SetForegroundWindow(Handle);}
        protected override void OnFormClosing(FormClosingEventArgs e){
            if(e.CloseReason==CloseReason.UserClosing&&minimizeToTray&&!exitRequested){e.Cancel=true;if(WindowState!=FormWindowState.Minimized)restoreState=WindowState;if(settingsWindow!=null&&!settingsWindow.IsDisposed)settingsWindow.Close();if(dataUpdateWindow!=null&&!dataUpdateWindow.IsDisposed)dataUpdateWindow.Close();trayIcon.Visible=true;Hide();return;}base.OnFormClosing(e);
        }
        HudSkillOverlay hudSkillOverlay;
        ComboBox hudSkillMode=new ModernComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=220,AccessibleName="局内技能加点显示方式",Margin=new Padding(0,6,0,0)};
        ShopSession shopSession=new ShopSession();DataStore data=new DataStore();LocalClient client=new LocalClient();List<Group> groups=new List<Group>();
        Label highDataSource=new Label{Text="数据来自高分段对局（隐藏分前 25%）",AutoSize=true,Font=new Font("Microsoft YaHei UI",9)};
        ComboBox heroes=new ModernComboBox(),categories=new ModernComboBox(),rarity=new ModernComboBox();
        ComboBox laterFlow=new ModernComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=280,Visible=false};Label laterFlowLabel=new Label{Text="适用方案：",AutoSize=true,Visible=false,Margin=new Padding(0,9,4,0)};
        TextBox augmentSearch=new TextBox();Label augmentSearchLabel=new Label();
        ToolTip searchTip=new ToolTip(); TextBox search=new TextBox(); ListBox suggestions=new ListBox(); Button back=new ModernButton(); Label pageTitle=new Label();
        FlowLayoutPanel tabs=new FlowLayoutPanel(), filters=new FlowLayoutPanel(); Label summary=new Label(),summaryRate=new Label(),rankCaption=new Label(),rankValue=new Label(),winCaption=new Label(); string section="海克斯"; int viewedHero=-1;
        Label emptyRoutes=new Label{Text="当前没有符合条件的路线",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Visible=false};Label status=new Label(),source=new Label();DataGridView grid=new SmoothGrid();
        bool rebuilding; Timer timer=new Timer();bool polling,loading,updating;int lastChampion;
        Label overlayStatus=new Label();OverlayController overlay;
        SelectionOverlayController selectionOverlay;ClientStatus clientStatus;DateTime clientStatusTime=DateTime.MinValue;int activeGamePid;
        string Scope {get{return "high";}}
        bool MayhemActive(bool selection){return data.Heroes.Count>0&&clientStatus!=null&&clientStatus.IsMayhem&&DateTime.UtcNow-clientStatusTime<TimeSpan.FromSeconds(selection?8:20)&&(selection?clientStatus.Phase=="ChampSelect":clientStatus.Phase=="InProgress"||clientStatus.Phase=="GameStart");}
        Hero SelectedHero {get{return heroes.SelectedItem as Hero;}}
        readonly Task<DataStore> startupData;
        bool startupApplied;
        async Task ApplyStartupData(){
            if(startupData!=null){loading=true;try{var loaded=await startupData;if(IsDisposed)return;data=loaded;PrepareWebGainBoards();heroes.DataSource=data.Heroes;heroes.SelectedItem=data.Heroes.FirstOrDefault(h=>h.Id==0)??data.Heroes[0];}catch(Exception e){if(!IsDisposed)status.Text="尚无完整网站数据，请联网后重新打开程序。"+e.Message;}finally{loading=false;}if(IsDisposed)return;await LoadGroupsAsync();}
            startupApplied=true;FillWebIcons();if(webReady)PublishWeb();
            if(StartupTrace.ForceClassic){RestoreClassic("网页界面暂时不可用，已恢复基础界面。");StartupTrace.Mark("classic-visible-rows-"+grid.Rows.Count);}
        }
        public MainForm() {
            ThemeManager.Select(ThemeSettings.Load());paintedTheme=ThemeManager.Current;
            if(File.Exists(Path.Combine(DataStore.DefaultRoot,"website-state.json")))startupData=Task.Run(()=>{WebsiteData.PruneSnapshots(DataStore.DefaultRoot);var loaded=new DataStore();loaded.Load(DataStore.DefaultRoot);loaded.PrepareGainBoardsAsync();StartupTrace.Mark("data-loaded");return loaded;});
            HandleCreated+=(s,e)=>StartupTrace.Mark("handle-created");
            Shown+=(s,e)=>{StartupTrace.Mark("shown");if(StartupTrace.Enabled){var timeout=new Timer{Interval=StartupTrace.TimeoutDelay};timeout.Tick+=(a,b)=>{timeout.Stop();if(StartupTrace.HasRendered)return;StartupTrace.Mark("timeout");exitRequested=true;Close();};timeout.Start();FormClosed+=(a,b)=>timeout.Dispose();}};
            Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);InitializeTray();
            Text=AppVersion.ProductName;Size=new Size(1140,Math.Min(1200,Screen.FromPoint(Cursor.Position).WorkingArea.Height));MinimumSize=new Size(SizeFromClientSize(new Size(1140,0)).Width,520);ClientSize=new Size(1140,ClientSize.Height);Font=DesktopTheme.Body;StartPosition=FormStartPosition.CenterScreen;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(20,16,20,8)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,94));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,0));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));Controls.Add(layout);
            var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};header.RowStyles.Add(new RowStyle(SizeType.Absolute,40));header.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.Controls.Add(header,0,0);var navigation=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};header.Controls.Add(navigation,0,0);var first=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};header.Controls.Add(first,0,1);
            heroes.Width=270;heroes.DropDownStyle=ComboBoxStyle.DropDown;heroes.AutoCompleteMode=AutoCompleteMode.SuggestAppend;heroes.AutoCompleteSource=AutoCompleteSource.ListItems;
            augmentSearch.Width=210;augmentSearch.AutoSize=false;augmentSearch.AccessibleName="搜索海克斯名称";augmentSearch.Margin=new Padding(0,6,0,0);augmentSearchLabel.Text="搜索海克斯：";augmentSearchLabel.AutoSize=false;augmentSearchLabel.TextAlign=ContentAlignment.MiddleLeft;augmentSearchLabel.Margin=new Padding(16,6,3,0);augmentSearch.TextChanged+=(s,e)=>ShowRows();augmentSearch.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){augmentSearch.Clear();e.SuppressKeyPress=true;}};TopMost=false;rarity.DropDownStyle=ComboBoxStyle.DropDownList;rarity.Width=105;rarity.Items.AddRange(new object[]{"全部","彩","金","银"});rarity.SelectedIndex=0;rarity.AccessibleName="海克斯品质";rarity.SelectedIndexChanged+=(s,e)=>ShowRows();
            heroes.Visible=false; Controls.Add(heroes); back.Text="←   返回英雄总榜";back.AutoSize=false;back.Size=new Size(145,34);back.Margin=new Padding(3);back.TextAlign=ContentAlignment.MiddleCenter;back.Click+=(s,e)=>{search.Clear();heroes.SelectedItem=data.Heroes.FirstOrDefault(h=>h.Id==0);};pageTitle.AutoSize=false;pageTitle.Size=new Size(160,34);pageTitle.Margin=new Padding(3);pageTitle.TextAlign=ContentAlignment.MiddleLeft;pageTitle.Padding=new Padding(0,0,12,0);search.Width=220;search.AccessibleName="搜索英雄名称、称号或外号";navigation.Controls.AddRange(new Control[]{back,pageTitle});hudSkillMode.Items.AddRange(new object[]{"不显示","只显示最佳加点顺序","显示全部合理加点顺序"});hudSkillMode.SelectedIndex=HudSkillSettings.Load();hudSkillMode.SelectedIndexChanged+=(s,e)=>{try{HudSkillSettings.Save(hudSkillMode.SelectedIndex);}catch(Exception error){MessageBox.Show(this,"本次选择已生效，但保存失败："+error.Message,"设置保存失败");}};first.Controls.AddRange(new Control[]{highDataSource,new Label{Text="搜索英雄名称、称号或外号：",AutoSize=false,Size=new Size(205,34),TextAlign=ContentAlignment.MiddleRight},search});
            var settingsButton=new ModernButton{Text="设置",AccessibleName="设置",Dock=DockStyle.Right,Width=72,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand,Font=new Font("Microsoft YaHei UI",10),ForeColor=BadgePaint.Gold};settingsButton.FlatAppearance.BorderSize=1;settingsButton.FlatAppearance.BorderColor=BadgePaint.Gold;settingsButton.Click+=(sender,args)=>ShowSettings();header.Controls.Remove(navigation);var navHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};navHost.Controls.Add(navigation);checkDataButton.Click+=async(sender,args)=>await UpdateWebsite(true);navHost.Controls.Add(settingsButton);header.Controls.Add(navHost,0,0);
            var second=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(second,0,1);categories.Width=590;categories.DropDownStyle=ComboBoxStyle.DropDownList;search.Width=250;search.AccessibleName="搜索英雄名称、称号或外号，按回车切换";
            categories.Visible=false;Controls.Add(categories);second.FlowDirection=FlowDirection.TopDown;second.WrapContents=false; summary.AutoSize=true;summary.Margin=new Padding(3,4,0,12);summaryRate.AutoSize=true;summaryRate.Margin=new Padding(0,5,3,11);var overview=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};foreach(var label in new[]{rankCaption,rankValue,winCaption}){label.AutoSize=true;label.Margin=new Padding(0,4,0,12);}rankCaption.Text="排名";winCaption.Text="统计学胜率 ";overview.Controls.AddRange(new Control[]{summary,rankCaption,rankValue,winCaption,summaryRate});second.Controls.Add(overview);tabs.AutoSize=true;tabs.WrapContents=false;second.Controls.Add(tabs);filters.Dock=DockStyle.Fill;filters.WrapContents=true;layout.Controls.Add(filters,0,2);foreach(string label in new[]{"海克斯","海克斯组合","装备","召唤师技能","技能加点"}){var button=new NavigationButton{Text=label,Primary=true,AutoSize=false,Size=new Size(label.Length>4?120:100,34)};button.Click+=(s,e)=>{section=button.Text;if(section=="装备")LoadGroups(true);else BuildFilters();};tabs.Controls.Add(button);} suggestions.FormattingEnabled=true;suggestions.Format+=(s,e)=>{var hero=e.ListItem as Hero;if(hero!=null)e.Value=ShortName(hero.Name);};suggestions.Visible=false;suggestions.Height=180;suggestions.Width=300;Controls.Add(suggestions); suggestions.Click+=(s,e)=>OpenSuggestion(); suggestions.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;OpenSuggestion();}else if(e.KeyCode==Keys.Escape)suggestions.Hide();};search.TextChanged+=(s,e)=>UpdateSuggestions();search.Leave+=(s,e)=>BeginInvoke((Action)(()=>{if(!suggestions.Focused)suggestions.Hide();}));
            var overlayOptions=new FlowLayoutPanel{Dock=DockStyle.Fill};
            overlayStatus.AutoSize=true;overlayStatus.Text="自动跟随本局英雄 · 可随时搜索其他英雄";
            overlayStatus.Visible=false;
            var footer=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};layout.Controls.Add(footer,0,5);source.Font=DesktopTheme.Small;source.AutoSize=true;source.ForeColor=Color.DimGray;source.Padding=new Padding(0,3,0,0);source.Dock=DockStyle.Fill;source.AutoSize=false;source.AutoEllipsis=true;footer.Controls.Add(source);checkDataButton.Width=110;checkDataButton.Font=DesktopTheme.Small;checkDataButton.ForeColor=Color.DimGray;checkDataButton.FlatStyle=FlatStyle.Flat;checkDataButton.FlatAppearance.BorderSize=0;footer.Controls.Add(checkDataButton);
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.RowTemplate.Height=30;grid.BackgroundColor=DesktopTheme.Surface;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.AutoGenerateColumns=false;
            foreach(string name in new[]{"排名","名称","统计学胜率","统计学胜率提升"}){var column=new DataGridViewTextBoxColumn{HeaderText=name,SortMode=DataGridViewColumnSortMode.NotSortable,Width=name=="名称"?310:(name=="统计学胜率"?170:name=="统计学胜率提升"?160:95)};if(name=="名称")column.AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;grid.Columns.Add(column);}
            grid.Columns[2].DefaultCellStyle.ForeColor=Color.FromArgb(15,115,95);grid.Columns[2].DefaultCellStyle.Font=new Font(Font,FontStyle.Bold);grid.Columns[3].DefaultCellStyle.Font=grid.Columns[2].DefaultCellStyle.Font;summaryRate.Font=grid.Columns[2].DefaultCellStyle.Font;layout.Controls.Add(grid,0,4);
            status.AutoSize=true;status.Padding=new Padding(4,3,0,0);status.Text="正在读取独立网站数据…";
            heroes.SelectedIndexChanged+=(s,e)=>LoadGroups();categories.SelectedIndexChanged+=(s,e)=>{ShowRows();};laterFlow.SelectedIndexChanged+=(s,e)=>ShowRows();search.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.Handled=true;e.SuppressKeyPress=true;SearchHero();}else if(e.KeyCode==Keys.Down&&suggestions.Visible){e.SuppressKeyPress=true;suggestions.Focus();if(suggestions.Items.Count>0)suggestions.SelectedIndex=0;}else if(e.KeyCode==Keys.Escape)suggestions.Hide();};
            grid.CellMouseMove+=(s,e)=>{bool onText=false;if(e.RowIndex>=0&&e.ColumnIndex==1&&SelectedHero!=null&&SelectedHero.Id==0){var bounds=grid.Rows[e.RowIndex].Cells[e.ColumnIndex].GetContentBounds(e.RowIndex);onText=bounds.Contains(e.X,e.Y);}grid.Cursor=onText?Cursors.Hand:Cursors.Default;};grid.CellMouseLeave+=(s,e)=>grid.Cursor=Cursors.Default;grid.MouseLeave+=(s,e)=>{grid.Cursor=Cursors.Default;ClearEffectHover();};
            grid.ShowCellToolTips=false;
            grid.CellMouseMove+=(s,e)=>MoveEffectHover(e);
            grid.CellPainting+=(s,e)=>PaintEffectCell(e);
            grid.CellMouseLeave+=(s,e)=>ClearEffectHover();
            grid.Scroll+=(s,e)=>ClearEffectHover();

            grid.CellClick+=(s,e)=>{if(e.RowIndex>=0&&SelectedHero!=null&&SelectedHero.Id==0){var row=grid.Rows[e.RowIndex].Tag as Row;var hero=row==null?null:data.Heroes.FirstOrDefault(h=>h.Id>0&&h.Name==row.Name);if(hero!=null)heroes.SelectedItem=hero;}}; ApplyTheme(this);highDataSource.ForeColor=DesktopTheme.Muted;StylePicker(rarity);suggestions.BackColor=ThemeManager.Current["desktop-field"];suggestions.ForeColor=ThemeManager.Current["choice-text"];suggestions.BorderStyle=BorderStyle.FixedSingle;suggestions.DrawMode=DrawMode.OwnerDrawFixed;suggestions.ItemHeight=26;suggestions.DrawItem+=DrawSuggestion;
            back.FlatStyle=FlatStyle.Flat;back.FlatAppearance.BorderSize=0;back.BackColor=BackColor;back.FlatAppearance.MouseOverBackColor=BackColor;back.FlatAppearance.MouseDownBackColor=BackColor;back.ForeColor=BadgePaint.Gold;back.Cursor=Cursors.Hand;
            foreach(NavigationButton tab in tabs.Controls)StyleNavigation(tab,tab.Text==section);
            first.Layout+=(s,e)=>{foreach(Control c in first.Controls){int top=Math.Max(0,(40-c.Height)/2);var margin=c==search?new Padding(0,top,8,0):c is Label&&c.Text.StartsWith("搜索英雄")?new Padding(3,top,2,0):new Padding(3,top,8,0);if(c.Margin!=margin)c.Margin=margin;}};
            grid.SizeChanged+=(s,e)=>ScheduleFitRows();grid.EnableHeadersVisualStyles=false;grid.BorderStyle=BorderStyle.None;grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.GridColor=DesktopTheme.Line;grid.BackgroundColor=DesktopTheme.Surface;grid.ColumnHeadersHeight=44;grid.RowTemplate.Height=44;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.DefaultCellStyle.Padding=new Padding(12,0,8,0);grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(12,0,8,0);grid.ColumnHeadersDefaultCellStyle.Font=DesktopTheme.Strong;grid.AlternatingRowsDefaultCellStyle.BackColor=ThemeManager.Current["grid-alternate"];
            grid.DefaultCellStyle.BackColor=DesktopTheme.Surface;grid.DefaultCellStyle.ForeColor=ThemeManager.Current["grid-text"];grid.DefaultCellStyle.SelectionBackColor=DesktopTheme.Selected;grid.DefaultCellStyle.SelectionForeColor=ThemeManager.Current["grid-selection-text"];grid.ColumnHeadersDefaultCellStyle.BackColor=DesktopTheme.Raised;grid.ColumnHeadersDefaultCellStyle.ForeColor=DesktopTheme.Muted;grid.Columns[2].DefaultCellStyle.ForeColor=BadgePaint.Jade;emptyRoutes.BackColor=grid.BackgroundColor;emptyRoutes.ForeColor=grid.ForeColor;grid.Controls.Add(emptyRoutes);source.ForeColor=ThemeManager.Current["source-text"];
            ThemeManager.Changed+=ThemeChanged;FormClosed+=(s,e)=>ThemeManager.Changed-=ThemeChanged;PrepareWeb(layout);TextChanged+=(s,e)=>ScheduleWeb();source.TextChanged+=(s,e)=>ScheduleWeb();checkDataButton.TextChanged+=(s,e)=>ScheduleWeb();
            timer.Interval=1000;timer.Tick+=async(s,e)=>{await Poll();};Shown+=async(s,e)=>{await ApplyStartupData();if(IsDisposed)return;overlay=new OverlayController(()=>data,()=>SelectedHero==null?0:SelectedHero.Id,()=>Scope,()=>-2,()=>true,m=>overlayStatus.Text=m,id=>FollowHero(id));overlay.ConfirmedHero=()=>clientStatus!=null&&clientStatus.IsMayhem&&DateTime.UtcNow-clientStatusTime<TimeSpan.FromSeconds(8)?clientStatus.Champion:0;overlay.ModeAllowed=()=>MayhemActive(false);overlay.Enabled=data.Heroes.Count>0;hudSkillOverlay=new HudSkillOverlay(()=>data,()=>overlay.CurrentHero,()=>Scope,()=>data.Heroes.Count>0&&MayhemActive(false),()=>hudSkillMode.SelectedIndex);selectionOverlay=new SelectionOverlayController(()=>data,()=>Scope,()=>DateTime.UtcNow-clientStatusTime<TimeSpan.FromSeconds(8)?clientStatus:null,()=>data.Heroes.Count>0&&MayhemActive(true),m=>overlayStatus.Text=m);timer.Start();await Poll();await UpdateWebsite();};FormClosed+=(s,e)=>{trayIcon.Visible=false;trayIcon.ContextMenuStrip.Dispose();trayIcon.Dispose();effectTip.Dispose();timer.Stop();if(hudSkillOverlay!=null)hudSkillOverlay.Dispose();if(overlay!=null)overlay.Dispose();if(selectionOverlay!=null)selectionOverlay.Dispose();};
        }
        Button checkDataButton=new ModernButton{Text="检查数据更新",AccessibleName="检查数据更新",Dock=DockStyle.Right,Width=132,Cursor=Cursors.Hand};
        Form settingsWindow;
        void SelectTheme(string id){ThemeManager.Select(id);ThemeSettings.Save(ThemeManager.Current.Id);}
        ThemePalette paintedTheme=ThemeManager.Current;
        static Color Recolor(Color value,ThemePalette before,ThemePalette after){foreach(var key in before.Css().Keys.Where(k=>k!="crumb"))if(value.ToArgb()==before[key].ToArgb())return after[key];return value;}
        static void RecolorControls(Control control,ThemePalette before,ThemePalette after){
            control.BackColor=Recolor(control.BackColor,before,after);control.ForeColor=Recolor(control.ForeColor,before,after);
            var table=control as DataGridView;if(table!=null){table.BackgroundColor=Recolor(table.BackgroundColor,before,after);table.GridColor=Recolor(table.GridColor,before,after);foreach(var style in new[]{table.DefaultCellStyle,table.AlternatingRowsDefaultCellStyle,table.ColumnHeadersDefaultCellStyle}){style.BackColor=Recolor(style.BackColor,before,after);style.ForeColor=Recolor(style.ForeColor,before,after);style.SelectionBackColor=Recolor(style.SelectionBackColor,before,after);style.SelectionForeColor=Recolor(style.SelectionForeColor,before,after);}}
            var button=control as Button;if(button!=null)button.FlatAppearance.BorderColor=Recolor(button.FlatAppearance.BorderColor,before,after);
            foreach(Control child in control.Controls)RecolorControls(child,before,after);control.Invalidate();
        }
        void ThemeChanged(object sender,EventArgs args){
            var next=ThemeManager.Current;Color rateColor=summaryRate.ForeColor;bool hasRate=rateColor.ToArgb()!=paintedTheme["desktop-muted"].ToArgb();RecolorControls(this,paintedTheme,next);if(hasRate)summaryRate.ForeColor=rateColor;if(settingsWindow!=null&&!settingsWindow.IsDisposed)RecolorControls(settingsWindow,paintedTheme,next);if(dataUpdateWindow!=null&&!dataUpdateWindow.IsDisposed)RecolorControls(dataUpdateWindow,paintedTheme,next);paintedTheme=next;
            if(web!=null&&!web.IsDisposed)web.DefaultBackgroundColor=DesktopTheme.Canvas;
            cachedWebRows=null;lastWebState=null;if(classicActive)ShowRows();SendWeb(new{type="theme",theme=next.Id,palette=next.Css()});ScheduleWeb();
        }
        void ShowSettings(){if(settingsWindow!=null&&!settingsWindow.IsDisposed){settingsWindow.Activate();return;}var dialog=new Form{Text="设置",Icon=Icon,ClientSize=new Size(420,200),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.Manual,Font=DesktopTheme.Body};settingsWindow=dialog;dialog.Location=new Point(Left+(Width-dialog.Width)/2,Top+(Height-dialog.Height)/2);dialog.FormClosed+=(sender,args)=>{settingsWindow=null;};
            var label=new Label{Text="局内技能加点",AutoSize=true,Location=new Point(20,20)};
            var picker=new ModernComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(20,55),Width=380,AccessibleName="局内技能加点显示方式"};picker.Items.AddRange(new object[]{"不显示","只显示最佳加点顺序","显示全部合理加点顺序"});picker.SelectedIndex=hudSkillMode.SelectedIndex;picker.SelectedIndexChanged+=(sender,args)=>hudSkillMode.SelectedIndex=picker.SelectedIndex;
            var closeLabel=new Label{Text="关闭窗口时",AutoSize=true,Location=new Point(20,108)};
            var closePicker=new ModernComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(20,143),Width=380,AccessibleName="关闭窗口时"};closePicker.Items.AddRange(new object[]{"退出程序","最小化至托盘"});closePicker.SelectedIndex=minimizeToTray?1:0;closePicker.SelectedIndexChanged+=(sender,args)=>{minimizeToTray=closePicker.SelectedIndex==1;try{CloseBehaviorSettings.Save(minimizeToTray);}catch(Exception error){MessageBox.Show(dialog,"本次选择已生效，但保存失败："+error.Message,"设置保存失败");}};
            dialog.ClientSize=new Size(420,296);var autoPicker=new CheckBox{Text="进入对局时自动最小化主窗口",AutoSize=true,Location=new Point(20,205),Checked=autoMinimize};autoPicker.CheckedChanged+=(sender,args)=>{autoMinimize=autoPicker.Checked;try{AutoMinimizeSettings.Save(autoMinimize);}catch(Exception error){MessageBox.Show(dialog,"本次选择已生效，但保存失败："+error.Message,"设置保存失败");}};var shopPicker=new CheckBox{Text="选人时自动导入商店方案",AutoSize=true,Location=new Point(20,253),Checked=shopSession.AutoImportEnabled};shopPicker.CheckedChanged+=(sender,args)=>{try{shopSession.SetAutoImport(shopPicker.Checked);}catch(Exception error){MessageBox.Show(dialog,"本次选择保存失败："+error.Message,"设置保存失败");}};dialog.ClientSize=new Size(420,390);var themeLabel=new Label{Text="主题",AutoSize=true,Location=new Point(20,298)};var themePicker=new ModernComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(20,331),Width=380,AccessibleName="主题"};themePicker.Items.AddRange(ThemePalette.All.Select(p=>(object)p.Name).ToArray());themePicker.SelectedIndex=Array.IndexOf(ThemePalette.All,ThemeManager.Current);themePicker.SelectedIndexChanged+=(sender,args)=>{try{SelectTheme(ThemePalette.All[themePicker.SelectedIndex].Id);}catch(Exception error){MessageBox.Show(dialog,"本次选择已生效，但保存失败："+error.Message,"设置保存失败");}};dialog.Controls.AddRange(new Control[]{label,picker,closeLabel,closePicker,autoPicker,shopPicker,themeLabel,themePicker});ApplyTheme(dialog);StylePicker(picker);StylePicker(closePicker);StylePicker(themePicker);dialog.Show(this);
        }
        static void StyleNavigation(NavigationButton button,bool selected){
            button.Selected=selected;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.Cursor=Cursors.Hand;button.Margin=new Padding(0,1,8,1);button.Padding=Padding.Empty;
            button.BackColor=selected?DesktopTheme.Selected:DesktopTheme.Canvas;button.ForeColor=selected?DesktopTheme.Accent:DesktopTheme.Muted;button.Font=selected?DesktopTheme.Strong:DesktopTheme.Body;button.Invalidate();
        }
        static void StylePicker(ComboBox combo){combo.FlatStyle=FlatStyle.Flat;combo.BackColor=DesktopTheme.Surface;combo.ForeColor=DesktopTheme.Text;combo.ItemHeight=24;combo.DrawMode=DrawMode.OwnerDrawFixed;combo.DrawItem-=DrawPicker;combo.DrawItem+=DrawPicker;combo.DropDownClosed-=PickerClosed;combo.DropDownClosed+=PickerClosed;}
        static void PickerClosed(object sender,EventArgs e){((ComboBox)sender).Invalidate();}
        static void DrawPicker(object sender,DrawItemEventArgs e){var combo=(ComboBox)sender;string text=e.Index>=0?combo.GetItemText(combo.Items[e.Index]):combo.Text;DrawChoice(e,text,(e.State&DrawItemState.ComboBoxEdit)==0&&combo.DroppedDown);}
        void DrawSuggestion(object sender,DrawItemEventArgs e){if(e.Index<0)return;DrawChoice(e,ShortName(((Hero)suggestions.Items[e.Index]).Name));}
        static void DrawChoice(DrawItemEventArgs e,string text,bool allowHighlight=true){bool selected=allowHighlight&&(e.State&DrawItemState.Selected)!=0;using(var brush=new SolidBrush(selected?DesktopTheme.Selected:DesktopTheme.Surface))e.Graphics.FillRectangle(brush,e.Bounds);var bounds=e.Bounds;bounds.X+=10;bounds.Width-=20;TextRenderer.DrawText(e.Graphics,text,e.Font,bounds,ThemeManager.Current["choice-text"],TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);}
        static void ApplyTheme(Control parent){
            parent.BackColor=DesktopTheme.Canvas;parent.ForeColor=DesktopTheme.Text;
            foreach(Control child in parent.Controls.Cast<Control>().ToArray()){ApplyTheme(child);
                var button=child as Button;if(button!=null){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=DesktopTheme.Line;button.BackColor=DesktopTheme.Surface;button.ForeColor=DesktopTheme.Text;button.Padding=new Padding(3);}
                var combo=child as ComboBox;if(combo!=null){combo.FlatStyle=FlatStyle.Flat;combo.BackColor=ThemeManager.Current["desktop-field"];}
                var input=child as TextBox;if(input!=null){input.BorderStyle=BorderStyle.None;input.BackColor=DesktopTheme.Surface;if(!(parent is InputSurface)&&input.AccessibleName!="搜索海克斯名称"){int index=parent.Controls.GetChildIndex(input);parent.Controls.Remove(input);var host=new InputSurface(input);parent.Controls.Add(host);parent.Controls.SetChildIndex(host,index);}}
            }
        }
        Form dataUpdateWindow;
        void ShowDataUpdate(WebsiteData.UpdateInfo info){
            if(dataUpdateWindow!=null&&!dataUpdateWindow.IsDisposed)dataUpdateWindow.Close();
            var dialog=new Form{Text="检查数据更新",Icon=Icon,ClientSize=new Size(400,info.NeedsSync?132:90),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.Manual,Font=Font};dataUpdateWindow=dialog;
            var current=new Label{AutoSize=true,Location=new Point(20,16)};var latest=new Label{AutoSize=true,Location=new Point(20,48)};
            var sync=new ModernButton{Text="同步最新数据",Size=new Size(146,32),Location=new Point(20,84),Visible=info.NeedsSync};
            Action<WebsiteData.UpdateInfo> present=x=>{current.Text="当前大数据版本："+x.CurrentVersion+"　"+x.CurrentDate;latest.Text="数据源最新版本："+x.LatestVersion+"　"+x.LatestDate;sync.Visible=x.NeedsSync;dialog.ClientSize=new Size(400,x.NeedsSync?132:90);};present(info);
            sync.Click+=async(sender,args)=>{if(updating)return;sync.Enabled=false;sync.Text="同步中…";try{await UpdateWebsite(false,true);var fresh=await Task.Run(()=>WebsiteData.Check(DataStore.DefaultRoot));if(!dialog.IsDisposed)present(fresh);}catch(Exception error){if(!dialog.IsDisposed)MessageBox.Show(dialog,error.GetBaseException().Message,"检查未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{if(!dialog.IsDisposed){sync.Enabled=true;sync.Text="同步最新数据";}}};
            dialog.FormClosed+=(sender,args)=>{if(dataUpdateWindow==dialog)dataUpdateWindow=null;};dialog.Controls.AddRange(new Control[]{current,latest,sync});ApplyTheme(dialog);dialog.Location=new Point(Left+(Width-dialog.Width)/2,Top+(Height-dialog.Height)/2);dialog.Show(this);
        }
        async Task UpdateWebsite(bool manual=false,bool notifyErrors=false){
            if((manual&&webReady)||(!manual&&!notifyErrors&&!classicActive&&!StartupTrace.ForceClassic)){await UpdateWebWebsite(manual);return;}
            if(updating)return;updating=true;checkDataButton.Enabled=false;checkDataButton.Text="检查中…";
            
            try{
                if(manual){var info=await Task.Run(()=>WebsiteData.Check(DataStore.DefaultRoot));if(!IsDisposed&&!Disposing)ShowDataUpdate(info);return;}
                var progress=new Progress<string>(message=>{if(IsDisposed||Disposing||!updating)return;checkDataButton.Text=message.StartsWith("正在下载")?"下载中…":"检查中…";searchTip.SetToolTip(checkDataButton,message);});
                // Old stores remain in group/overlay tasks; prune on the next startup before loading data.
                string result=await Task.Run(()=>WebsiteData.Update(DataStore.DefaultRoot,message=>((IProgress<string>)progress).Report(message)));
                if(IsDisposed)return;if(!result.StartsWith("网站数据已是最新"))await ReloadAsync();FillWebIcons();source.Text="程序版本："+AppVersion.Display+"　游戏版本："+data.Version+"　大数据更新日期："+data.Date;

            }catch(Exception ex){
                if(IsDisposed)return;bool available=File.Exists(Path.Combine(DataStore.DefaultRoot,"website-state.json"));
                if(available)ReloadAfterFailure();
                status.Text="更新未完成："+ex.GetBaseException().Message;
                if(manual||notifyErrors)MessageBox.Show(this,status.Text+(available?"\n仍可使用原有完整数据。":"\n请检查网络后重试。"),"数据更新未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }finally{updating=false;if(!IsDisposed&&!Disposing){checkDataButton.Enabled=true;checkDataButton.Text="检查数据更新";searchTip.SetToolTip(checkDataButton,"检查数据源版本，有更新时可手动同步。");if(overlay!=null)overlay.Enabled=data.Heroes.Count>0;}}
        }
        async void ReloadAfterFailure(){if(await Task.Run(()=>WebsiteData.Verify(DataStore.DefaultRoot)))await ReloadAsync();FillWebIcons();}
        async Task ReloadAsync(bool propagateErrors=false){int id=SelectedHero==null?0:SelectedHero.Id;loading=true;try{var loaded=await Task.Run(()=>{var store=new DataStore();store.Load(DataStore.DefaultRoot);store.PrepareGainBoardsAsync();return store;});if(IsDisposed)return;StartupTrace.Mark("data-loaded");data=loaded;PrepareWebGainBoards();heroes.DataSource=data.Heroes;heroes.SelectedItem=data.Heroes.FirstOrDefault(h=>h.Id==id)??data.Heroes[0];}catch(Exception e){status.Text="尚无完整网站数据，请联网后重新打开程序。"+e.Message;if(propagateErrors)throw;}finally{loading=false;}if(propagateErrors)await LoadGroupsAsync();else LoadGroups();}
        static string ShortName(string name){int at=name.LastIndexOf(" · ",StringComparison.Ordinal);return at<0?name:name.Substring(at+3);}
        static string Percent(double value){return double.IsNaN(value)?"—":(value*100).ToString("F1")+"%";}
        int loadGeneration;
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        async void LoadGroups(bool equipment=false){await LoadGroupsAsync(equipment);}
        async Task LoadGroupsAsync(bool equipment=false){
            if(loading||SelectedHero==null)return;
            int generation=++loadGeneration,id=SelectedHero.Id;string sample=Scope;var snapshot=data;List<Group> loaded;
            bool needEquipment=equipment||(viewedHero==id&&section=="装备");
            try{loaded=await Task.Run(()=>snapshot.DisplayGroups(id,sample,needEquipment));}catch(Exception e){if(generation==loadGeneration&&!IsDisposed)status.Text="该英雄数据不可用："+e.Message;return;}
            if(IsDisposed||Disposing||generation!=loadGeneration||snapshot!=data||SelectedHero==null||SelectedHero.Id!=id||Scope!=sample)return;
            IntPtr redrawTarget=grid.Parent.IsHandleCreated?grid.Parent.Handle:IntPtr.Zero;bool redraw=classicActive&&redrawTarget!=IntPtr.Zero;if(redraw)SendMessage(redrawTarget,11,IntPtr.Zero,IntPtr.Zero);
            SuspendLayout();grid.Parent.SuspendLayout();rebuilding=true;grid.SuspendLayout();try{
                bool home=SelectedHero.Id==0;var layout=grid.Parent as TableLayoutPanel;if(layout!=null){layout.RowStyles[1].Height=home?0:78;layout.RowStyles[2].Height=home?0:40;}string oldPath=(categories.SelectedItem as Group)==null?null:((Group)categories.SelectedItem).Path;
                if(viewedHero!=SelectedHero.Id){section="海克斯";oldPath=null;}viewedHero=SelectedHero.Id;
                groups=loaded;back.Visible=!home;pageTitle.Visible=home;pageTitle.Font=DesktopTheme.Title;pageTitle.Text=home?"英雄总榜":ShortName(SelectedHero.Name);pageTitle.ForeColor=DesktopTheme.Text;tabs.Visible=!home;filters.Visible=!home;summary.Visible=!home;summaryRate.Visible=!home;
                var baseline=groups.FirstOrDefault(g=>g.Path=="champions/champion/stats");var row=baseline==null?null:baseline.Rows.FirstOrDefault();
                summary.Text=ShortName(SelectedHero.Name)+"     ";rankCaption.ForeColor=DesktopTheme.Muted;winCaption.ForeColor=DesktopTheme.Muted;rankValue.Text="#"+(row!=null&&row.Rank>0?row.Rank.ToString():"—")+"     ";rankCaption.Visible=!home;rankValue.Visible=!home;winCaption.Visible=!home;summaryRate.Text=row==null?"":Percent(row.Lower);summaryRate.ForeColor=BadgePaint.RateColor(row==null?double.NaN:row.Lower);

                categories.DataSource=groups;
                if(home)categories.SelectedItem=groups.FirstOrDefault(g=>g.Path=="champions/rows");else BuildFilters(oldPath);
                source.Text="程序版本："+AppVersion.Display+"　游戏版本："+data.Version+"　大数据更新日期："+data.Date;ShowRows();
            }catch(Exception e){grid.Rows.Clear();status.Text="该英雄数据不可用："+e.Message;}finally{rebuilding=false;grid.ResumeLayout();grid.Parent.ResumeLayout(true);ResumeLayout(true);try{ShowRows();}finally{if(redraw){SendMessage(redrawTarget,11,new IntPtr(1),IntPtr.Zero);grid.Parent.Invalidate(true);}if(!classicActive){classicLayout.Visible=false;if(web!=null)web.BringToFront();}}}
        }
        void BuildFilters(string preferred=null){
            filters.SuspendLayout();grid.Parent.SuspendLayout();try{
            foreach(var oldButton in filters.Controls.OfType<NavigationButton>().ToArray()){filters.Controls.Remove(oldButton);oldButton.Dispose();}
            filters.Controls.Clear();
            foreach(NavigationButton tab in tabs.Controls)StyleNavigation(tab,tab.Text==section);
            var options=new List<KeyValuePair<string,string>>();
            if(section=="海克斯"){options.Add(new KeyValuePair<string,string>("总体","champions/augments/all"));for(int i=1;i<=4;i++)options.Add(new KeyValuePair<string,string>(new[]{"总体","一选","二选","三选","四选"}[i],"champions/augments/stages/"+i));}
            else if(section=="装备"){options.Add(new KeyValuePair<string,string>("核心出装",EquipmentRecommendations.Prefix+"plans"));for(int i=5;i<=6;i++)if(groups.Any(g=>g.Path==EquipmentRecommendations.Prefix+"later/"+i))options.Add(new KeyValuePair<string,string>("第"+i+"件",EquipmentRecommendations.Prefix+"later/"+i));}
            else {string path=section=="海克斯组合"?"champions/augmentCombinations":section=="召唤师技能"?"champions/summoners":"champions/skills";options.Add(new KeyValuePair<string,string>(section,path));}
            filters.Visible=options.Count>1;foreach(var option in options){var path=option.Value;var button=new NavigationButton{Text=option.Key,Tag=path,AutoSize=false,Size=new Size(option.Key.Length>4?110:90,30),Enabled=path=="starters"?groups.Any(g=>g.Path==RouteRecommendations.GroupPath):groups.Any(g=>g.Path==path)};button.Click+=(s,e)=>SelectGroup(path);filters.Controls.Add(button);}if(section=="海克斯"){if(filters.Controls.Count>0)filters.SetFlowBreak(filters.Controls[filters.Controls.Count-1],true);rarity.Margin=new Padding(0,6,0,0);filters.Controls.Add(rarity);filters.Controls.Add(augmentSearchLabel);filters.Controls.Add(augmentSearch);}if(section=="装备"){if(filters.Controls.Count>0)filters.SetFlowBreak(filters.Controls[filters.Controls.Count-1],true);laterFlow.Margin=new Padding(0,6,0,0);filters.Controls.Add(laterFlowLabel);filters.Controls.Add(laterFlow);}ApplyTheme(filters);if(section=="海克斯"){augmentSearch.Height=rarity.Height;augmentSearchLabel.Height=rarity.Height;augmentSearchLabel.Width=TextRenderer.MeasureText(augmentSearchLabel.Text,augmentSearchLabel.Font).Width;}
            SelectGroup(options.Any(o=>o.Value==preferred)?preferred:options[0].Value);
            }finally{filters.ResumeLayout(true);grid.Parent.ResumeLayout(true);}
        }
        void SelectGroup(string path){
            bool wasRebuilding=rebuilding;rebuilding=true;try{
            bool starters=path=="starters";var layout=grid.Parent as TableLayoutPanel;if(layout!=null)layout.RowStyles[2].Height=section=="海克斯"?72:section=="装备"?(path.Contains("/later/")?72:40):0;
            categories.SelectedItem=groups.FirstOrDefault(g=>g.Path==(starters?RouteRecommendations.GroupPath:path));
            bool later=path.Contains("/later/");laterFlow.Visible=laterFlowLabel.Visible=later;
            if(later){string previous=laterFlow.SelectedItem as string;var group=categories.SelectedItem as Group;var core=groups.FirstOrDefault(g=>g.Path==EquipmentRecommendations.Prefix+"plans");var labels=(core==null?Enumerable.Empty<string>():core.Rows.Select(r=>r.Name)).Concat(group==null?Enumerable.Empty<string>():group.Rows.Select(r=>r.Name)).Distinct().ToArray();laterFlow.DataSource=labels;if(previous!=null&&labels.Contains(previous))laterFlow.SelectedItem=previous;}
            foreach(NavigationButton b in filters.Controls.OfType<NavigationButton>())StyleNavigation(b,(string)b.Tag==path);
            }finally{rebuilding=wasRebuilding;}ShowRows();
        }
        bool MatchesRarity(Group group,Row row){
            if(!group.Path.StartsWith("champions/augments/"))return true;string query=augmentSearch.Text.Trim();if(query.Length>0&&(row.Name??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)return false;if(rarity.SelectedIndex<=0)return true;
            Dictionary<string,object> resources;object augment;
            string id=Json.Str(Json.Get(row.Raw,"id")??Json.Get(row.Raw,"augmentId"));
            if(!data.Resources.TryGetValue("augments",out resources)||!resources.TryGetValue(id,out augment))return false;
            return Json.Str(Json.Get(augment,"rarity"))==new[]{"","prismatic","gold","silver"}[rarity.SelectedIndex];
        }
        ToolTip effectTip=new ToolTip{AutoPopDelay=15000,InitialDelay=0,ReshowDelay=0};int effectRow=-1,effectColumn=-1,effectIndex=-1;string effectContent="";
        void ClearEffectHover(){effectTip.Hide(grid);int row=effectRow,column=effectColumn;effectRow=effectColumn=effectIndex=-1;effectContent="";if(row>=0&&row<grid.Rows.Count&&column>=0)grid.InvalidateCell(column,row);}
        void MoveEffectHover(DataGridViewCellMouseEventArgs e){
            if(e.RowIndex<0||e.ColumnIndex<0){ClearEffectHover();return;}var cell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];if(string.IsNullOrEmpty(cell.ToolTipText)){ClearEffectHover();return;}
            var bounds=grid.GetCellDisplayRectangle(e.ColumnIndex,e.RowIndex,false);var point=new Point(bounds.X+e.X,bounds.Y+e.Y);
            var run=EffectText.Layout(data,Convert.ToString(cell.Value),cell.InheritedStyle.Font,bounds,Json.Str(cell.Tag)).FirstOrDefault(r=>r.Kind.Length>0&&r.Bounds.Contains(point));
            if(run==null){ClearEffectHover();return;}if(effectRow==e.RowIndex&&effectColumn==e.ColumnIndex&&effectIndex==run.Index)return;
            ClearEffectHover();effectRow=e.RowIndex;effectColumn=e.ColumnIndex;effectIndex=run.Index;effectContent=EffectText.Single(data,run.Kind,run.Name);grid.InvalidateCell(e.ColumnIndex,e.RowIndex);effectTip.Show(effectContent,grid,point.X+12,point.Y+18,15000);
        }
        void PaintEffectCell(DataGridViewCellPaintingEventArgs e){
            if(e.RowIndex<0||e.ColumnIndex<0||data==null)return;var cell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];if(string.IsNullOrEmpty(cell.ToolTipText))return;
            var runs=EffectText.Layout(data,Convert.ToString(e.FormattedValue),e.CellStyle.Font,e.CellBounds,Json.Str(cell.Tag));if(!runs.Any(r=>r.Kind.Length>0))return;
            e.Paint(e.ClipBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground|DataGridViewPaintParts.Border);var state=e.Graphics.Save();e.Graphics.SetClip(Rectangle.Intersect(e.CellBounds,e.ClipBounds));
            foreach(var run in runs){bool hover=effectRow==e.RowIndex&&effectColumn==e.ColumnIndex&&effectIndex==run.Index;Color color=hover?BadgePaint.Gold:(e.State&DataGridViewElementStates.Selected)!=0?e.CellStyle.SelectionForeColor:e.CellStyle.ForeColor;TextRenderer.DrawText(e.Graphics,run.Text,e.CellStyle.Font,run.Bounds,color,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping);}
            e.Graphics.Restore(state);e.Handled=true;
        }
        bool drawingRows;
        void ShowRows(){
            if(rebuilding||drawingRows||Disposing||IsDisposed)return;drawingRows=true;grid.Cursor=Cursors.Default;grid.SuspendLayout();try{ClearEffectHover();grid.Rows.Clear();displayedRows.Clear();cachedWebRows=null;var g=categories.SelectedItem as Group;if(g==null){return;}bool home=SelectedHero!=null&&SelectedHero.Id==0;
            if(g.Path.StartsWith(EquipmentRecommendations.Prefix)){ShowEquipment(g);return;}grid.Columns[0].Visible=true;grid.Columns[2].Visible=true;grid.Columns[2].HeaderText="统计学胜率";grid.Columns[3].HeaderText="统计学胜率提升";bool routes=g.Path==RouteRecommendations.GroupPath;emptyRoutes.Visible=routes&&g.Rows.Count==0;if(emptyRoutes.Visible)emptyRoutes.BringToFront();
            grid.Columns[0].HeaderText=routes?"出装路线":"排名";grid.Columns[1].HeaderText=routes?"对应最佳出门装":home?"英雄":g.Path=="champions/augmentCombinations"||g.Path.EndsWith("/summoners")?"组合":g.Path.EndsWith("/skills")?"加点顺序":g.Path.StartsWith("champions/augments/")?"海克斯":"名称";
            grid.Columns[0].AutoSizeMode=routes?DataGridViewAutoSizeColumnMode.Fill:DataGridViewAutoSizeColumnMode.None;grid.Columns[0].FillWeight=65;if(!routes)grid.Columns[0].Width=95;
            grid.Columns[1].FillWeight=routes?35:100;grid.Columns[0].DefaultCellStyle.WrapMode=routes?DataGridViewTriState.True:DataGridViewTriState.False;grid.Columns[1].DefaultCellStyle.WrapMode=routes?DataGridViewTriState.True:DataGridViewTriState.False;
            grid.RowTemplate.Height=routes?82:44;
            
            grid.Columns[3].Visible=!home;PresentRows(DataRows(g,home,routes));if(g.Path.StartsWith("champions/augments/")&&displayedRows.Count==0){emptyRoutes.Text="没有符合条件的海克斯";emptyRoutes.Visible=true;emptyRoutes.BringToFront();}if(g.Path=="champions/augmentCombinations"&&displayedRows.Count==0){emptyRoutes.Text="暂无值得推荐的海克斯组合";emptyRoutes.Visible=true;emptyRoutes.BringToFront();}ScheduleFitRows();}finally{drawingRows=false;grid.ResumeLayout();ScheduleWeb();}
        }
        void ShowEquipment(Group g){
            bool plans=g.Path.EndsWith("/plans"),synergy=g.Path.EndsWith("/synergy");
            emptyRoutes.Text=plans?"当前没有符合条件的核心出装":synergy?"当前没有数据充分支持的优秀海克斯联动":"当前方案没有该位置的装备推荐，请查看核心出装。";
            grid.Columns[0].Visible=true;
            grid.Columns[0].HeaderText=plans?"适用方案":synergy?"所需海克斯":"评级";
            grid.Columns[1].HeaderText=plans?"建议购买顺序":synergy?"专用购买顺序":"推荐装备";
            grid.Columns[2].Visible=!plans&&!synergy;grid.Columns[2].HeaderText=synergy?"联动统计学胜率":g.RateTitle;grid.Columns[3].Visible=!plans&&!synergy;grid.Columns[3].HeaderText=g.GainTitle;
            grid.Columns[0].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;grid.Columns[0].Width=180;grid.Columns[1].FillWeight=100;
            grid.Columns[0].DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.Columns[1].DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            grid.RowTemplate.Height=44;
            PresentRows(EquipmentRows(g,plans,synergy));emptyRoutes.Visible=displayedRows.Count==0;if(emptyRoutes.Visible)emptyRoutes.BringToFront();ScheduleFitRows();
        }
        bool fittingRows,fitScheduled;
        void ScheduleFitRows(){if(fitScheduled||Disposing||IsDisposed||!IsHandleCreated)return;fitScheduled=true;BeginInvoke((Action)(()=>{fitScheduled=false;if(!Disposing&&!IsDisposed&&!grid.Disposing&&!grid.IsDisposed)FitRows();}));}

        void FitRows(){if(Disposing||IsDisposed||grid.Disposing||grid.IsDisposed||drawingRows||fittingRows||grid.Rows.Count==0)return;int available=grid.ClientSize.Height-grid.ColumnHeadersHeight;int baseHeight=grid.RowTemplate.Height;if(available<baseHeight)return;int visible=available/baseHeight;fittingRows=true;try{int height=available/visible,remainder=available%visible;for(int i=0;i<grid.Rows.Count;i++){int target=height+(i%visible<remainder?1:0);if(grid.Rows[i].Height!=target)grid.Rows[i].Height=target;}}finally{fittingRows=false;}}
        void UpdateSuggestions(){
            searchTip.Hide(search);suggestions.Items.Clear();string q=search.Text.Trim();if(q.Length==0){suggestions.Hide();return;}foreach(var h in HeroAliases.Search(data.Heroes,q))suggestions.Items.Add(h);if(suggestions.Items.Count==0){suggestions.Hide();return;}suggestions.Location=PointToClient(search.Parent.PointToScreen(new Point(search.Left,search.Bottom)));suggestions.Height=Math.Min(200,suggestions.Items.Count*suggestions.ItemHeight+6);suggestions.Show();suggestions.BringToFront();
        }
        void OpenSuggestion(){var h=suggestions.SelectedItem as Hero;if(h==null)return;search.Clear();suggestions.Hide();heroes.SelectedItem=h;}
        void SearchHero(){
            string query=search.Text.Trim();if(query.Length==0)return;
            var matches=HeroAliases.Search(data.Heroes,query);
            if(matches.Count==1){search.Clear();heroes.SelectedItem=matches[0];searchTip.Hide(search);}
            else if(matches.Count>1)searchTip.Show("请选择下拉列表中的英雄",search,0,search.Height+4,4000);
            else searchTip.Show("未找到英雄，可输入名字、称号或外号",search,0,search.Height+4,4000);
        }
        void FollowHero(int id){if(id<=0||id==lastChampion)return;lastChampion=id;var hero=data.Heroes.FirstOrDefault(h=>h.Id==id);if(hero!=null)heroes.SelectedItem=hero;}
        internal static string ConnectionText(ClientStatus result,DataStore data){
            if(!result.Connected)return result.Message!=null&&result.Message.Contains("暂不可用")?"连接异常":"未检测到客户端";
            string phase;
            switch(result.Phase){case "None":case "Lobby":phase="大厅";break;case "Matchmaking":phase="匹配中";break;case "ReadyCheck":phase="等待确认";break;case "ChampSelect":phase="选人中";break;case "GameStart":phase="游戏启动";break;case "InProgress":phase="游戏中";break;case "EndOfGame":phase="对局结束";break;default:phase="已连接";break;}
            if(result.Champion>0&&(result.Phase=="ChampSelect"||result.Phase=="GameStart"||result.Phase=="InProgress"))phase+=" · "+ShortName(data.Lookup("champions",result.Champion));
            return phase;
        }
        void ApplyGameWindow(ClientStatus result){
            IntPtr gameHandle=IntPtr.Zero;foreach(var process in System.Diagnostics.Process.GetProcessesByName("League of Legends"))using(process){try{if(process.MainWindowHandle!=IntPtr.Zero){gameHandle=process.MainWindowHandle;break;}}catch{}}
            bool same=gameHandle==IntPtr.Zero?Screen.FromHandle(Handle).Primary:Screen.FromHandle(gameHandle).DeviceName==Screen.FromHandle(Handle).DeviceName;
            var decision=AutoMinimizeDecision.Next(gameWindow,result.Connected?result.Phase:"",Visible,WindowState,same,autoMinimize,false);gameWindow=decision.State;
            // SW_SHOWMINNOACTIVE keeps activation with the current foreground window.
            changingGameWindow=true;try{if(decision.Action=="minimize")ShowWindow(Handle,7);else if(decision.Action=="restore")RestoreGameWindow(decision.Restore);}finally{changingGameWindow=false;}
        }
        protected override void OnResize(EventArgs e){base.OnResize(e);if(!changingGameWindow&&gameWindow!=null&&gameWindow.Owned&&WindowState!=FormWindowState.Minimized)gameWindow=AutoMinimizeDecision.Next(gameWindow,"",Visible,WindowState,true,autoMinimize,true).State;}
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int SetWindowLong(IntPtr window,int index,int value);
        void RestoreGameWindow(FormWindowState previous){int style=GetWindowLong(Handle,-20);try{SetWindowLong(Handle,-20,style|0x08000000);ShowWindow(Handle,4);if(previous==FormWindowState.Maximized)ShowWindow(Handle,3);}finally{SetWindowLong(Handle,-20,style);}}
        async Task Poll(){if(polling)return;polling=true;try{var current=client;var result=await Task.Run(()=>current.Poll());if(IsDisposed||current!=client)return;int gamePid=0;foreach(var game in System.Diagnostics.Process.GetProcessesByName("League of Legends")){using(game){gamePid=game.Id;break;}}bool retain=!result.Connected&&clientStatus!=null&&clientStatus.Connected&&clientStatus.IsMayhem&&(clientStatus.Phase=="InProgress"||clientStatus.Phase=="GameStart")&&gamePid>0&&gamePid==activeGamePid&&DateTime.UtcNow-clientStatusTime<TimeSpan.FromSeconds(20);if(!retain){clientStatus=result;if(result.Connected)clientStatusTime=DateTime.UtcNow;activeGamePid=gamePid;}Text=AppVersion.ProductName+" · "+ConnectionText(result,data);if(result.IsMayhem&&result.Champion>0)FollowHero(result.Champion);else if(result.Connected&&(result.Phase=="Lobby"||result.Phase=="None"||result.Phase=="EndOfGame"))lastChampion=0;var shopData=data;string shopScope=Scope;await Task.Run(()=>{shopSession.Client=current;shopSession.Tick(shopData,result,shopScope);});ApplyGameWindow(result);SendWeb(new {type="shopStatus",kind=shopSession.DisplayKind,count=shopSession.ImportedCount,error=shopSession.Error});ScheduleWeb();}finally{polling=false;}}

    }
}







