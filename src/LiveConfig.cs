using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace iLovbeeStudio;
/// <summary>設定介面與常駐小工具共用的即時燈效設定，純文字 key=value（不使用反射，Native AOT 可用）。</summary>
public sealed class LiveConfig {
    public string Mode="off";           // off／music／custom
    public int Style;
    public double Gain=40;
    public byte[] Frame=new byte[LedFrame.Bytes];
    static string FilePath=>Path.Combine(Store.Root,"live.ini");
    public static DateTime Stamp=>File.Exists(FilePath)?File.GetLastWriteTimeUtc(FilePath):DateTime.MinValue;
    public static LiveConfig Load(){
        var c=new LiveConfig();
        try{
            foreach(var line in File.ReadAllLines(FilePath)){
                int i=line.IndexOf('=');if(i<1)continue;string k=line[..i].Trim(),v=line[(i+1)..].Trim();
                switch(k){
                    case "mode":if(v is "off" or "music" or "custom")c.Mode=v;break;
                    case "style":if(int.TryParse(v,out int s))c.Style=Math.Clamp(s,0,LedFrame.MusicStyles.Length-1);break;
                    case "gain":if(double.TryParse(v,System.Globalization.CultureInfo.InvariantCulture,out double g))c.Gain=Math.Clamp(g,5,200);break;
                    case "frame":if(v.Length==LedFrame.Bytes*2)c.Frame=Convert.FromHexString(v);break;
                }
            }
        }catch(Exception e)when(e is IOException or FormatException){}
        return c;
    }
    public void Save(){
        Directory.CreateDirectory(Store.Root);string temp=FilePath+".tmp";
        File.WriteAllLines(temp,new[]{"mode="+Mode,"style="+Style,"gain="+Gain.ToString(System.Globalization.CultureInfo.InvariantCulture),"frame="+Convert.ToHexString(Frame)});
        File.Move(temp,FilePath,true);
    }
    /// <summary>讀取設定介面儲存的燈光配置（customlights.json），只取名稱與框格。</summary>
    public static List<(string Name,byte[] Frame)> Layouts(){
        var list=new List<(string,byte[])>();
        try{
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Store.Root,"customlights.json")));
            foreach(var e in doc.RootElement.EnumerateArray()){
                string? n=e.GetProperty("Name").GetString(),f=e.GetProperty("Frame").GetString();
                if(n!=null && f?.Length==LedFrame.Bytes*2)list.Add((n,Convert.FromHexString(f)));
            }
        }catch(Exception ex)when(ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException){}
        return list;
    }
}
