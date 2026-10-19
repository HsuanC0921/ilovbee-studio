using System;
using Windows.UI;

namespace iLovbeeStudio;

/// <summary>燈效示意圖的軟體模擬（花開富貴、霓虹流動、左右掃描、旋轉風車使用原廠 B87 動畫分組檔的按鍵分群）；索引與鍵盤 payload[10] 的模式值相同。實際效果以鍵盤韌體為準。</summary>
public static class LightEffects {
    public const double Width=789,Height=313;
    public const int KeyCount=87;
    public static readonly string[] Names={"關閉主背光","恆亮","呼吸","夢幻彩虹","一觸即發","雨中漫步","彩虹輪盤","漣漪擴散","繁星閃爍","踏雪無痕","流動貪食蛇","霓虹流動","如影隨形","正弦光波","左右掃描","旋轉風車","七彩瀑布","花開富貴","自訂燈效"};
    public static readonly string[] Hints={
        "主背光全部熄滅，鍵盤輸入不受影響。","所有按鍵維持固定亮度。","整體亮度緩慢漸強、漸弱。","彩虹色彩沿鍵盤平緩流動。",
        "按下的按鍵瞬間亮起，隨即淡出。","光點如雨滴沿各列落下。","色相繞鍵盤中心旋轉。","光圈由隨機位置向外擴散。",
        "按鍵隨機明滅，如同星空。","按過的按鍵緩緩淡出，留下殘影。","光帶沿鍵盤逐列蛇行。","彩色條紋橫向流動。",
        "按鍵亮起後，光向周邊按鍵擴散。","光波沿正弦曲線橫向行進。","光條由左至右來回掃過。","彩色扇葉繞中心旋轉。",
        "色彩由上往下流瀉。","光環自中心綻放並擴大。","使用逐鍵自訂配色。"};

    // 每個模式可用的控制項（來自原廠 KB.ini 的 LedOpt1..17）。模式值同 payload[10]。
    public static bool HasBrightness(int m)=>m is >=1 and <=17;
    public static bool HasSpeed(int m)=>m is >=2 and <=17;
    public static bool HasColor(int m)=>m is 1 or 2 or (>=4 and <=14);
    /// <summary>韌體固定色票（實機逐一確認）：0 紅、1 綠、2 深藍、3 黃、4 紫、5 淺藍、6 白；7 為混色。</summary>
    public static readonly string[] PaletteNames={"紅","綠","深藍","黃","紫","淺藍","白"};
    public static readonly Color[] Palette={Color.FromArgb(255,255,64,64),Color.FromArgb(255,70,230,110),Color.FromArgb(255,40,80,255),Color.FromArgb(255,255,220,0),Color.FromArgb(255,185,70,255),Color.FromArgb(255,80,210,255),Color.FromArgb(255,255,255,255)};
    public static readonly double[] SpeedScale={0,.35,.6,.85,1.2};
    /// <summary>套用色票：colorIndex 0..6 為固定色，7 為混色（保留效果原本的色相）。</summary>
    public static Color SampleTinted(int mode,double t,double x,double y,int ordinal,int colorIndex){
        var c=Sample(mode,t,x,y,ordinal);
        if(!HasColor(mode)||c.A==0)return c;
        if(colorIndex is >=0 and <7){var p=Palette[colorIndex];return Color.FromArgb(c.A,p.R,p.G,p.B);}
        return mode is 1 or 2?Hsv(t*.12,c.A/255.0):c;
    }
    static double Hash(int n){unchecked{uint x=(uint)n*2654435761u;x^=x>>15;x*=2246822519u;x^=x>>13;return (x&0xFFFFFF)/(double)0x1000000;}}
    static double Row(double y)=>y<80?0:y<111?1:y<152?2:y<192?3:y<233?4:5;
    static double Smooth(double d,double width)=>Math.Exp(-d*d/(width*width));
    static Color Rgb(double i,byte r,byte g,byte b)=>Color.FromArgb((byte)Math.Clamp(i*255,0,255),r,g,b);
    public static Color Hsv(double h,double i){
        h-=Math.Floor(h);double s=.9,v=1,k=h*6;int sector=(int)k;double f=k-sector,p=v*(1-s),q=v*(1-s*f),t=v*(1-s*(1-f));
        var (r,g,b)=sector switch{0=>(v,t,p),1=>(q,v,p),2=>(p,v,t),3=>(p,q,v),4=>(t,p,v),_=>(v,p,q)};
        return Color.FromArgb((byte)Math.Clamp(i*255,0,255),(byte)(r*255),(byte)(g*255),(byte)(b*255));
    }
    /// <summary>以隨機「按鍵事件」模擬按下後淡出；回傳 0..1 強度。</summary>
    static double Pulse(double t,int ordinal,double rate,double decay){
        double best=0;int n=(int)Math.Floor(t*rate);
        for(int k=n;k>n-12 && k>=0;k--)if((int)(Hash(k*7+3)*KeyCount)==ordinal)best=Math.Max(best,Math.Exp(-(t-k/rate)/decay));
        return best;
    }
    static double PingPong(double v){v-=Math.Floor(v/2)*2;return v>1?2-v:v;}

