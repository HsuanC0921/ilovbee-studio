using iLovbeeStudio;

/// <summary>依 live.ini 控制即時燈效；切到自訂模式前記下原本的燈效模式，停止或結束時還原。</summary>
internal sealed class LiveController : IDisposable {
    readonly KeyboardService service;
    readonly List<PhysicalKey> keys;
    LiveLights? live;
    static string RestoreFile=>Path.Combine(Store.Root,"live-restore.txt");
    public string Status{get;private set;}="未執行";
    public LiveController(KeyboardService service){
        this.service=service;
        keys=KeyLayout.Parse(System.Text.Encoding.UTF8.GetString(Api.Resource("keyboard.json")));
        // 上次異常結束時，鍵盤可能還停在自訂模式；先還原。
        try{Restore();}catch(Exception e)when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){}
    }
    public void Apply(LiveConfig c){
        try{
            if(c.Mode=="off"){Stop();return;}
            bool music=c.Mode=="music";
            if(live==null || live.Music!=music){live?.Dispose();live=null;EnterCustomMode();live=new LiveLights(service,keys,music?null:(byte[])c.Frame.Clone());}
            live.Gain=c.Gain;live.Style=c.Style;live.Color=c.Palette;live.FxSpeed=c.FxSpeed;if(!music)live.Show((byte[])c.Frame.Clone(),(byte[])c.Fx.Clone());
            Status=music?"音樂律動 · "+LedFrame.MusicStyles[c.Style]:"自訂燈光";
        }catch(Exception e)when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException){Status="錯誤："+e.Message;}
    }
    void EnterCustomMode(){
        if(service.Current==null)service.Scan();
        var before=service.ReadLights();
        if(before[10]==LedFrame.CustomMode)return;
        File.WriteAllText(RestoreFile,before[10].ToString());
        var after=(byte[])before.Clone();after[10]=LedFrame.CustomMode;service.SetLights(before,after);
    }
    void Restore(){
        if(!File.Exists(RestoreFile))return;
        if(service.Current==null)service.Scan();
        if(service.Current==null)return;
        byte mode=byte.Parse(File.ReadAllText(RestoreFile).Trim());
        var now=service.ReadLights();
        if(now[10]==LedFrame.CustomMode){var after=(byte[])now.Clone();after[10]=mode;service.SetLights(now,after);}
        File.Delete(RestoreFile);
    }
    public void Stop(){live?.Dispose();live=null;Restore();Status="未執行";}
    public void Dispose(){try{Stop();}catch(Exception e)when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){}}
}
