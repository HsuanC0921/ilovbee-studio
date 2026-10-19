using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;

namespace iLovbeeStudio;

public sealed record SavedDraft(Dictionary<int,string> Baseline,Dictionary<int,string> Desired);

public sealed partial class MainWindow : Window {
    static readonly Brush Ink=Brush("#20242B"),Muted=Brush("#55606D"),Blue=Brush("#185ABD"),Surface=Brush("#F7F8FA"),Line=Brush("#D5DBE3");
    readonly KeyboardService service=new();
    readonly MappingEditor editor=new();
    readonly BatteryReader battery=new();
    TextBlock? pendingLabel;
    Button? stageButton,undoButton,redoButton;
    readonly List<KeyAction> shortcutKeys=KeyActions.All().Where(a=>a.Bytes[0]==0 && a.Bytes[1]==0 && a.Bytes[2]==0 && a.Bytes[3]!=0).DistinctBy(a=>Convert.ToHexString(a.Bytes)).ToList();
    readonly StackPanel body=new(){Spacing=20,Margin=new Thickness(32,24,32,24)};
    readonly Grid shell=new();
    readonly StackPanel sidebar=new(){Background=Brush("#EEF1F5")};
    readonly Dictionary<string,Button> navigationButtons=new();
    readonly Dictionary<string,(TextBlock Label,SymbolIcon Icon)> navigationItems=new();
    readonly TextBlock footer=T("就緒",12,Muted);
    readonly InfoBar info=new(){IsOpen=false,IsClosable=false,Severity=InfoBarSeverity.Informational,Message="準備讀取鍵盤……"};
    readonly TextBlock device=new(){Text="B87 · 讀取中",FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=Ink},powerLabel=T("",12,Muted);
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(30)};
    readonly Dictionary<int,byte[]> maps=new();
    readonly Dictionary<int,Button> buttons=new();
    readonly List<PhysicalKey> keys;
    readonly List<string> events=new();
    readonly HashSet<VirtualKey> pressed=new();
    readonly Dictionary<VirtualKey,long> released=new();
    readonly TextBox test=new(){AcceptsReturn=true,IsReadOnly=true,MinHeight=260,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Cascadia Mono"),PlaceholderText="點入這裡，再按實體鍵盤。"};
    readonly List<KeyAction> actions=KeyActions.All();
    ComboBox? actionBox;
    TextBlock? currentKey;
    Button? applyButton;
    PhysicalKey? selected;
    byte[]? lights;
    PowerInfo? power;
    int layer;
    string page="按鍵重設";
    bool busy,closing,resumed;
    DateTimeOffset lastPower;
    DispatcherTimer? lightTimer;
    public MainWindow() {
        var arguments=Environment.GetCommandLineArgs();
        Title="iLovbee Studio";AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"assets","app.ico"));
        ExtendsContentIntoTitleBar=true;
        keys=JsonSerializer.Deserialize<List<PhysicalKey>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"keyboard.json")))??new();
        selected=keys.FirstOrDefault(k=>k.Id==88);
        var root=new Grid{RequestedTheme=ElementTheme.Light,Background=Surface};Content=root;
        root.RowDefinitions.Add(new(){Height=new GridLength(48)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var titleBar=new Grid{Height=48,Background=Surface,Padding=new Thickness(20,0,150,0)};
        var titleLine=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center};
        var mark=new Microsoft.UI.Xaml.Controls.Image{Width=26,Height=26,Source=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"assets","logo.png")))};
        titleLine.Children.Add(mark);titleLine.Children.Add(new TextBlock{Text="iLovbee Studio",FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        titleBar.Children.Add(titleLine);
        root.Children.Add(titleBar);SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption=TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor=ColorHelper.FromArgb(255,247,248,250);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor=ColorHelper.FromArgb(255,247,248,250);
        AppWindow.TitleBar.ButtonForegroundColor=ColorHelper.FromArgb(255,32,36,43);
        AppWindow.TitleBar.ButtonInactiveForegroundColor=ColorHelper.FromArgb(255,85,96,109);
        AppWindow.TitleBar.ButtonHoverBackgroundColor=ColorHelper.FromArgb(255,227,231,237);
        titleBar.SizeChanged+=(_,_)=>titleBar.Padding=new Thickness(20,0,AppWindow.TitleBar.RightInset/(titleBar.XamlRoot?.RasterizationScale??1)+16,0);
        var brand=new StackPanel{Margin=new Thickness(16,12,12,24),Spacing=12};var brandText=new StackPanel{Spacing=2};brandText.Children.Add(new TextBlock{Text="B87",FontSize=22,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=Ink});brandText.Children.Add(T("鍵盤設定",12,Muted));brand.Children.Add(brandText);sidebar.Children.Add(brand);
        var names=new[]{("按鍵重設",Symbol.Keyboard),("燈效",Symbol.Highlight),("裝置狀態",Symbol.Link),("按鍵測試",Symbol.TouchPointer),("設定檔",Symbol.Save)};
        foreach(var n in names){
            var icon=new SymbolIcon(n.Item2){Foreground=Muted,Width=18,Height=18};var label=T(n.Item1,14,Ink);
            var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center};content.Children.Add(icon);content.Children.Add(label);
            var button=new Button{Content=content,Tag=n.Item1,Height=44,Padding=new Thickness(12,8,8,8),Margin=new Thickness(8,2,8,2),Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(6),HorizontalContentAlignment=HorizontalAlignment.Left,HorizontalAlignment=HorizontalAlignment.Stretch};
            AutomationProperties.SetName(button,n.Item1);ToolTipService.SetToolTip(button,n.Item1);button.Click+=(_,_)=>Navigate((string)button.Tag);
            sidebar.Children.Add(button);navigationButtons[n.Item1]=button;navigationItems[n.Item1]=(label,icon);
        }
        shell.ColumnDefinitions.Add(new(){Width=new GridLength(210)});shell.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetColumn(sidebar,0);shell.Children.Add(sidebar);
        var panel=new Grid();panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});panel.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{Padding=new Thickness(32,18,32,18),BorderBrush=Line,BorderThickness=new Thickness(0,0,0,1)};header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var state=new StackPanel{Spacing=4};state.Children.Add(device);state.Children.Add(powerLabel);header.Children.Add(state);
        var refresh=B("重新讀取",async()=>await Refresh());Grid.SetColumn(refresh,1);refresh.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(refresh);panel.Children.Add(header);
        var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled};Grid.SetRow(scroll,1);panel.Children.Add(scroll);
        var bottom=new StackPanel{Spacing=8,Margin=new Thickness(32,8,32,16)};bottom.Children.Add(info);bottom.Children.Add(footer);Grid.SetRow(bottom,2);panel.Children.Add(bottom);
        Grid.SetColumn(panel,1);shell.Children.Add(panel);Grid.SetRow(shell,1);root.Children.Add(shell);
        timer.Tick+=async(_,_)=>{if(!busy && !closing)await Poll();};root.Loaded+=async(_,_)=>{
            try{
                Navigate(page);
                var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
                double scale=root.XamlRoot.RasterizationScale;
                int width=Math.Min((int)(1320*scale),area.Width-80),height=Math.Min((int)(850*scale),area.Height-80);
                AppWindow.MoveAndResize(new RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
                await Refresh();
                timer.Start();
                if(arguments.Length==3 && arguments[1]=="--ui-check")await ValidateUi(arguments[2],root);
            }catch(Exception ex){
                Directory.CreateDirectory(Store.Root);
                File.WriteAllText(Path.Combine(Store.Root,"last-error.txt"),ex.ToString());
                info.Title="啟動未完成";info.Severity=InfoBarSeverity.Error;
                info.Message="程式已開啟，但初始化未完成。錯誤記錄已儲存至本機設定資料夾。";info.IsOpen=true;
            }
        };Closed+=(_,_)=>{closing=true;timer.Stop();lightTimer?.Stop();};
        test.KeyDown+=TestDown;test.KeyUp+=TestUp;test.LostFocus+=(_,_)=>pressed.Clear();
    }
    static SolidColorBrush Brush(string hex)=>new(ColorHelper.FromArgb(255,Convert.ToByte(hex.Substring(1,2),16),Convert.ToByte(hex.Substring(3,2),16),Convert.ToByte(hex.Substring(5,2),16)));
    static TextBlock T(string text,double size=14,Brush? color=null)=>new(){Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,LineHeight=size*1.6};
    static Button B(string name,Action action){var b=new Button{Content=name,Padding=new Thickness(16,9,16,9)};b.Click+=(_,_)=>action();return b;}
    void Heading(string title,string detail){body.Children.Clear();body.Spacing=20;body.Margin=new Thickness(32,24,32,24);body.Children.Add(new TextBlock{Text=title,FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});body.Children.Add(T(detail,14,Muted));}
    void Rule()=>body.Children.Add(new Border{Height=1,Background=Line,Margin=new Thickness(0,8,0,8)});
    void Message(string message,bool error=false){footer.Text=message;info.Message=error?message:"";info.Severity=InfoBarSeverity.Error;info.IsOpen=error;}
    void Log(string message){events.Add($"{DateTime.Now:HH:mm:ss}  {message}");if(events.Count>100)events.RemoveAt(0);}
    async Task Run(string message,Func<Task> action){
        if(busy){Message("前一個操作尚未完成，請稍候。");return;}busy=true;UpdateApply();Message(message);
        try{await action();}catch(Exception e){Message(e.Message,true);Log(e.Message);}finally{busy=false;UpdateApply();}
    }
    void UpdateDevice(){device.Text=service.Current!=null?"B87 · USB 已連線":"B87 · USB 未連線";device.Foreground=service.Current!=null?Blue:Muted;powerLabel.Text=battery.Latest?.Percent is int percent?$"電量 {percent}% · Windows 最近回報":"電量尚未取得";}
    async Task Refresh(){
        if(editor.ChangeCount>0){Message("請先套用或取消變更，再重新讀取。");return;}
        await Run("正在讀取鍵盤……",async()=>{
        await Task.Run(()=>service.Scan());await battery.ReadAsync();
        if(service.Current==null){maps.Clear();lights=null;power=null;UpdateDevice();Navigate(page);Message("找不到相容 USB 設定介面。請連接 USB 並切至有線模式。");return;}
        var profile=await Task.Run(()=>service.Capture());maps.Clear();foreach(var m in profile.Keymaps)maps[m.Key]=Convert.FromHexString(m.Value);lights=Convert.FromHexString(profile.Lighting);editor.Load(maps);if(!resumed){ResumeDraft();resumed=true;}
        power=await Task.Run(()=>service.ReadPower());lastPower=DateTimeOffset.Now;UpdateDevice();Navigate(page);Message("鍵盤設定已同步。");
    });
    }
    async Task Poll()=>await Run("正在檢查裝置連線……",async()=>{
        bool connected=service.Current!=null;await Task.Run(()=>service.Scan());
        if(service.Current==null){maps.Clear();lights=null;power=null;if(connected){Log("USB 設定介面離線");Navigate(page);}}
        else if(!connected){Log("USB 設定介面重新連線；請重新讀取設定。");}
        if((DateTimeOffset.Now-lastPower).TotalSeconds>=60){await battery.ReadAsync();if(service.Current!=null)power=await Task.Run(()=>service.ReadPower());lastPower=DateTimeOffset.Now;if(page=="裝置狀態")PowerPage();}
        UpdateDevice();
        Message(service.Current!=null?"鍵盤已連線。":"鍵盤未連線，請使用 USB 連接以編輯設定。");
    });
    void UpdateNavigation(){
        foreach(var pair in navigationButtons){bool active=pair.Key==page;pair.Value.Background=active?Brush("#DCEAFF"):new SolidColorBrush(Colors.Transparent);pair.Value.Foreground=active?Blue:Ink;var visual=navigationItems[pair.Key];visual.Label.Foreground=active?Blue:Ink;visual.Label.FontWeight=active?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal;visual.Icon.Foreground=active?Blue:Muted;}
    }
    void Navigate(string name){lightTimer?.Stop();page=name;switch(name){case "按鍵重設":RemapPage();break;case "燈效":LightingPage();break;case "裝置狀態":PowerPage();break;case "按鍵測試":TestPage();break;case "設定檔":ProfilesPage();break;default:ProfilesPage();break;}UpdateNavigation();}
    void RemapPage(){
        Heading("按鍵重設","自訂按鍵、媒體控制與組合快捷鍵。");
        body.Children.RemoveAt(1);body.Spacing=12;body.Margin=new Thickness(32,16,32,16);
        var toolbar=new Grid{ColumnSpacing=12};toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});toolbar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var layers=new ComboBox{ItemsSource=new[]{"預設層","FN1 層","FN2 層"},SelectedIndex=layer,Width=150};AutomationProperties.SetName(layers,"編輯層");layers.SelectionChanged+=(_,_)=>{layer=layers.SelectedIndex;RemapPage();};toolbar.Children.Add(layers);
        var history=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};Grid.SetColumn(history,1);
        undoButton=B("復原",()=>{editor.Undo();DraftChanged();});redoButton=B("重做",()=>{editor.Redo();DraftChanged();});history.Children.Add(undoButton);history.Children.Add(redoButton);toolbar.Children.Add(history);
        applyButton=B("套用變更",async()=>await ApplyDraft());applyButton.Style=(Style)Application.Current.Resources["AccentButtonStyle"];Grid.SetColumn(applyButton,2);toolbar.Children.Add(applyButton);body.Children.Add(toolbar);
        pendingLabel=T("",12,Muted);body.Children.Add(pendingLabel);
        var canvas=new Canvas{Width=800,Height=310};canvas.Children.Add(new Border{Width=800,Height=305,Background=Brush("#E3E7ED"),CornerRadius=new CornerRadius(12)});buttons.Clear();
        foreach(var k in keys.Where(k=>k.Id is not (16 or 89))){
            string label=k.Name switch{"PrtSc"=>"Prt","SrcLock"=>"Scr","CapsLock"=>"Caps","Backspace"=>"Back","LShift" or "RShift"=>"Shift","LControl" or "RControl"=>"Ctrl",_=>k.Name};
            var button=B(label,()=>{selected=k;UpdateSelection();});button.Width=k.Width+8;button.Height=k.Height+6;button.MinHeight=0;button.MinWidth=0;button.Padding=new Thickness(0);button.FontSize=label.Length>4 && k.Width<40?10:12;button.CornerRadius=new CornerRadius(5);button.Tag=k;
            AutomationProperties.SetName(button,k.Id==88?"小算盤鍵":k.Name);Canvas.SetLeft(button,k.X-4);Canvas.SetTop(button,k.Y-15);canvas.Children.Add(button);buttons[k.Id]=button;
        }
        var knob=B("",()=>Navigate("按鍵測試"));knob.Width=36;knob.Height=36;knob.MinHeight=0;knob.MinWidth=0;knob.Padding=new Thickness(0);knob.CornerRadius=new CornerRadius(18);knob.Background=Brush("#465365");knob.Content=new Microsoft.UI.Xaml.Shapes.Ellipse{Width=24,Height=24,Stroke=new SolidColorBrush(Colors.White),StrokeThickness=1.5};
        AutomationProperties.SetName(knob,"旋鈕，開啟按鍵測試");ToolTipService.SetToolTip(knob,"測試旋鈕按鍵");Canvas.SetLeft(knob,736);Canvas.SetTop(knob,20);canvas.Children.Add(knob);
        body.Children.Add(new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,MaxHeight=280});
        var strip=new Grid{Padding=new Thickness(16),Background=new SolidColorBrush(Colors.White),CornerRadius=new CornerRadius(8),ColumnSpacing=24};strip.ColumnDefinitions.Add(new(){Width=new GridLength(.8,GridUnitType.Star)});strip.ColumnDefinitions.Add(new(){Width=new GridLength(1.2,GridUnitType.Star)});
        var old=new StackPanel{Spacing=12};old.Children.Add(T("目前指派",12,Muted));currentKey=T("",16);old.Children.Add(currentKey);
        old.Children.Add(B("取消此鍵變更",()=>{if(selected!=null && editor.Draft.ContainsKey(layer)){editor.Revert(layer,selected.Slot);DraftChanged();}}));
        strip.Children.Add(old);
        var next=new StackPanel{Spacing=12};Grid.SetColumn(next,1);
        var tabs=new Pivot{Margin=new Thickness(-12,-8,-12,0)};
        var standard=new StackPanel{Spacing=8};
        var search=new AutoSuggestBox{PlaceholderText="搜尋功能，例如 F13、截圖、靜音",QueryIcon=new SymbolIcon(Symbol.Find)};AutomationProperties.SetName(search,"搜尋按鍵功能");
        actionBox=new ComboBox{ItemsSource=actions,DisplayMemberPath="Name",HorizontalAlignment=HorizontalAlignment.Stretch,PlaceholderText="選擇功能",MaxDropDownHeight=320};AutomationProperties.SetName(actionBox,"指派功能");
        search.TextChanged+=(_,_)=>{actionBox.ItemsSource=actions.Where(a=>a.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();UpdateApply();};
        actionBox.SelectionChanged+=(_,_)=>UpdateApply();standard.Children.Add(search);standard.Children.Add(actionBox);
        stageButton=B("加入變更",()=>{if(actionBox.SelectedItem is KeyAction action)Stage(action.Bytes);});standard.Children.Add(stageButton);
        tabs.Items.Add(new PivotItem{Header="功能",Content=standard});
        var shortcut=new StackPanel{Spacing=12};
        var modifiers=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        CheckBox ctrl=new(){Content="Ctrl"},shift=new(){Content="Shift"},alt=new(){Content="Alt"},win=new(){Content="Win"};
        modifiers.Children.Add(ctrl);modifiers.Children.Add(shift);modifiers.Children.Add(alt);modifiers.Children.Add(win);shortcut.Children.Add(modifiers);
        var shortcutKey=new ComboBox{ItemsSource=shortcutKeys,DisplayMemberPath="Name",PlaceholderText="選擇主鍵",HorizontalAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetName(shortcutKey,"組合鍵主鍵");shortcut.Children.Add(shortcutKey);
        shortcut.Children.Add(B("加入組合鍵",()=>{if(shortcutKey.SelectedItem is not KeyAction key){Message("請選擇組合鍵的主鍵。");return;}byte mod=(byte)((ctrl.IsChecked==true?1:0)|(shift.IsChecked==true?2:0)|(alt.IsChecked==true?4:0)|(win.IsChecked==true?8:0));Stage(KeyActions.Code(key.Bytes[3],mod));}));
        tabs.Items.Add(new PivotItem{Header="組合鍵",Content=shortcut});next.Children.Add(tabs);strip.Children.Add(next);body.Children.Add(strip);UpdateSelection();
        var changes=new Expander{Header="變更清單",HorizontalAlignment=HorizontalAlignment.Stretch};
        var changeBody=new StackPanel{Spacing=12};foreach(var change in editor.Changes())changeBody.Children.Add(T($"{LayerName(change.Layer)} · {KeyName(change.Slot)}\n{change.Before} → {change.After}",12));
        var actionsRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};actionsRow.Children.Add(B("取消全部變更",()=>{editor.Discard();DraftChanged();}));actionsRow.Children.Add(B("匯出草稿",async()=>await ExportDraft()));changeBody.Children.Add(actionsRow);changes.Content=changeBody;body.Children.Add(changes);
    }
    static string LayerName(int index)=>index==0?"預設層":"FN"+index+" 層";
    string KeyName(int slot)=>keys.FirstOrDefault(k=>k.Slot==slot)?.Name??"按鍵 "+slot;
    void Stage(byte[] code){
        if(selected==null || !maps.ContainsKey(layer) || service.Current==null || busy)return;
        try{editor.Assign(layer,selected.Slot,code);DraftChanged();Message("已加入變更，按「套用變更」儲存至鍵盤。");}catch(Exception e){Message(e.Message,true);}
    }
    void SaveDraft(){
        if(editor.Baseline.Count!=3)return;
        Store.Write("drafts","pending.json",new SavedDraft(editor.Baseline.ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value)),editor.Draft.ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value))));
    }
    void ResumeDraft(){
        string path=Path.Combine(Store.Root,"drafts","pending.json");if(!File.Exists(path))return;
        try{
            if(new FileInfo(path).Length>16384)throw new InvalidDataException("草稿檔案過大。");
            var saved=JsonSerializer.Deserialize<SavedDraft>(File.ReadAllText(path))??throw new InvalidDataException("無效草稿。");
            var before=saved.Baseline.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));var desired=saved.Desired.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));MappingEditor.Validate(before);MappingEditor.Validate(desired);
            if(before.Any(p=>!maps[p.Key].SequenceEqual(p.Value))){Log("本機草稿與鍵盤設定不同，已保留原檔。");return;}editor.Resume(desired);
        }catch(Exception e)when(e is IOException or ArgumentException or JsonException or FormatException or NullReferenceException){Log("無法載入草稿："+e.Message);}
    }
    void DraftChanged(){try{SaveDraft();}catch(IOException e){Message("草稿無法保存："+e.Message,true);}RemapPage();}
    void UpdateApply(){
        if(applyButton!=null){applyButton.IsEnabled=!busy && service.Current!=null && editor.ChangeCount>0;applyButton.Content=editor.ChangeCount>0?$"套用變更（{editor.ChangeCount}）":"套用變更";}
        if(stageButton!=null)stageButton.IsEnabled=!busy && service.Current!=null && selected!=null && selected.Name!="FN" && actionBox?.SelectedItem is KeyAction && maps.ContainsKey(layer);
        if(undoButton!=null)undoButton.IsEnabled=!busy && editor.CanUndo;if(redoButton!=null)redoButton.IsEnabled=!busy && editor.CanRedo;
        if(pendingLabel!=null)pendingLabel.Text=editor.ChangeCount==0?"所有變更已儲存":$"{editor.ChangeCount} 顆按鍵尚未儲存 · 淺藍色為待套用變更";
    }
    void UpdateSelection(){
        if(selected==null || currentKey==null)return;
        foreach(var pair in buttons){var button=pair.Value;var key=(PhysicalKey)button.Tag;bool active=key.Id==selected.Id,dirty=editor.Draft.ContainsKey(layer) && editor.Changed(layer,key.Slot);
            button.Background=active?Blue:dirty?Brush("#DCEAFF"):new SolidColorBrush(Colors.White);button.Foreground=active?new SolidColorBrush(Colors.White):Ink;button.BorderBrush=active||dirty?Blue:Line;button.BorderThickness=new Thickness(dirty?2:1);
            if(editor.Draft.TryGetValue(layer,out var map)){var code=map.AsSpan(key.Slot*4,4).ToArray();ToolTipService.SetToolTip(button,$"{key.Name} → {KeyActions.Decode(code)}"+(dirty?"（未儲存）":""));if(key.Id==88)button.Content=code.SequenceEqual(KeyActions.Code(7,8))?"桌面":code.SequenceEqual(KeyActions.Code(0x68))?"F13":"Cal";}
        }
        string name=selected.Id==88?"小算盤鍵":selected.Name;
        if(editor.Draft.TryGetValue(layer,out var draft)){
            var code=draft.AsSpan(selected.Slot*4,4).ToArray();string before=KeyActions.Decode(editor.Baseline[layer].AsSpan(selected.Slot*4,4).ToArray());
            currentKey.Text=name+"\n"+before+(editor.Changed(layer,selected.Slot)?"\n待套用："+KeyActions.Decode(code):"");
            if(actionBox!=null)actionBox.SelectedItem=actions.FirstOrDefault(a=>a.Bytes.SequenceEqual(code));
        }else{currentKey.Text=name+"\n尚未讀取裝置";if(actionBox!=null)actionBox.SelectedIndex=-1;}
        if(actionBox!=null)actionBox.IsEnabled=selected.Name!="FN" && maps.ContainsKey(layer);UpdateApply();
    }
    async Task ApplyDraft()=>await Run("正在儲存按鍵設定……",async()=>{
        var before=MappingEditor.Clone(editor.Baseline);var after=MappingEditor.Clone(editor.Draft);int count=editor.ChangeCount;
        await Task.Run(()=>service.ApplyMappings(before,after));maps.Clear();foreach(var map in after)maps[map.Key]=map.Value;editor.Load(maps);SaveDraft();RemapPage();Log($"已套用 {count} 顆按鍵");Message($"已儲存 {count} 顆按鍵的變更。");
    });
    async Task ExportDraft()=>await Run("正在匯出草稿……",async()=>{
        if(lights==null || editor.Draft.Count!=3)throw new InvalidOperationException("請先讀取鍵盤。");
        var profile=new DeviceProfile(1,"258A:0199","2010",DateTimeOffset.Now,editor.Draft.ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value)),Convert.ToHexString(lights));
        var picker=new FileSavePicker{SuggestedFileName="B87-草稿-"+DateTime.Now.ToString("yyyyMMdd-HHmm")};picker.FileTypeChoices.Add("iLovbee 設定檔",new List<string>{".json"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSaveFileAsync();if(file!=null){await File.WriteAllTextAsync(file.Path,JsonSerializer.Serialize(profile,Store.Json),Encoding.UTF8);Message("草稿已匯出，尚未寫入鍵盤。");}
    });
    static Border Card(UIElement child)=>new(){Background=new SolidColorBrush(Colors.White),BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(20),Child=child};
    static Border Chip(string text)=>new(){Background=Brush("#E3E7ED"),CornerRadius=new CornerRadius(5),Padding=new Thickness(8,3,8,3),Child=new TextBlock{Text=text,FontSize=12,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=Ink}};
    static TextBlock Sub(string text)=>new(){Text=text,FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=Ink};
    static StackPanel Row(params UIElement[] items){var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center};foreach(var item in items)row.Children.Add(item);return row;}
    void PowerPage(){
        Heading("裝置狀態","檢視鍵盤連線與電量資訊。");
        bool usb=service.Current!=null;
        var link=new StackPanel{Spacing=6};link.Children.Add(Row(new Microsoft.UI.Xaml.Shapes.Ellipse{Width=10,Height=10,Fill=usb?Blue:Muted},T(usb?"USB 已連線":"USB 未連線",20)));
        link.Children.Add(T("使用 USB 連接鍵盤，即可編輯按鍵、燈效與設定檔。",14,Muted));body.Children.Add(Card(link));
        var power=new StackPanel{Spacing=12};power.Children.Add(T(battery.Latest?.Percent is int percent?$"電量 {percent}%":"電量尚未取得",24));
        if(battery.Latest?.Percent is int value)power.Children.Add(new ProgressBar{Value=value,Minimum=0,Maximum=100,Width=320,HorizontalAlignment=HorizontalAlignment.Left});
        power.Children.Add(T(battery.Latest?.Percent!=null?$"Windows 最近回報：{battery.Latest.ReadAt.ToLocalTime():MM/dd HH:mm}":battery.Latest?.State??"請連接藍牙以讀取電量。",12,Muted));
        power.Children.Add(Row(
            B("更新電量",async()=>await Run("正在讀取電量……",async()=>{await battery.ReadAsync();UpdateDevice();PowerPage();Message(battery.Latest?.Percent!=null?"電量已更新。":battery.Latest?.State??"未取得電量。");})),
            B("開啟 Windows 藍牙設定",()=>Process.Start(new ProcessStartInfo("ms-settings:bluetooth"){UseShellExecute=true})),
            B("開啟系統匣小工具",()=>{string path=Path.Combine(AppContext.BaseDirectory,"iLovbeeWidget.exe");if(File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});else Message("找不到小工具，請使用完整發佈資料夾。",true);})));
        body.Children.Add(Card(power));
        var diag=new StackPanel{Spacing=12};diag.Children.Add(Sub("裝置診斷"));diag.Children.Add(T("將裝置識別、連線紀錄與電量回報儲存至本機檔案。",14,Muted));
        diag.Children.Add(B("匯出診斷",async()=>await Run("正在匯出診斷……",async()=>{var devices=await Task.Run(()=>service.Scan());string file=Store.Write("diagnostics",DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json",new{Version="0.3.6",Devices=devices,Power=this.power,Battery=battery.Latest,BatteryDiagnostic=battery.LastDiagnostic,Events=events,FirmwareModified=false});Message("已儲存："+file);})));
        body.Children.Add(Card(diag));
    }
    void TestPage(){
        Heading("按鍵測試","檢視按下、放開及重複輸入事件。");
        body.Children.Add(B("開始測試",()=>{test.Focus(FocusState.Programmatic);Message("測試中。可按一下旋鈕，確認 Windows 收到的按鍵。");}));body.Children.Add(test);
        body.Children.Add(B("清除紀錄",()=>{test.Text="";pressed.Clear();released.Clear();}));body.Children.Add(T("測試期間僅記錄此視窗接收的按鍵。按 Tab 離開測試區。",14,Muted));
    }
    void TestDown(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Tab)return;long now=Stopwatch.GetTimestamp();string note=pressed.Contains(e.Key)?" · 系統重複":released.TryGetValue(e.Key,out long before) && Stopwatch.GetElapsedTime(before,now).TotalMilliseconds<30?" · 短間隔，待檢查":"";pressed.Add(e.Key);AppendTest($"按下 {e.Key}{note}");e.Handled=true;}
    void TestUp(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Tab)return;pressed.Remove(e.Key);released[e.Key]=Stopwatch.GetTimestamp();AppendTest($"放開 {e.Key}");e.Handled=true;}
    void AppendTest(string value){test.Text=string.Join(Environment.NewLine,(test.Text+$"{DateTime.Now:HH:mm:ss.fff}　{value}"+Environment.NewLine).Split(Environment.NewLine).TakeLast(150));test.SelectionStart=test.Text.Length;}
    void ProfilesPage(){
        Heading("設定檔與備份","匯出、還原與管理鍵盤設定。");
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};row.Children.Add(B("匯出完整設定",async()=>await ExportProfile()));row.Children.Add(B("從檔案還原",async()=>await RestoreProfile()));body.Children.Add(row);
        body.Children.Add(B("立即建立完整備份",async()=>await Run("正在備份……",async()=>{string path=await Task.Run(()=>service.Backup("manual"));Message("已儲存："+path);ProfilesPage();})));Rule();body.Children.Add(T("自動備份",20));
        string folder=Path.Combine(Store.Root,"backups");var files=Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").OrderDescending().Select(Path.GetFileName).ToArray():Array.Empty<string>();body.Children.Add(new ListView{ItemsSource=files,MaxHeight=260,SelectionMode=ListViewSelectionMode.None});
        body.Children.Add(B("開啟備份資料夾",()=>{Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}));body.Children.Add(T("變更設定前會自動建立備份，檔案儲存於本機。",12,Muted));
    }
    async Task ExportProfile()=>await Run("正在讀取完整設定……",async()=>{
        var profile=await Task.Run(()=>service.Capture());var picker=new FileSavePicker{SuggestedFileName="B87-"+DateTime.Now.ToString("yyyyMMdd-HHmm")};picker.FileTypeChoices.Add("iLovbee 設定檔",new List<string>{".json"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSaveFileAsync();if(file==null){Message("已取消匯出。");return;}await File.WriteAllTextAsync(file.Path,JsonSerializer.Serialize(profile,Store.Json),Encoding.UTF8);Message("設定檔已匯出。");
    });
    async Task RestoreProfile(){
        if(editor.ChangeCount>0){Message("請先套用或取消按鍵變更，再還原設定檔。");return;}
        await Run("請選擇設定檔……",async()=>{
        var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null){Message("已取消還原。");return;}var profile=Store.Load(file.Path);
        var confirm=new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="還原完整設定？",Content="將覆寫三層映射與燈效。會先建立目前設定的完整備份，再讀回核對。",PrimaryButtonText="還原至鍵盤",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close};
        if(await confirm.ShowAsync()!=ContentDialogResult.Primary){Message("已取消還原。");return;}await Task.Run(()=>service.Restore(profile));foreach(var m in profile.Keymaps)maps[m.Key]=Convert.FromHexString(m.Value);lights=Convert.FromHexString(profile.Lighting);editor.Load(maps);ProfilesPage();Message("完整設定已還原並讀回核對。");
    });
    }
}
