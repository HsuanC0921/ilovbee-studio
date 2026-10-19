using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace iLovbeeStudio;
/// <summary>按鍵方案：四層（預設、FN1、FN2、Tap）按鍵矩陣，只存在本機；套用＝整份寫入鍵盤（與原廠行為相同）。</summary>
public sealed record Scheme(string Name,Dictionary<int,string> Layers);
public static class SchemeLibrary {
    public const int Max=20,MaxName=20;
    static string FilePath=>Path.Combine(Store.Root,"schemes.json");
    public static List<Scheme> Items{get;private set;}=Load();
    static List<Scheme> Load(){try{return JsonSerializer.Deserialize<List<Scheme>>(File.ReadAllText(FilePath),Store.Json)??new();}catch{return new();}}
    public static void Save()=>Store.Write("","schemes.json",Items);
    public static Scheme Create(string name,Dictionary<int,byte[]> layers){
        name=name.Trim();
        if(name.Length is 0 or >MaxName)throw new ArgumentException($"方案名稱需為 1 到 {MaxName} 個字。");
        if(Items.Any(s=>s.Name==name))throw new ArgumentException("已有同名方案。");
        if(Items.Count>=Max)throw new ArgumentException($"方案最多 {Max} 個。");
        MappingEditor.Validate(layers);
        return new(name,layers.ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value)));
    }
    public static Dictionary<int,byte[]> Decode(Scheme s){var maps=s.Layers.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));MappingEditor.Validate(maps);return maps;}
}
public sealed partial class MainWindow {
    int schemeSel=-1;
    void SchemeCard(){
        var items=SchemeLibrary.Items;if(schemeSel>=items.Count)schemeSel=-1;
        var card=new StackPanel{Spacing=12};card.Children.Add(Sub($"按鍵方案（{items.Count}／{SchemeLibrary.Max}）"));
        card.Children.Add(T("方案存在本機，包含預設層、FN1、FN2 與 Tap 四層。套用方案會整份寫入鍵盤，寫入前自動備份並讀回核對。",12,Muted));
        var list=new ListView{ItemsSource=items.Select(s=>s.Name).ToList(),SelectedIndex=schemeSel,MinHeight=60,MaxHeight=220};
        list.SelectionChanged+=(_,_)=>schemeSel=list.SelectedIndex;card.Children.Add(list);
        var name=new TextBox{PlaceholderText="新方案名稱",MaxLength=SchemeLibrary.MaxName,Width=220};
        card.Children.Add(Row(name,B("另存目前鍵盤為方案",()=>{
            try{
                if(maps.Count!=4)throw new InvalidOperationException("請先讀取鍵盤。");
                var scheme=SchemeLibrary.Create(string.IsNullOrWhiteSpace(name.Text)?"方案"+(items.Count+1):name.Text,maps);
                items.Add(scheme);SchemeLibrary.Save();schemeSel=items.Count-1;ProfilesPage();Message($"已儲存方案「{scheme.Name}」。");
            }catch(Exception e)when(e is ArgumentException or InvalidOperationException){Message(e.Message,true);}})));
        card.Children.Add(Row(
            B("套用到鍵盤",async()=>await Run("正在套用方案……",async()=>{
                if(schemeSel<0)throw new InvalidOperationException("請先選擇方案。");
                if(editor.ChangeCount>0)throw new InvalidOperationException("按鍵重設頁有尚未套用的變更，請先套用或取消。");
                var scheme=items[schemeSel];var desired=SchemeLibrary.Decode(scheme);var before=MappingEditor.Clone(maps);
                await Task.Run(()=>service.ApplyMappings(before,desired));
                maps.Clear();foreach(var m in desired)maps[m.Key]=m.Value;editor.Load(maps);SaveDraft();ProfilesPage();Log($"已套用方案「{scheme.Name}」");Message($"已套用方案「{scheme.Name}」並讀回核對。");})),
            B("刪除方案",()=>{if(schemeSel<0)return;items.RemoveAt(schemeSel);SchemeLibrary.Save();schemeSel=-1;ProfilesPage();})));
        body.Children.Add(Card(card));
    }
}
