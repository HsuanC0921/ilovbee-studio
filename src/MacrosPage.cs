using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace iLovbeeStudio;
public sealed partial class MainWindow {
    int macroSel;
    void MacrosPage(){
        Heading("巨集","錄製或編輯按鍵序列，寫入鍵盤後，到「按鍵重設」把巨集指派給任一按鍵。");
        var lib=MacroLibrary.Items;macroSel=Math.Clamp(macroSel,0,Math.Max(0,lib.Count-1));
        var listBox=new ListView{ItemsSource=lib.Select(m=>m.Name).ToList(),SelectedIndex=lib.Count>0?macroSel:-1,MinHeight=60,MaxHeight=260};
        listBox.SelectionChanged+=(_,_)=>{if(listBox.SelectedIndex>=0&&listBox.SelectedIndex!=macroSel){macroSel=listBox.SelectedIndex;DispatcherQueue.TryEnqueue(MacrosPage);}};
        var listPanel=new StackPanel{Spacing=12};listPanel.Children.Add(Sub($"巨集清單（{lib.Count}／{MacroCodec.MaxMacros}）"));listPanel.Children.Add(listBox);
        listPanel.Children.Add(Row(
            B("新增",()=>{if(lib.Count>=MacroCodec.MaxMacros){Message($"巨集最多 {MacroCodec.MaxMacros} 個。",true);return;}lib.Add(new Macro("巨集"+(lib.Count+1),new()));macroSel=lib.Count-1;MacroLibrary.Save();MacrosPage();}),
            B("複製",()=>{if(lib.Count==0||lib.Count>=MacroCodec.MaxMacros)return;var m=lib[macroSel];lib.Add(new Macro((m.Name+"2").Length>MacroCodec.MaxName?m.Name:m.Name+"2",m.Events.ToList()));macroSel=lib.Count-1;MacroLibrary.Save();MacrosPage();}),
            B("刪除",()=>{if(lib.Count==0)return;if(maps.Values.Any(map=>Enumerable.Range(0,104).Any(s=>MacroCodec.IsMacroKey(map.AsSpan(s*4,4)))))Message("鍵盤上仍有按鍵指向巨集；刪除會改變巨集編號，請先到按鍵重設改回原本功能。",true);else{lib.RemoveAt(macroSel);MacroLibrary.Save();MacrosPage();}})));
        body.Children.Add(Card(listPanel));
        if(lib.Count>0)body.Children.Add(Card(MacroEditor(lib[macroSel])));
        var write=new StackPanel{Spacing=10};write.Children.Add(Sub("寫入鍵盤"));
        int used;try{used=MacroCodec.Build(lib).Length;}catch{used=0;}
        write.Children.Add(T($"目前容量 {used}／{MacroCodec.MaxBytes} 位元組。寫入前會自動備份；原廠流程不讀回巨集，因此寫入後請按下指派的按鍵實測。",12,Muted));
        write.Children.Add(B("寫入巨集到鍵盤",async()=>await Run("正在寫入巨集……",async()=>{
            var items=MacroLibrary.Items.ToList();if(items.Count==0)throw new InvalidOperationException("尚無巨集。");MacroCodec.Validate(items);
            await Task.Run(()=>service.WriteMacros(MacroCodec.Build(items)));Log($"已寫入 {items.Count} 個巨集");Message("巨集已寫入。請到「按鍵重設」指派，並按該鍵實測。");})));
        body.Children.Add(Card(write));
    }
    UIElement MacroEditor(Macro current){
        int index=macroSel;var events=current.Events;
        void Commit(Macro m){MacroLibrary.Items[index]=m;MacroLibrary.Save();}
        var name=new TextBox{Text=current.Name,MaxLength=MacroCodec.MaxName,Width=260,Header="名稱",HorizontalAlignment=HorizontalAlignment.Left};
        name.LostFocus+=(_,_)=>{if(name.Text.Length>0&&name.Text!=current.Name){Commit(current with{Name=name.Text});DispatcherQueue.TryEnqueue(MacrosPage);}};
        string Line(MacroEvent e)=>$"{(e.Up?"↑ 放開":"↓ 按下")}　{MacroCodec.KeyName(e.Code)}　　之後等待 {e.Delay} ms";
        var list=new ListView{ItemsSource=events.Select(Line).ToList(),MinHeight=140,MaxHeight=300};
        var delay=new NumberBox{Header="等待毫秒",Minimum=0,Maximum=100000,Value=10,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,Width=140};
        var keyBox=new ComboBox{ItemsSource=shortcutKeys,DisplayMemberPath="Name",Width=200,PlaceholderText="選擇按鍵",Header="手動加入"};
        void Add(params MacroEvent[] add){var next=events.ToList();next.AddRange(add);if(next.Count>MacroCodec.MaxEvents){Message($"單一巨集最多 {MacroCodec.MaxEvents} 個事件。",true);return;}Commit(current with{Events=next});MacrosPage();}
        // 錄製：事件之後的等待時間＝到下一個事件的間隔。
        bool recording=false;var clock=new Stopwatch();long last=-1;
        var capture=new Border{IsTabStop=true,Padding=new Thickness(12),Background=Brush("#F4F6F9"),CornerRadius=new CornerRadius(6),Child=T("點「開始錄製」後，在此區域按鍵（修飾鍵也會記錄）。",13,Muted)};
        var record=new Button{Content="開始錄製",Padding=new Thickness(16,9,16,9)};
        var recorded=events.ToList();var downCodes=new Dictionary<VirtualKey,byte>();
        void Stamp(){long now=clock.ElapsedMilliseconds;if(recorded.Count>0&&last>=0){var p=recorded[^1];recorded[^1]=p with{Delay=(int)Math.Min(now-last,0xFFFFF)};}last=now;}
        byte? Resolve(VirtualKey key,bool down,Windows.UI.Core.CorePhysicalKeyStatus status){
            if(!down&&downCodes.TryGetValue(key,out var remembered))return remembered;
            byte hid;
            if(key==VirtualKey.Shift)hid=status.ScanCode==0x36?(byte)0xE5:(byte)0xE1;
            else if(key==VirtualKey.Control)hid=status.IsExtendedKey?(byte)0xE4:(byte)0xE0;
            else if(key==VirtualKey.Menu)hid=status.IsExtendedKey?(byte)0xE6:(byte)0xE2;
            else if(!MacroCodec.TryHid((int)key,out hid))return null;
            return hid;
        }
        capture.KeyDown+=(_,e)=>{if(!recording)return;e.Handled=true;if(downCodes.ContainsKey(e.Key))return;var code=Resolve(e.Key,true,e.KeyStatus);if(code==null||recorded.Count>=MacroCodec.MaxEvents)return;Stamp();downCodes[e.Key]=code.Value;recorded.Add(new MacroEvent(code.Value,false,0));};
        capture.KeyUp+=(_,e)=>{if(!recording)return;e.Handled=true;var code=Resolve(e.Key,false,e.KeyStatus);if(code==null||recorded.Count>=MacroCodec.MaxEvents)return;Stamp();downCodes.Remove(e.Key);recorded.Add(new MacroEvent(code.Value,true,0));};
        record.Click+=(_,_)=>{
            if(!recording){recording=true;recorded=new();clock.Restart();last=-1;downCodes.Clear();record.Content="停止並儲存";capture.Focus(FocusState.Programmatic);}
            else{recording=false;clock.Stop();if(recorded.Count>0)recorded[^1]=recorded[^1] with{Delay=0};Commit(current with{Events=recorded.ToList()});MacrosPage();}
        };
        var editRow=Row(delay,B("套用到所選事件",()=>{int i=list.SelectedIndex;if(i<0)return;var next=events.ToList();next[i]=next[i] with{Delay=(int)delay.Value};Commit(current with{Events=next});MacrosPage();}),
            B("刪除所選事件",()=>{int i=list.SelectedIndex;if(i<0)return;var next=events.ToList();next.RemoveAt(i);Commit(current with{Events=next});MacrosPage();}),
            B("清空",()=>{Commit(current with{Events=new()});MacrosPage();}));
        editRow.VerticalAlignment=VerticalAlignment.Bottom;
        var manual=Row(keyBox,B("加入點擊",()=>{if(keyBox.SelectedItem is KeyAction k)Add(new MacroEvent(k.Bytes[3],false,(int)delay.Value),new MacroEvent(k.Bytes[3],true,(int)delay.Value));}),
            B("加入按下",()=>{if(keyBox.SelectedItem is KeyAction k)Add(new MacroEvent(k.Bytes[3],false,(int)delay.Value));}),
            B("加入放開",()=>{if(keyBox.SelectedItem is KeyAction k)Add(new MacroEvent(k.Bytes[3],true,(int)delay.Value));}));
        manual.VerticalAlignment=VerticalAlignment.Bottom;
        var panel=new StackPanel{Spacing=14};panel.Children.Add(Sub("編輯巨集"));panel.Children.Add(name);
        panel.Children.Add(T($"事件（{events.Count}／{MacroCodec.MaxEvents}）：每個事件的「之後等待」就是它與下一個事件的間隔。",12,Muted));
        panel.Children.Add(list);panel.Children.Add(editRow);panel.Children.Add(manual);panel.Children.Add(record);panel.Children.Add(capture);
        return panel;
    }
}



