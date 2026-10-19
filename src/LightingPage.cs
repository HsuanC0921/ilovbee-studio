using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace iLovbeeStudio;

public sealed partial class MainWindow {
    void LightingPage(){
        Heading("燈效","");body.Children.RemoveAt(1);body.Spacing=16;body.Margin=new Thickness(32,16,32,24);
        var names=LightEffects.Names;
        int current=lights!=null && lights[10]<names.Length?lights[10]:-1;int chosen=current>=0?current:1;
        // 示意圖：依 keyboard.json 的座標自行繪製，亮度由 LightEffects 模擬。
        var stage=new Grid{Width=LightEffects.Width,Height=LightEffects.Height};
        stage.Children.Add(new Border{Width=LightEffects.Width,Height=LightEffects.Height,CornerRadius=new CornerRadius(18),Background=Brush("#252A32")});
        var canvas=new Canvas{Width=LightEffects.Width,Height=LightEffects.Height};stage.Children.Add(canvas);
        var cells=new List<(PhysicalKey Key,Rectangle Glow,Rectangle Lit)>();var capBrush=Brush("#3A414C");
        foreach(var k in keys.Where(k=>k.Id is not (16 or 89))){
            var cap=new Rectangle{Width=k.Width,Height=k.Height,RadiusX=5,RadiusY=5,Fill=capBrush};Canvas.SetLeft(cap,k.X);Canvas.SetTop(cap,k.Y);canvas.Children.Add(cap);
            var glow=new Rectangle{Width=k.Width+10,Height=k.Height+10,RadiusX=9,RadiusY=9,Fill=new SolidColorBrush(Colors.White),Opacity=0};Canvas.SetLeft(glow,k.X-5);Canvas.SetTop(glow,k.Y-5);canvas.Children.Add(glow);
            var lit=new Rectangle{Width=k.Width-4,Height=k.Height-4,RadiusX=4,RadiusY=4,Fill=new SolidColorBrush(Colors.White),Opacity=0};Canvas.SetLeft(lit,k.X+2);Canvas.SetTop(lit,k.Y+2);canvas.Children.Add(lit);
            cells.Add((k,glow,lit));
        }
        var preview=new Viewbox{Child=stage,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,MaxHeight=190,HorizontalAlignment=HorizontalAlignment.Center};
        AutomationProperties.SetName(preview,"燈效示意圖");
        var onDark=Brush("#AEB7C4");
        var modeName=new TextBlock{FontSize=22,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(Colors.White)};
        var hint=T("",14,onDark);var status=T("",12,onDark);
        var apply=B("套用至鍵盤",()=>{});apply.Style=(Style)Application.Current.Resources["AccentButtonStyle"];
        var info=new StackPanel{Spacing=2};info.Children.Add(modeName);info.Children.Add(hint);info.Children.Add(status);
        var bar=new Grid{ColumnSpacing=16};bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        bar.Children.Add(info);Grid.SetColumn(apply,1);apply.VerticalAlignment=VerticalAlignment.Center;bar.Children.Add(apply);
        // 亮度：每個模式在 payload[56+2*模式] 有一個 1..4 的亮度值（實機以 Fn+↓ 三次讓 4→1 確認）。
        var slider=new Slider{Minimum=1,Maximum=4,StepFrequency=1,TickFrequency=1,TickPlacement=Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside,Width=220,RequestedTheme=ElementTheme.Dark,VerticalAlignment=VerticalAlignment.Center};AutomationProperties.SetName(slider,"亮度");
        var levelText=T("",12,onDark);levelText.VerticalAlignment=VerticalAlignment.Center;
        var levelRow=Row(T("亮度",14,new SolidColorBrush(Colors.White)),slider,levelText);
        var hero=new StackPanel{Spacing=16};hero.Children.Add(preview);hero.Children.Add(levelRow);hero.Children.Add(bar);
        var heroBg=new LinearGradientBrush{StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(0,1)};
        heroBg.GradientStops.Add(new GradientStop{Color=ColorHelper.FromArgb(255,43,49,59),Offset=0});heroBg.GradientStops.Add(new GradientStop{Color=ColorHelper.FromArgb(255,22,25,31),Offset=1});
        body.Children.Add(new Border{Background=heroBg,CornerRadius=new CornerRadius(14),Padding=new Thickness(24,20,24,20),Child=hero});
        // 每個模式一張縮圖卡片，縮圖為該效果的代表畫面。
        var tiles=new GridView{SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true,Margin=new Thickness(-4,0,-4,0)};AutomationProperties.SetName(tiles,"主背光模式");
        var frames=new List<Border>();
        for(int i=0;i<names.Length;i++){
            var thumb=new Canvas{Width=LightEffects.Width,Height=LightEffects.Height};
            thumb.Children.Add(new Rectangle{Width=LightEffects.Width,Height=LightEffects.Height,RadiusX=18,RadiusY=18,Fill=Brush("#1B1F25")});
            for(int n=0;n<cells.Count;n++){
                var k=cells[n].Key;var c=LightEffects.Sample(i,i==4||i==9||i==12?2.3:1.7,k.X+k.Width/2.0,k.Y+k.Height/2.0,n);double a=c.A/255.0;
                byte Mix(byte lit,byte cap)=>(byte)(cap+(lit-cap)*a);
                var r=new Rectangle{Width=k.Width,Height=k.Height,RadiusX=5,RadiusY=5,Fill=new SolidColorBrush(Color.FromArgb(255,Mix(c.R,0x3A),Mix(c.G,0x41),Mix(c.B,0x4C)))};Canvas.SetLeft(r,k.X);Canvas.SetTop(r,k.Y);thumb.Children.Add(r);
            }
            var label=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Height=26};label.Children.Add(new TextBlock{Text=names[i],FontSize=13,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center});
            if(i==current){label.Children.Add(Chip("使用中"));}
            var cardBody=new StackPanel{Spacing=8,Width=176};cardBody.Children.Add(new Viewbox{Child=thumb,Width=176,Height=70,Stretch=Stretch.Uniform});cardBody.Children.Add(label);
            var frame=new Border{Padding=new Thickness(8),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=Line,Background=new SolidColorBrush(Colors.White),Margin=new Thickness(4),Child=cardBody};
            AutomationProperties.SetName(frame,names[i]);frames.Add(frame);tiles.Items.Add(frame);
        }
        body.Children.Add(tiles);
        bool BrightnessMode(int m)=>m is >=1 and <=17;
        int StoredLevel(int m)=>lights!=null && BrightnessMode(m) && lights[56+2*m] is >=1 and <=4?lights[56+2*m]:4;
        int level=StoredLevel(chosen);bool syncing=false;
        var sw=Stopwatch.StartNew();bool still=!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        void Draw(){
            double t=still?1.2:sw.Elapsed.TotalSeconds;
            for(int i=0;i<cells.Count;i++){
                var (k,glow,lit)=cells[i];var c=LightEffects.Sample(chosen,t,k.X+k.Width/2.0,k.Y+k.Height/2.0,i);
                var color=Color.FromArgb(255,c.R,c.G,c.B);double a=c.A/255.0*(BrightnessMode(chosen)?new[]{0,.08,.8,.9,1.0}[level]:1);
                ((SolidColorBrush)lit.Fill).Color=color;lit.Opacity=a*.92;((SolidColorBrush)glow.Fill).Color=color;glow.Opacity=a*.28;
            }
        }
        void Sync(){
            modeName.Text=names[chosen];hint.Text=LightEffects.Hints[chosen];
            syncing=true;slider.IsEnabled=BrightnessMode(chosen);slider.Value=level;levelText.Text=BrightnessMode(chosen)?$"{level} / 4"+(level==1?" · 近乎熄滅":""):"此模式無亮度設定";syncing=false;
            bool changed=chosen!=current || (BrightnessMode(chosen) && level!=StoredLevel(chosen));
            apply.IsEnabled=!busy && lights!=null && changed;
            status.Text=lights==null?"示意圖僅供預覽；連接 USB 並讀取鍵盤後即可套用。":!changed?"示意圖僅供預覽 · 這是鍵盤目前使用的設定。":$"示意圖僅供預覽 · 目前：{(current>=0?names[current]:"未知")} → 套用後：{names[chosen]}";
            Draw();
        }
        void Mark(){for(int i=0;i<frames.Count;i++){bool on=i==chosen;frames[i].BorderBrush=on?Blue:Line;frames[i].BorderThickness=new Thickness(on?2:1);frames[i].Background=on?Brush("#EEF4FF"):new SolidColorBrush(Colors.White);}}
        tiles.ItemClick+=(_,e)=>{int i=frames.IndexOf((Border)e.ClickedItem);if(i>=0){chosen=i;level=StoredLevel(i);Mark();Sync();}};
        slider.ValueChanged+=(_,e)=>{if(syncing)return;level=(int)Math.Round(e.NewValue);Sync();};
        Mark();Sync();
        apply.Click+=async(_,_)=>await Run("正在讀取並套用燈效……",async()=>{
            if(lights==null)throw new InvalidOperationException("請先讀取鍵盤。");
            var before=await Task.Run(()=>service.ReadLights());var after=(byte[])before.Clone();after[10]=(byte)chosen;if(BrightnessMode(chosen))after[56+2*chosen]=(byte)level;
            if(!after.SequenceEqual(before)){await Task.Run(()=>service.SetLights(before,after));Message("燈效已套用並完成讀回核對。");}else Message("鍵盤目前已使用此模式。");
            lights=after;current=chosen;level=StoredLevel(chosen);Sync();
        });
        if(!still){lightTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};lightTimer.Tick+=(_,_)=>Draw();lightTimer.Start();}
        var keysCard=new StackPanel{Spacing=12};keysCard.Children.Add(Sub("鍵盤快捷鍵"));
        keysCard.Children.Add(Row(Chip("Fn"),T("＋",12,Muted),Chip("Scroll Lock"),T("切換主背光效果",14,Muted)));
        keysCard.Children.Add(Row(Chip("Fn"),T("＋",12,Muted),Chip("Backspace"),T("切換整體 RGB 燈光（含側燈）",14,Muted)));body.Children.Add(Card(keysCard));
    }
}
