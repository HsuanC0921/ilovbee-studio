using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace iLovbeeStudio;
/// <summary>即時燈效框格：126 個按鍵槽位 × 3 位元組（R,G,B），槽位與按鍵矩陣相同。</summary>
public static class LedFrame {
    public const int Keys=126,Bytes=Keys*3,CustomMode=18,Bands=18;
    public static void Set(byte[] frame,int slot,byte r,byte g,byte b){frame[slot*3]=r;frame[slot*3+1]=g;frame[slot*3+2]=b;}
    public static (byte R,byte G,byte B) Hsv(double h,double s,double v){
        h=(h%360+360)%360/60;double c=v*s,x=c*(1-Math.Abs(h%2-1)),m=v-c;
        var (r,g,b)=(int)h switch{0=>(c,x,0.0),1=>(x,c,0.0),2=>(0.0,c,x),3=>(0.0,x,c),4=>(x,0.0,c),_=>(c,0.0,x)};
        return ((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    /// <summary>B87 的六排按鍵：0＝F 列 … 5＝空白鍵列。</summary>
    public static int Row(PhysicalKey k)=>Math.Clamp((int)Math.Round((k.Y-38)/43.0),0,5);
    /// <summary>逐鍵行為（存在 fx[slot]）：0 恆亮、1 閃爍、2 呼吸、3 彩虹。</summary>
    public static readonly string[] KeyEffects={"恆亮","閃爍","呼吸","彩虹"};
    static readonly double[] Rate={0,.4,.7,1.1,1.6};   // 速度 1..4 對應每秒循環次數
    /// <summary>依逐鍵行為算出時間 t（秒）的畫面；彩虹的亮度取原色最亮的通道。</summary>
    public static byte[] Animate(byte[] frame,byte[] fx,double t,int speed){
        var f=(byte[])frame.Clone();double w=t*Rate[Math.Clamp(speed,1,4)],ph=w-Math.Floor(w);
        double breath=.5-.5*Math.Cos(2*Math.PI*w);
        var (hr,hg,hb)=Hsv(w*120,1,1);
        for(int s=0;s<Math.Min(Keys,fx.Length);s++){
            int o=s*3;
            switch(fx[s]){
                case 1:if(ph>=.5)f[o]=f[o+1]=f[o+2]=0;break;
                case 2:for(int i=0;i<3;i++)f[o+i]=(byte)Math.Round(f[o+i]*breath);break;
                case 3:{double v=Math.Max(f[o],Math.Max(f[o+1],f[o+2]))/255.0;f[o]=(byte)Math.Round(hr*v);f[o+1]=(byte)Math.Round(hg*v);f[o+2]=(byte)Math.Round(hb*v);break;}
            }
        }
        return f;
    }
    public static readonly string[] MusicStyles={"頻譜柱","鏡像頻譜","瀑布","低頻脈衝","漣漪","能量彩虹","音量計","峰值保持","星空","火焰","對角掃描","橫向電平"};
}
/// <summary>音樂燈效的配色。Mode 0＝彩虹混色（各樣式自己算色相）；Mode 1＝自訂配色：Stops 為 1–6 個顏色，
/// Direction 決定漸層沿哪個方向：0 高度（柱子由下到上）、1 頻率（左到右）、2 隨時間流動、3 依強度。</summary>
public sealed record MusicColor(int Mode,byte[][] Stops,int Direction) {
    public const int MaxStops=6;
    public static readonly MusicColor Mix=new(0,new[]{new byte[]{255,64,0}},0);
    byte[] Sample(double pos){
        int n=Stops.Length;if(n==1)return Stops[0];
        double f=Math.Clamp(pos,0,1)*(n-1);int i=Math.Min(n-2,(int)f);double t=f-i;
        return new[]{Lerp(Stops[i][0],Stops[i+1][0],t),Lerp(Stops[i][1],Stops[i+1][1],t),Lerp(Stops[i][2],Stops[i+1][2],t)};
    }
    static byte Lerp(byte x,byte y,double t)=>(byte)Math.Round(x+(y-x)*t);
    /// <summary>hue 供彩虹混色；pos 為自訂配色在漸層上的位置 0..1；sat 0 表示白色強調（如峰值點）。</summary>
    public (byte R,byte G,byte B) Pick(double hue,double sat,double v,double pos){
        if(Mode==0)return LedFrame.Hsv(hue,sat,v);
        var c=Sample(pos);double k=Math.Clamp(v,0,1);
        return ((byte)Math.Round(Lerp(255,c[0],sat)*k),(byte)Math.Round(Lerp(255,c[1],sat)*k),(byte)Math.Round(Lerp(255,c[2],sat)*k));
    }
}
/// <summary>音樂視覺化。bands 為各欄強度 0..1（欄 0 在最左、為低頻）；有狀態的效果（瀑布、漣漪）需沿用同一個實例。
/// 按鍵依實際座標分成 18 欄，寬鍵（空白鍵、Shift…）橫跨多欄，讓每根柱子貼著真正的按鍵欄。</summary>
public sealed class MusicVisualizer {
    const double Pitch=41.2,Left=21;
    readonly IReadOnlyList<PhysicalKey> keys;
    readonly (int C0,int C1,int Row)[] cell;
    readonly float[][] history=new float[6][];                  // 瀑布：每排保存一組頻譜
    readonly List<(double Born,double Hue)> ripples=new(),sweeps=new();
    readonly float[] peakHold=new float[LedFrame.Bands];
    double lastBeat,bass,wf,peak;
    public MusicVisualizer(IReadOnlyList<PhysicalKey> keys){
        this.keys=keys;for(int i=0;i<history.Length;i++)history[i]=new float[LedFrame.Bands];
        static int Col(double x)=>Math.Clamp((int)Math.Floor((x-Left)/Pitch),0,LedFrame.Bands-1);
        cell=keys.Select(k=>k.Width>45?(Col(k.X+8),Col(k.X+k.Width-8),LedFrame.Row(k)):(Col(k.X+k.Width/2.0),Col(k.X+k.Width/2.0),LedFrame.Row(k))).ToArray();
    }
    static double Hash(int n){unchecked{uint x=(uint)n*2654435761u;x^=x>>15;x*=2246822519u;x^=x>>13;return (x&0xFFFFFF)/(double)0x1000000;}}
    static double PingPong(double x){x=(x/2-Math.Floor(x/2))*2;return x>1?2-x:x;}
    public byte[] Render(int style,float[] raw,double t,MusicColor? color=null){
        color??=MusicColor.Mix;
        var f=new byte[LedFrame.Bytes];int n=raw.Length;
        // 自動增益：以近期峰值正規化，再平方拉開強弱，避免音量大時整片都亮。
        peak=Math.Max(raw.Max(),peak*.997);float norm=(float)Math.Max(.25,peak);
        var bands=raw.Select(b=>{float x=Math.Clamp(b/norm,0,1);return x*x;}).ToArray();
        float low=(bands[0]+bands[1]+bands[2])/3;bass=Math.Max(low,bass*.9);
        bool beat=low>.35 && low>bass*.92 && t-lastBeat>.18;if(beat)lastBeat=t;
        if(style==2 && t-wf>.07){wf=t;for(int r=history.Length-1;r>0;r--)Array.Copy(history[r-1],history[r],n);Array.Copy(bands,history[0],n);}
        if(style==4 && beat){ripples.Add((t,t*40));if(ripples.Count>4)ripples.RemoveAt(0);}
        if(style==10 && beat){sweeps.Add((t,t*50));if(sweeps.Count>3)sweeps.RemoveAt(0);}
        ripples.RemoveAll(r=>t-r.Born>1.6);sweeps.RemoveAll(r=>t-r.Born>1.5);
        for(int i=0;i<n;i++)peakHold[i]=Math.Max(bands[i],peakHold[i]*.97f);
        float energy=bands.Average();
        for(int ki=0;ki<keys.Count;ki++){
            var k=keys[ki];if(k.Slot is <0 or >=LedFrame.Keys)continue;
            var (c0,c1,row)=cell[ki];double cx=k.X+k.Width/2.0,cy=k.Y+k.Height/2.0;
            float Span(float[] a){float m=0;for(int c=c0;c<=c1;c++)m=Math.Max(m,a[c]);return m;}
            int band=(c0+c1)/2;double up=5-row,hue,v,sat=1;
            switch(style){
                case 0: hue=t*6+band*270.0/n;v=Span(bands)*6-up;break;                                                  // 頻譜柱
                case 1:{int m=(int)Math.Min(n/2-1,Math.Floor(Math.Abs((c0+c1)/2.0-(n-1)/2.0)));hue=t*6+m*270.0/n;v=bands[m*2]*6-up;break;}   // 鏡像：低頻在中央
                case 2: hue=t*10+band*270.0/n;v=Span(history[row])*1.4;break;                                           // 瀑布
                case 3: hue=t*15+row*18;v=Math.Min(1,bass*1.5)*(1-.12*row);break;                                      // 低頻脈衝
                case 4:{hue=0;v=0;foreach(var r in ripples){double age=t-r.Born,dist=Math.Sqrt((cx-420)*(cx-420)+(cy-150)*(cy-150)),w=Math.Abs(dist-age*420);
                        if(w<60){double a=(1-w/60)*(1-age/1.6);if(a>v){v=a;hue=r.Hue;}}}break;}                         // 漣漪
                case 5:{hue=t*30+cx/760*240-energy*60;v=.12+Math.Min(1,energy*2.4)*(.5+.5*Span(bands));break;}        // 能量彩虹
                case 6:{hue=row*24;v=Math.Min(1,energy*3)*6-up;break;}                                                  // 音量計
                case 7:{hue=t*6+band*270.0/n;v=Span(bands)*6-up;int top=5-(int)Math.Min(5,Span(peakHold)*6);if(row==top){v=1;sat=0;}break;}   // 峰值保持
                case 8:{double tw=Math.Pow(Math.Max(0,Math.Sin(t*(1.2+3*Hash(k.Slot))+Hash(k.Slot+9)*6.28)),8);hue=Hash(k.Slot+31)*360;v=tw*Math.Min(1,.25+energy*3);break;}  // 星空
                case 9:{double b=Span(bands),flick=Hash((int)(t*14)*131+band*7)*.9;v=b*6+flick*b*2-up;hue=Math.Max(0,42-9*up);break;}   // 火焰
                case 10:{hue=0;v=0;double u=cx+cy*1.2;foreach(var r in sweeps){double age=t-r.Born,d=Math.Abs(u-age*620+80);if(d<110){double a=(1-d/110)*(1-age/1.5);if(a>v){v=a;hue=r.Hue;}}}break;}  // 對角掃描
                default:{int g=Math.Clamp((int)up*n/6,0,n-1);double level=0;for(int i=g;i<Math.Min(n,g+Math.Max(1,n/6));i++)level=Math.Max(level,bands[i]);hue=up*55;v=(cx-Left)/(Pitch*n)<level?1:0;break;}   // 橫向電平
            }
            if(v<=0.02)continue;
            v=Math.Clamp(v,0,1);double colPos=Math.Clamp((cx-Left)/(Pitch*(n-1)),0,1);
            double pos=color.Direction switch{0=>up/5.0,1=>colPos,2=>PingPong(t*.12+colPos*.5),_=>v};
            var (r2,g2,b2)=color.Pick(hue,sat,v,pos);LedFrame.Set(f,k.Slot,r2,g2,b2);
        }
        return f;
    }
}
/// <summary>讀取 keyboard.json 的按鍵座標（不使用反射，Native AOT 可用）。</summary>
public static class KeyLayout {
    public static List<PhysicalKey> Load(string path)=>Parse(File.ReadAllText(path));
    public static List<PhysicalKey> Parse(string json){
        using var doc=JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e=>new PhysicalKey(e.GetProperty("Id").GetInt32(),e.GetProperty("Name").GetString()??"",e.GetProperty("X").GetDouble(),e.GetProperty("Y").GetDouble(),e.GetProperty("Width").GetDouble(),e.GetProperty("Height").GetDouble(),e.GetProperty("Slot").GetInt32())).ToList();
    }
}
/// <summary>在背景持續送出即時燈效。固定顏色每秒重送一次；音樂律動約 30 fps。</summary>
public sealed class LiveLights : IDisposable {
    readonly KeyboardService service;readonly IReadOnlyList<PhysicalKey> keys;
    readonly Thread thread;readonly AutoResetEvent wake=new(false);volatile bool stop;
    volatile byte[]? still,fx;AudioLoopback? audio;
    public volatile int FxSpeed=2;
    public double Gain{get;set;}=40;public volatile int Style;public volatile MusicColor Color=MusicColor.Mix;
    public bool Music=>audio!=null;
    public string? Error{get;private set;}
    public LiveLights(KeyboardService service,IReadOnlyList<PhysicalKey> keys,byte[]? frame){
        this.service=service;this.keys=keys;still=frame;if(frame==null)audio=new AudioLoopback();
        thread=new Thread(Loop){IsBackground=true,Name="live-lights"};thread.Start();
    }
    public void Show(byte[] frame,byte[]? effects=null){if(frame.Length!=LedFrame.Bytes)throw new ArgumentException("無效的燈效框格。");fx=effects!=null&&effects.Any(b=>b!=0)?effects:null;still=frame;wake.Set();}
    void Loop(){
        var level=new float[LedFrame.Bands];var viz=new MusicVisualizer(keys);byte[]? sent=null;var sw=System.Diagnostics.Stopwatch.StartNew();
        while(!stop){
            try{
                if(audio!=null){
                    var now=audio.Bands(LedFrame.Bands,Gain);
                    for(int i=0;i<level.Length;i++)level[i]=Math.Max(now[i],level[i]*.82f);
                    var frame=viz.Render(Style,level,sw.Elapsed.TotalSeconds,Color);
                    if(sent==null || !frame.AsSpan().SequenceEqual(sent)){service.SendFrame(frame);sent=frame;}
                    Error=audio.Error;Thread.Sleep(33);   // 每秒約 30 格；送太快會擠滿鍵盤的 USB 通道
                }else if(fx is {} effects){   // 有逐鍵行為時約 30 fps 動畫
                    var frame=LedFrame.Animate(still!,effects,sw.Elapsed.TotalSeconds,FxSpeed);
                    if(sent==null || !frame.AsSpan().SequenceEqual(sent)){service.SendFrame(frame);sent=frame;}
                    Error=null;wake.WaitOne(33);
                }else{service.SendFrame(still!);sent=null;Error=null;wake.WaitOne(1000);}
            }catch(Exception e){Error=e.Message;Thread.Sleep(1000);try{service.Scan();}catch(Exception){}}
        }
    }
    public void Dispose(){stop=true;wake.Set();thread.Join(1500);audio?.Dispose();wake.Dispose();}
}
