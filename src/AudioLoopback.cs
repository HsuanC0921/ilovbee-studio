using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace iLovbeeStudio;
/// <summary>擷取系統播放中的聲音（WASAPI loopback），提供頻段音量給音樂律動燈效。
/// 以 COM 虛擬函式表直接呼叫，才能用在 Native AOT（不依賴 ComImport 的執行階段產生程式碼）。</summary>
public sealed unsafe class AudioLoopback : IDisposable {
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint model);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("ole32.dll")] static extern int CoCreateInstance(Guid* clsid,IntPtr outer,uint context,Guid* iid,void** instance);
    static readonly Guid ClsidEnumerator=new("BCDE0395-E52F-467C-8E3D-C4579291692E"),IidEnumerator=new("A95664D2-9614-4F35-A746-DE8DB63617E6"),
        IidClient=new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),IidCapture=new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    static void* Slot(void* obj,int index)=>(*(void***)obj)[index];
    static void Release(void* obj){if(obj!=null)((delegate* unmanaged[Stdcall]<void*,uint>)Slot(obj,2))(obj);}

    public const int Window=2048;
    readonly float[] ring=new float[Window];int head;
    readonly object sync=new();
    readonly Thread thread;volatile bool stop;
    public int SampleRate{get;private set;}=48000;
    public string? Error{get;private set;}
    public AudioLoopback(){thread=new Thread(Loop){IsBackground=true,Name="audio-loopback"};thread.SetApartmentState(ApartmentState.MTA);thread.Start();}
    void Loop(){
        void* enumerator=null,device=null,client=null,capture=null;IntPtr format=IntPtr.Zero;bool com=false;
        try{
            com=CoInitializeEx(IntPtr.Zero,0)>=0;
            Guid clsid=ClsidEnumerator,iid=IidEnumerator;
            Check(CoCreateInstance(&clsid,IntPtr.Zero,1,&iid,&enumerator));
            Check(((delegate* unmanaged[Stdcall]<void*,int,int,void**,int>)Slot(enumerator,4))(enumerator,0,0,&device));          // GetDefaultAudioEndpoint(eRender,eConsole)
            iid=IidClient;Check(((delegate* unmanaged[Stdcall]<void*,Guid*,uint,void*,void**,int>)Slot(device,3))(device,&iid,1,null,&client)); // Activate
            IntPtr fmt;Check(((delegate* unmanaged[Stdcall]<void*,IntPtr*,int>)Slot(client,8))(client,&fmt));format=fmt;                // GetMixFormat
            int channels=Marshal.ReadInt16(format,2),bits=Marshal.ReadInt16(format,14);SampleRate=Marshal.ReadInt32(format,4);
            if(bits is not (16 or 32))throw new NotSupportedException("不支援的音訊格式。");
            Check(((delegate* unmanaged[Stdcall]<void*,int,uint,long,long,IntPtr,IntPtr,int>)Slot(client,3))(client,0,0x00020000,10_000_000,0,format,IntPtr.Zero)); // Initialize(shared, loopback)
            iid=IidCapture;Check(((delegate* unmanaged[Stdcall]<void*,Guid*,void**,int>)Slot(client,14))(client,&iid,&capture));         // GetService
            Check(((delegate* unmanaged[Stdcall]<void*,int>)Slot(client,10))(client));                                                    // Start
            while(!stop){
                Thread.Sleep(15);
                uint packet;
                while(((delegate* unmanaged[Stdcall]<void*,uint*,int>)Slot(capture,5))(capture,&packet)>=0 && packet>0){
                    byte* data;uint frames,flags;ulong pos,qpc;
                    Check(((delegate* unmanaged[Stdcall]<void*,byte**,uint*,uint*,ulong*,ulong*,int>)Slot(capture,3))(capture,&data,&frames,&flags,&pos,&qpc));
                    lock(sync)for(int i=0;i<frames;i++){
                        float sum=0;
                        if((flags&2)==0)for(int c=0;c<channels;c++)sum+=bits==32?((float*)data)[i*channels+c]:((short*)data)[i*channels+c]/32768f;
                        ring[head]=sum/channels;head=(head+1)%Window;
                    }
                    Check(((delegate* unmanaged[Stdcall]<void*,uint,int>)Slot(capture,4))(capture,frames));                                // ReleaseBuffer
                }
            }
            ((delegate* unmanaged[Stdcall]<void*,int>)Slot(client,11))(client);                                                            // Stop
        }catch(Exception e){Error=e.Message;}
        finally{
            Release(capture);Release(client);Release(device);Release(enumerator);
            if(format!=IntPtr.Zero)Marshal.FreeCoTaskMem(format);if(com)CoUninitialize();
        }
    }
    /// <summary>以 Goertzel 計算對數分布頻段（60 Hz–12 kHz）的強度 0..1；gain 為靈敏度。</summary>
    public float[] Bands(int count,double gain){
        var samples=new float[Window];
        lock(sync)for(int i=0;i<Window;i++)samples[i]=ring[(head+i)%Window];
        var result=new float[count];
        for(int b=0;b<count;b++){
            double f=60*Math.Pow(12000/60.0,(b+.5)/count),w=2*Math.PI*f/SampleRate,k=2*Math.Cos(w),s1=0,s2=0;
            for(int i=0;i<Window;i++){double hann=.5-.5*Math.Cos(2*Math.PI*i/(Window-1));double s=samples[i]*hann+k*s1-s2;s2=s1;s1=s;}
            double power=Math.Sqrt(Math.Max(0,s1*s1+s2*s2-k*s1*s2))/(Window/4.0);
            // 高頻能量較低，依頻段略為補償。
            result[b]=(float)Math.Clamp(Math.Sqrt(power*gain*(1+2.0*b/count)),0,1);
        }
        return result;
    }
    public void Dispose(){stop=true;thread.Join(1500);}
}
