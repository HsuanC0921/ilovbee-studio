using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace iLovbeeStudio;
/// <summary>即時燈效框格：126 個按鍵槽位 × 3 位元組（R,G,B），槽位與按鍵矩陣相同。</summary>
public static class LedFrame {
    public const int Keys=126,Bytes=Keys*3,CustomMode=18,Bands=16;
    public static void Set(byte[] frame,int slot,byte r,byte g,byte b){frame[slot*3]=r;frame[slot*3+1]=g;frame[slot*3+2]=b;}
    public static (byte R,byte G,byte B) Hsv(double h,double s,double v){
        h=(h%360+360)%360/60;double c=v*s,x=c*(1-Math.Abs(h%2-1)),m=v-c;
        var (r,g,b)=(int)h switch{0=>(c,x,0.0),1=>(x,c,0.0),2=>(0.0,c,x),3=>(0.0,x,c),4=>(x,0.0,c),_=>(c,0.0,x)};
        return ((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    /// <summary>B87 的六排按鍵：0＝F 列 … 5＝空白鍵列。</summary>
    public static int Row(PhysicalKey k)=>Math.Clamp((int)Math.Round((k.Y-38)/43.0),0,5);
    public static readonly string[] MusicStyles={"頻譜柱","鏡像頻譜","瀑布","低頻脈衝","漣漪","能量彩虹"};
}
/// <summary>音樂視覺化。bands 為各頻段強度 0..1（左低右高）；有狀態的效果（瀑布、漣漪）需沿用同一個實例。</summary>
public sealed class MusicVisualizer {
    readonly IReadOnlyList<PhysicalKey> keys;
    readonly float[][] history=new float[6][];                  // 瀑布：每排保存一組頻譜
    readonly List<(double Born,double Hue)> ripples=new();
    double lastBeat,bass;
    public MusicVisualizer(IReadOnlyList<PhysicalKey> keys){this.keys=keys;for(int i=0;i<history.Length;i++)history[i]=new float[LedFrame.Bands];}
    static int Band(PhysicalKey k,int n)=>Math.Clamp((int)((k.X+k.Width/2.0-20)/760*n),0,n-1);
    public byte[] Render(int style,float[] bands,double t){
        var f=new byte[LedFrame.Bytes];int n=bands.Length;
        float low=(bands[0]+bands[1]+bands[2])/3;bass=Math.Max(low,bass*.9);
        bool beat=low>.35 && low>bass*.92 && t-lastBeat>.18;if(beat)lastBeat=t;
        if(style==2 && t-wf>.07){wf=t;for(int r=history.Length-1;r>0;r--)Array.Copy(history[r-1],history[r],n);Array.Copy(bands,history[0],n);}
        if(style==4 && beat){ripples.Add((t,t*40));if(ripples.Count>4)ripples.RemoveAt(0);}
        ripples.RemoveAll(r=>t-r.Born>1.6);
        foreach(var k in keys){
            if(k.Slot is <0 or >=LedFrame.Keys)continue;
            int band=Band(k,n),row=LedFrame.Row(k);double hue,v;double cx=k.X+k.Width/2.0,cy=k.Y+k.Height/2.0;
            switch(style){
                case 0: hue=t*20+band*300.0/n;v=bands[band]*6-(5-row);break;
                case 1:{int m=Math.Clamp((int)(Math.Abs(cx-420)/380*n),0,n-1);hue=t*20+m*300.0/n;v=bands[m]*6-(5-row);break;}
                case 2: hue=t*10+band*300.0/n;v=history[row][band]*1.4;break;
                case 3: hue=t*15+row*18;v=Math.Min(1,bass*1.5)*(1-.12*row)-(beat?0:0);break;
                case 4:{hue=0;v=0;foreach(var r in ripples){double age=t-r.Born,dist=Math.Sqrt((cx-420)*(cx-420)+(cy-150)*(cy-150)),w=Math.Abs(dist-age*420);
                        if(w<60){double a=(1-w/60)*(1-age/1.6);if(a>v){v=a;hue=r.Hue;}}}break;}
                default:{double energy=bands.Average();hue=t*30+cx/760*240-energy*60;v=.12+Math.Min(1,energy*2.4)*(.5+.5*bands[band]);break;}
            }
            if(v<=0.02)continue;
            var (r2,g2,b2)=LedFrame.Hsv(hue,1,Math.Clamp(v,0,1));LedFrame.Set(f,k.Slot,r2,g2,b2);
        }
        return f;
    }
    double wf;
}
/// <summary>讀取 keyboard.json 的按鍵座標（不使用反射，Native AOT 可用）。</summary>
public static class KeyLayout {
    public static List<PhysicalKey> Load(string path){
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateArray().Select(e=>new PhysicalKey(e.GetProperty("Id").GetInt32(),e.GetProperty("Name").GetString()??"",e.GetProperty("X").GetDouble(),e.GetProperty("Y").GetDouble(),e.GetProperty("Width").GetDouble(),e.GetProperty("Height").GetDouble(),e.GetProperty("Slot").GetInt32())).ToList();
    }
}
/// <summary>在背景持續送出即時燈效。固定顏色每秒重送一次；音樂律動約 30 fps。</summary>
public sealed class LiveLights : IDisposable {
    readonly KeyboardService service;readonly IReadOnlyList<PhysicalKey> keys;
    readonly Thread thread;readonly AutoResetEvent wake=new(false);volatile bool stop;
    volatile byte[]? still;AudioLoopback? audio;
    public double Gain{get;set;}=40;public volatile int Style;
    public bool Music=>audio!=null;
    public string? Error{get;private set;}
    public LiveLights(KeyboardService service,IReadOnlyList<PhysicalKey> keys,byte[]? frame){
        this.service=service;this.keys=keys;still=frame;if(frame==null)audio=new AudioLoopback();
        thread=new Thread(Loop){IsBackground=true,Name="live-lights"};thread.Start();
    }
    public void Show(byte[] frame){if(frame.Length!=LedFrame.Bytes)throw new ArgumentException("無效的燈效框格。");still=frame;wake.Set();}
    void Loop(){
        var level=new float[LedFrame.Bands];var viz=new MusicVisualizer(keys);byte[]? sent=null;var sw=System.Diagnostics.Stopwatch.StartNew();
        while(!stop){
            try{
                if(audio!=null){
                    var now=audio.Bands(LedFrame.Bands,Gain);
                    for(int i=0;i<level.Length;i++)level[i]=Math.Max(now[i],level[i]*.82f);
                    var frame=viz.Render(Style,level,sw.Elapsed.TotalSeconds);
                    if(sent==null || !frame.AsSpan().SequenceEqual(sent)){service.SendFrame(frame);sent=frame;}
                    Error=audio.Error;Thread.Sleep(33);   // 每秒約 30 格；送太快會擠滿鍵盤的 USB 通道
                }else{service.SendFrame(still!);Error=null;wake.WaitOne(1000);}
            }catch(Exception e){Error=e.Message;Thread.Sleep(1000);try{service.Scan();}catch(Exception){}}
        }
    }
    public void Dispose(){stop=true;wake.Set();thread.Join(1500);audio?.Dispose();wake.Dispose();}
}
