using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace iLovbeeStudio;
public sealed record LightLayout(string Name,string Frame);
public static class LightLayoutLibrary {
    public const int Max=20,MaxName=20;
    static string FilePath=>Path.Combine(Store.Root,"customlights.json");
    public static List<LightLayout> Items{get;}=Load();
    static List<LightLayout> Load(){try{return (JsonSerializer.Deserialize<List<LightLayout>>(File.ReadAllText(FilePath),Store.Json)??new()).Where(l=>l.Frame?.Length==LedFrame.Bytes*2).ToList();}catch{return new();}}
    public static void Save()=>Store.Write("","customlights.json",Items);
    public static void Put(string name,byte[] frame){
        name=name.Trim();if(name.Length is 0 or >MaxName)throw new ArgumentException($"名稱需為 1 到 {MaxName} 個字。");
        int i=Items.FindIndex(l=>l.Name==name);var item=new LightLayout(name,Convert.ToHexString(frame));
        if(i>=0)Items[i]=item;else if(Items.Count>=Max)throw new ArgumentException($"最多 {Max} 組。");else Items.Add(item);Save();
    }
}
public sealed partial class MainWindow {
    byte[] customFrame=LightLayoutLibrary.Items.Count>0?Convert.FromHexString(LightLayoutLibrary.Items[0].Frame):new byte[LedFrame.Bytes];
    Color paint=Color.FromArgb(255,255,64,0);
    double musicGain=40;int musicStyle;
    /// <summary>即時燈效由常駐小工具負責（它在背景持續送出畫面）；這裡只寫入 live.ini 並確保小工具在執行。</summary>
    void SetLive(string mode){
        var c=LiveConfig.Load();c.Mode=mode;c.Style=musicStyle;c.Gain=musicGain;c.Frame=(byte[])customFrame.Clone();c.Save();
        if(mode!="off")EnsureWidget();
    }
    static void EnsureWidget(){
        if(System.Diagnostics.Process.GetProcessesByName("iLovbeeWidget").Length>0)return;
        string path=Path.Combine(AppContext.BaseDirectory,"iLovbeeWidget.exe");
        if(File.Exists(path))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});else throw new FileNotFoundException("找不到常駐小工具 iLovbeeWidget.exe。");
    }
    void StartLive(string mode){
        try{SetLive(mode);Message(mode=="music"?"音樂律動已交給常駐小工具執行。":"自訂燈光已交給常駐小工具執行。");}catch(IOException e){Message(e.Message,true);}
        DispatcherQueue.TryEnqueue(CustomLightsPage);
    }
    void CustomLightsPage(){
        Heading("自訂燈光","逐鍵設定顏色，或讓燈光跟著電腦播放的聲音律動。由本程式即時送出，程式開啟期間有效。");
        var cfg=LiveConfig.Load();musicStyle=cfg.Style;musicGain=cfg.Gain;
        bool running=System.Diagnostics.Process.GetProcessesByName("iLovbeeWidget").Length>0;
        var status=new StackPanel{Spacing=12};status.Children.Add(Sub("目前狀態"));
        status.Children.Add(T(cfg.Mode=="off"?"未執行":!running?"設定為"+(cfg.Mode=="music"?"音樂律動":"自訂燈光")+"，但常駐小工具未執行":cfg.Mode=="music"?"音樂律動執行中 · "+LedFrame.MusicStyles[cfg.Style]:"自訂燈光執行中",20,cfg.Mode=="off"?Muted:Blue));
        status.Children.Add(T("燈光由系統匣的常駐小工具持續送出，關閉這個視窗也會繼續；從系統匣選單也能切換。",12,Muted));
        status.Children.Add(Row(B("套用自訂燈光",()=>StartLive("custom")),B("開始音樂律動",()=>StartLive("music")),B("停止並恢復原本燈效",()=>StartLive("off"))));
        var gain=new Slider{Minimum=5,Maximum=200,Value=musicGain,Width=260,StepFrequency=5};AutomationProperties.SetName(gain,"音樂靈敏度");
        gain.ValueChanged+=(_,e)=>{musicGain=e.NewValue;if(LiveConfig.Load().Mode!="off")SetLive(LiveConfig.Load().Mode);};
        status.Children.Add(Row(T("音樂靈敏度",14),gain));
        var style=new ComboBox{ItemsSource=LedFrame.MusicStyles,SelectedIndex=musicStyle,Width=160};AutomationProperties.SetName(style,"音樂模式");
        style.SelectionChanged+=(_,_)=>{if(style.SelectedIndex>=0 && style.SelectedIndex!=musicStyle){musicStyle=style.SelectedIndex;var m=LiveConfig.Load().Mode;if(m=="music")SetLive(m);}};
        status.Children.Add(Row(T("音樂模式",14),style));body.Children.Add(Card(status));

        var editorCard=new StackPanel{Spacing=12};editorCard.Children.Add(Sub("逐鍵顏色"));
        editorCard.Children.Add(T("先選顏色，再點按鍵上色。燈光執行中時，變更會立即顯示在鍵盤上。",12,Muted));
        var canvas=new Canvas{Width=800,Height=300};canvas.Children.Add(new Border{Width=800,Height=300,Background=Brush("#252A32"),CornerRadius=new CornerRadius(12)});
        var caps=new Dictionary<int,Button>();
        void Push(){if(LiveConfig.Load().Mode=="custom")SetLive("custom");}
        void Paint(PhysicalKey k){var b=caps[k.Id];int s=k.Slot*3;b.Background=new SolidColorBrush(Color.FromArgb(255,customFrame[s],customFrame[s+1],customFrame[s+2]));}
        foreach(var k in keys.Where(k=>k.Id is not (16 or 89))){
            var key=k;var b=new Button{Width=k.Width+8,Height=k.Height+6,MinWidth=0,MinHeight=0,Padding=new Thickness(0),CornerRadius=new CornerRadius(5),BorderBrush=Brush("#5A6270"),BorderThickness=new Thickness(1),Content=new TextBlock{Text=k.Name.Length>5?k.Name[..4]:k.Name,FontSize=9,Foreground=new SolidColorBrush(Colors.White)}};
            AutomationProperties.SetName(b,k.Name);Canvas.SetLeft(b,k.X-4);Canvas.SetTop(b,k.Y-15);canvas.Children.Add(b);caps[k.Id]=b;
            b.Click+=(_,_)=>{LedFrame.Set(customFrame,key.Slot,paint.R,paint.G,paint.B);Paint(key);Push();};
        }
        foreach(var k in keys.Where(k=>caps.ContainsKey(k.Id)))Paint(k);
        var picker=new ColorPicker{Color=paint,IsAlphaEnabled=false,IsMoreButtonVisible=false,IsColorChannelTextInputVisible=false,IsHexInputVisible=true,ColorSpectrumShape=ColorSpectrumShape.Ring,Width=260};
        picker.ColorChanged+=(_,e)=>paint=e.NewColor;
        void Fill(Func<PhysicalKey,(byte,byte,byte)> pick){foreach(var k in keys.Where(k=>caps.ContainsKey(k.Id))){var (r,g,b)=pick(k);LedFrame.Set(customFrame,k.Slot,r,g,b);Paint(k);}Push();}
        var tools=new StackPanel{Spacing=8};
        tools.Children.Add(B("全部填入此色",()=>Fill(_=>(paint.R,paint.G,paint.B))));
        tools.Children.Add(B("彩虹",()=>Fill(k=>LedFrame.Hsv((k.X-20)/760.0*300,1,1))));
        tools.Children.Add(B("依排漸層",()=>Fill(k=>LedFrame.Hsv(LedFrame.Row(k)*50,1,1))));
        tools.Children.Add(B("全部清除",()=>Fill(_=>(0,0,0))));
        var editRow=new Grid{ColumnSpacing=24};editRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});editRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});editRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var view=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,MaxHeight=300,VerticalAlignment=VerticalAlignment.Top};editRow.Children.Add(view);
        Grid.SetColumn(picker,1);editRow.Children.Add(picker);Grid.SetColumn(tools,2);editRow.Children.Add(tools);editorCard.Children.Add(editRow);
        body.Children.Add(Card(editorCard));

        var lib=new StackPanel{Spacing=12};lib.Children.Add(Sub($"燈光配置（{LightLayoutLibrary.Items.Count}／{LightLayoutLibrary.Max}）"));
        var list=new ComboBox{ItemsSource=LightLayoutLibrary.Items.Select(l=>l.Name).ToList(),PlaceholderText="選擇配置",Width=220};AutomationProperties.SetName(list,"燈光配置");
        var name=new TextBox{PlaceholderText="配置名稱",MaxLength=LightLayoutLibrary.MaxName,Width=220};
        lib.Children.Add(Row(list,B("載入",()=>{if(list.SelectedIndex<0)return;customFrame=Convert.FromHexString(LightLayoutLibrary.Items[list.SelectedIndex].Frame);Push();CustomLightsPage();}),
            B("刪除",()=>{if(list.SelectedIndex<0)return;LightLayoutLibrary.Items.RemoveAt(list.SelectedIndex);LightLayoutLibrary.Save();CustomLightsPage();})));
        lib.Children.Add(Row(name,B("儲存目前顏色",()=>{try{LightLayoutLibrary.Put(string.IsNullOrWhiteSpace(name.Text)?"配置"+(LightLayoutLibrary.Items.Count+1):name.Text,customFrame);CustomLightsPage();Message("已儲存燈光配置。");}catch(ArgumentException e){Message(e.Message,true);}})));
        body.Children.Add(Card(lib));
    }
}