    /// <summary>回傳該按鍵在時間 t 的顏色；Alpha 代表亮度強度（0 為熄滅）。</summary>
    public static Color Sample(int mode,double t,double x,double y,int ordinal){
        double dx=x-Width/2,dy=y-165,angle=Math.Atan2(dy,dx),dist=Math.Sqrt(dx*dx+dy*dy);
        switch(mode){
            case 1:return Rgb(1,255,226,170);
            case 2:return Rgb(.12+.88*(.5+.5*Math.Sin(t*2.2-Math.PI/2)),255,110,200);
            case 3:return Hsv(x/Width*.8+y/Height*.2+t*.12,1);
            case 4:return Hsv(Hash(ordinal)+.0,Pulse(t,ordinal,5,.22));
            case 5:{double col=Math.Floor(x/28),head=(t*(1.5+Hash((int)col+50))+Hash((int)col)*8)%9,d=head-Row(y);return Rgb(d>=0&&d<3?1-d/3:0,96,176,255);}
            case 6:return Hsv(angle/(2*Math.PI)+t*.25,1);
            case 7:{double period=2.6,k=Math.Floor(t/period),age=t/period-k;int c=(int)(Hash((int)k)*KeyCount);double cx=60+Hash(c+11)*670,cy=50+Hash(c+23)*220,d=Math.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy));return Hsv(Hash((int)k+5),Smooth(d-age*420,34)*(1-age));}
            case 8:{double phase=Hash(ordinal+400),s=Math.Sin(Math.PI*(t*(.25+.35*Hash(ordinal+900))+phase));return Hsv(Hash(ordinal+77),Math.Pow(Math.Max(0,s),10));}
            case 9:return Rgb(Pulse(t,ordinal,3.2,1.1),170,225,255);
            case 10:{double len=6*Width,pos=Row(y)*Width+(Row(y)%2==0?x:Width-x),head=(t*820)%len,d=((head-pos)%len+len)%len;return Rgb(d<240?1-d/240:0,90,255,140);}
            case 11:{double u=LightGroups.Stream[ordinal]/(double)LightGroups.StreamCount-t*.3;return Hsv(Math.Floor(u*3)/3+.05,.35+.65*Math.Pow(Math.Sin(u*Math.PI*6),2));}
            case 12:{double best=0;int n=(int)Math.Floor(t*3);for(int k=n;k>n-6 && k>=0;k--){int c=(int)(Hash(k*13+5)*KeyCount);double cx=60+Hash(c+11)*670,cy=50+Hash(c+23)*220,d=Math.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy)),age=t-k/3.0;best=Math.Max(best,Math.Exp(-age/1.4)*Smooth(d,50+age*40));}return Rgb(best,255,150,90);}
            case 13:return Hsv(x/Width*.5+.5,Smooth(y-165-70*Math.Sin(x/85-t*3),26));
            case 14:{double bar=PingPong(t*.55)*(LightGroups.StreamCount-1),g=LightGroups.Stream[ordinal];return Hsv(bar/LightGroups.StreamCount*.5+.45,Smooth(g-bar,1.6));}
            case 15:{double n=LightGroups.WheelCount,g=LightGroups.Wheel[ordinal],best=0;for(int arm=0;arm<2;arm++){double d=((t*9+arm*n/2-g)%n+n)%n;best=Math.Max(best,d<7?1-d/7:0);}return Hsv(g/n+t*.1,best);}
            case 16:return Hsv(y/Height*.7-t*.3,1);
            case 17:{double n=LightGroups.FlowerCount,head=(t*4.5)%(n+3),g=LightGroups.Flower[ordinal];return Hsv(head/n,Smooth(g-head,1.2));}
            case 18:return Hsv(Math.Floor(x/Width*7)/7,1);
            default:return Color.FromArgb(0,0,0,0);
        }
    }
}
