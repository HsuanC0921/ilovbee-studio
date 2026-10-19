using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace iLovbeeStudio;
public sealed record LightLayout(string Name,string Frame);
public static class LightLayoutLibrary {
    public const int Max=20,MaxName=20;
    static string FilePath=>Path.Combine(Store.Root,"customlights.json");
    public static List<LightLayout> Items{get;}=Load();
    static List<LightLayout> Load(){try{return (JsonSerializer.Deserialize(File.ReadAllText(FilePath),AppJson.Default.ListLightLayout)??new()).Where(l=>l.Frame?.Length==LedFrame.Bytes*2).ToList();}catch{return new();}}
    public static void Save()=>Store.Write("","customlights.json",Items);
    public static void Put(string name,byte[] frame){
        name=name.Trim();if(name.Length is 0 or >MaxName)throw new ArgumentException($"名稱需為 1 到 {MaxName} 個字。");
        int i=Items.FindIndex(l=>l.Name==name);var item=new LightLayout(name,Convert.ToHexString(frame));
        if(i>=0)Items[i]=item;else if(Items.Count>=Max)throw new ArgumentException($"最多 {Max} 組。");else Items.Add(item);Save();
    }
}
