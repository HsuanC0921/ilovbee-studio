using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace iLovbeeStudio;
/// <summary>按鍵方案：四層（預設、FN1、FN2、Tap）按鍵矩陣，只存在本機；套用＝整份寫入鍵盤（與原廠行為相同）。</summary>
public sealed record Scheme(string Name,Dictionary<int,string> Layers);
public static class SchemeLibrary {
    public const int Max=20,MaxName=20;
    static string FilePath=>Path.Combine(Store.Root,"schemes.json");
    public static List<Scheme> Items{get;private set;}=Load();
    static List<Scheme> Load(){try{return JsonSerializer.Deserialize(File.ReadAllText(FilePath),AppJson.Default.ListScheme)??new();}catch{return new();}}
    public static void Save()=>Store.Write("","schemes.json",Items);
    public static Scheme Create(string name,Dictionary<int,byte[]> layers){
        name=name.Trim();
        if(name.Length is 0 or >MaxName)throw new ArgumentException($"方案名稱需為 1 到 {MaxName} 個字。");
        if(Items.Any(s=>s.Name==name))throw new ArgumentException("已有同名方案。");
        if(Items.Count>=Max)throw new ArgumentException($"方案最多 {Max} 個。");
        MappingEditor.Validate(layers);
        return new(name,layers.ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value)));
    }
    public static void Rename(int index,string name){
        name=name.Trim();
        if(name.Length is 0 or >MaxName)throw new ArgumentException($"方案名稱需為 1 到 {MaxName} 個字。");
        if(Items.Where((_,i)=>i!=index).Any(s=>s.Name==name))throw new ArgumentException("已有同名方案。");
        Items[index]=Items[index] with{Name=name};Save();
    }
    public static Dictionary<int,byte[]> Decode(Scheme s){var maps=s.Layers.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));MappingEditor.Validate(maps);return maps;}
}
