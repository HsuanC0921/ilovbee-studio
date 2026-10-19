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
    public int ColorMode,ColorDir;       // 0 彩虹混色、1 自訂配色；配色方向 0 高度、1 頻率、2 時間、3 強度
    public string[] Colors={"FF2060","20C0FF"};   // 自訂配色的 1–6 個 RRGGBB
    public MusicColor Palette=>new(ColorMode,Colors.Select(Convert.FromHexString).ToArray(),ColorDir);
    public byte[] Frame=new byte[LedFrame.Bytes];
    public byte[] Fx=new byte[LedFrame.Keys];   // 逐鍵行為，見 LedFrame.KeyEffects
    public int FxSpeed=2;
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
                    case "colormode":if(int.TryParse(v,out int cm))c.ColorMode=Math.Clamp(cm,0,1);break;
                    case "colordir":if(int.TryParse(v,out int cd))c.ColorDir=Math.Clamp(cd,0,3);break;
                    case "colors":{var l=v.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(IsHex6).Select(x=>x.ToUpperInvariant()).Take(MusicColor.MaxStops).ToArray();if(l.Length>0)c.Colors=l;break;}
                    case "fx":if(v.Length==LedFrame.Keys*2)c.Fx=Convert.FromHexString(v).Select(b=>(byte)Math.Min((int)b,LedFrame.KeyEffects.Length-1)).ToArray();break;
                    case "fxspeed":if(int.TryParse(v,out int fs))c.FxSpeed=Math.Clamp(fs,1,4);break;
                    case "frame":if(v.Length==LedFrame.Bytes*2)c.Frame=Convert.FromHexString(v);break;
                }
            }
        }catch(Exception e)when(e is IOException or FormatException){}
        return c;
    }
    static bool IsHex6(string v)=>v.Length==6&&v.All(Uri.IsHexDigit);
    public void Save(){
        Directory.CreateDirectory(Store.Root);string temp=FilePath+".tmp";
        File.WriteAllLines(temp,new[]{"mode="+Mode,"style="+Style,"gain="+Gain.ToString(System.Globalization.CultureInfo.InvariantCulture),"colormode="+ColorMode,"colordir="+ColorDir,"colors="+string.Join(",",Colors),"frame="+Convert.ToHexString(Frame),"fx="+Convert.ToHexString(Fx),"fxspeed="+FxSpeed});
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
