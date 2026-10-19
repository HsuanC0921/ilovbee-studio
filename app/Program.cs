using System.Diagnostics;
using System.Runtime.InteropServices;
using iLovbeeStudio;

internal static unsafe class Program {
    const uint Callback=0x8001;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr Procedure(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass {public uint Size,Style;public IntPtr Proc;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;public string? Menu,Name;public IntPtr SmallIcon;}
    [StructLayout(LayoutKind.Sequential)] struct Message {public IntPtr Hwnd;public uint Id;public IntPtr Wparam,Lparam;public uint Time;public int X,Y;public uint Private;}
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrayData {public uint Size;public IntPtr Hwnd;public uint Id,Flags,Message;public IntPtr Icon;public fixed char Tip[128];public uint State,StateMask;public fixed char Info[256];public uint Timeout;public fixed char InfoTitle[64];public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex,string className,string name,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd,uint msg,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")] static extern int GetMessageW(out Message msg,IntPtr hwnd,uint min,uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref Message msg);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr hwnd,UIntPtr id,uint interval,IntPtr procedure);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hwnd,UIntPtr id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint message,ref TrayData data);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu,uint flags,UIntPtr id,string? text);
    [DllImport("user32.dll")] static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr hwnd,IntPtr rect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point pt);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")] static extern IntPtr CreateIcon(IntPtr instance,int width,int height,byte planes,byte bits,byte[] andMask,byte[] xorMask);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
    static readonly Procedure proc=WindowProc;
    static TrayData tray;
    static WindowsBatteryReading? battery;
    static int? lastNotice;
    static uint taskbarCreated;
    static LiveController? controller;static Api? api;static MiniHttp? http;static DateTime configStamp;static List<(string Name,byte[] Frame)> layouts=new();
    static void Put(char* target,int capacity,string text){int n=Math.Min(capacity-1,text.Length);for(int i=0;i<n;i++)target[i]=text[i];target[n]='\0';}
    [STAThread] static void Main(){
        using var mutex=new Mutex(true,@"Local\iLovbeeStudioWidget",out bool first);if(!first){OpenUiOfRunningInstance();return;}
        taskbarCreated=RegisterWindowMessageW("TaskbarCreated");
        var wc=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Proc=Marshal.GetFunctionPointerForDelegate(proc),Instance=GetModuleHandleW(null),Name="iLovbeeBatteryWidget"};
        if(RegisterClassExW(ref wc)==0)return;
        IntPtr window=CreateWindowExW(0,wc.Name!,"iLovbee 電量小工具",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,wc.Instance,IntPtr.Zero);if(window==IntPtr.Zero)return;
        tray.Size=(uint)sizeof(TrayData);tray.Hwnd=window;tray.Id=1;tray.Flags=7;tray.Message=Callback;
        Update(0);SetTimer(window,(UIntPtr)1,60000,IntPtr.Zero);SetTimer(window,(UIntPtr)2,500,IntPtr.Zero);
        var service=new KeyboardService();
        try{controller=new LiveController(service);var c=LiveConfig.Load();configStamp=LiveConfig.Stamp;if(c.Mode!="off")controller.Apply(c);}catch(Exception e)when(e is IOException or System.Text.Json.JsonException){}
        if(controller!=null){
            api=new Api(service,controller,()=>{configStamp=default;PollConfig();});
            http=new MiniHttp(api.Handle);http.Start();api.Port=http.Port;
            Directory.CreateDirectory(Store.Root);File.WriteAllLines(Path.Combine(Store.Root,"server.txt"),new[]{http.Port.ToString(),api.Token});
            if(!Environment.GetCommandLineArgs().Contains("--tray"))OpenUi();   // 開機自動啟動時只常駐在系統匣
        }
        while(GetMessageW(out var message,IntPtr.Zero,0,0)>0){TranslateMessage(ref message);DispatchMessageW(ref message);}
        KillTimer(window,(UIntPtr)1);KillTimer(window,(UIntPtr)2);http?.Stop();controller?.Dispose();Shell_NotifyIconW(2,ref tray);if(tray.Icon!=IntPtr.Zero)DestroyIcon(tray.Icon);
    }
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index,uint dpi);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    /// <summary>依系統 DPI 取得托盤圖示尺寸（SM_CXSMICON），再把 16×16 的電池圖樣放大到該尺寸。</summary>
    static IntPtr MakeIcon(int? level){
        int size=Math.Max(16,GetSystemMetricsForDpi(49,GetDpiForSystem()));
        var rgba=new byte[size*size*4];var mask=Enumerable.Repeat((byte)255,size*((size+7)/8)).ToArray();
        for(int py=0;py<size;py++)for(int px=0;px<size;px++){
            int x=px*16/size,y=py*16/size;
            bool border=(x==1||x==13||y==3||y==12)&&x<=13&&y>=3&&y<13&&x>=1;
            bool cap=x==14&&y>=6&&y<=9;
            bool fill=level is int value&&x>=3&&x<3+(int)Math.Round(value/100.0*9)&&y>=5&&y<=10;
            if(!border&&!cap&&!fill)continue;int at=(py*size+px)*4;
            rgba[at]=(byte)(level<=20?45:189);rgba[at+1]=(byte)(level<=20?55:90);rgba[at+2]=(byte)(level<=20?200:24);rgba[at+3]=255;
            mask[py*((size+7)/8)+px/8]&=(byte)~(0x80>>(px%8));
        }return CreateIcon(IntPtr.Zero,size,size,1,32,mask,rgba);
    }
    static void Update(uint command=1){
        battery=WindowsBattery.Read();IntPtr previous=tray.Icon;tray.Icon=MakeIcon(battery?.Percent);
        string text=battery==null?"B87 · 尚未取得電量":"B87 · "+battery.Percent+"% · Windows 最近回報 "+(battery.ReportedAt?.ToLocalTime().ToString("HH:mm")??"");
        fixed(char* tip=tray.Tip)Put(tip,128,text);tray.Flags=7;Shell_NotifyIconW(command,ref tray);if(previous!=IntPtr.Zero)DestroyIcon(previous);
        bool recent=battery?.ReportedAt is DateTimeOffset at && DateTimeOffset.Now-at<TimeSpan.FromMinutes(10);
        if(recent&&battery!.Percent<=20&&lastNotice!=battery.Percent){tray.Flags=0x10;fixed(char* title=tray.InfoTitle)Put(title,64,"B87 電量偏低");fixed(char* info=tray.Info)Put(info,256,$"剩餘電量 {battery.Percent}%，請接上充電線。");tray.InfoFlags=2;Shell_NotifyIconW(1,ref tray);lastNotice=battery.Percent;}
        if(battery?.Percent>20)lastNotice=null;
    }
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    static bool AutoStart(){using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);return key?.GetValue("iLovbeeStudio") is string;}
    static void SetAutoStart(bool on){
        using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if(on)key.SetValue("iLovbeeStudio","\""+Environment.ProcessPath+"\" --tray");else key.DeleteValue("iLovbeeStudio",false);key.DeleteValue("iLovbeeWidget",false);
    }
    /// <summary>設定檔有變動時套用（設定介面與托盤選單都透過 live.ini 溝通）。</summary>
    static void PollConfig(){
        var stamp=LiveConfig.Stamp;if(stamp==configStamp||controller==null)return;
        configStamp=stamp;controller.Apply(LiveConfig.Load());
    }
    /// <summary>以 Edge 的應用程式視窗（無網址列）開啟設定介面；沒有 Edge 時改用預設瀏覽器。</summary>
    static void OpenUi(){if(http!=null&&api!=null)LaunchUi(http.Port,api.Token);}
    static void OpenUiOfRunningInstance(){
        try{
            var lines=File.ReadAllLines(Path.Combine(Store.Root,"server.txt"));int port=int.Parse(lines[0]);
            using var probe=new System.Net.Sockets.TcpClient();probe.Connect("127.0.0.1",port);LaunchUi(port,lines[1]);
        }catch(Exception e)when(e is IOException or FormatException or IndexOutOfRangeException or System.Net.Sockets.SocketException){}
    }
    static void LaunchUi(int port,string token){
        string url=$"http://127.0.0.1:{port}/?t={token}";
        string[] candidates={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Microsoft\Edge\Application\msedge.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"Microsoft\Edge\Application\msedge.exe")};
        string? edge=candidates.FirstOrDefault(File.Exists);
        if(edge==null){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return;}
        string profile=Path.Combine(Store.Root,"web");
        Process.Start(new ProcessStartInfo(edge,$"--app={url} --user-data-dir=\"{profile}\" --window-size=1200,860 --no-first-run --disable-extensions --disable-features=Translate --disable-background-networking --renderer-process-limit=1 --in-process-gpu"){UseShellExecute=false});
    }
    static IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam){
        if(message==taskbarCreated && taskbarCreated!=0){Update(0);return IntPtr.Zero;}
        if(message==0x113){if(wparam.ToInt64()==2){PollConfig();return IntPtr.Zero;}Update();return IntPtr.Zero;}
        if(message==Callback){uint action=(uint)lparam&0xffff;if(action==0x203){OpenUi();return IntPtr.Zero;}if(action==0x205||action==0x7b){
            IntPtr menu=CreatePopupMenu();var cfg=LiveConfig.Load();layouts=LiveConfig.Layouts();
            AppendMenuW(menu,2,(UIntPtr)10,battery==null?"B87 · 尚未取得電量":$"B87 · 電量 {battery.Percent}%");
            AppendMenuW(menu,2,(UIntPtr)11,"燈光：" + (controller?.Status ?? "未執行"));AppendMenuW(menu,0x800,UIntPtr.Zero,null);
            IntPtr music=CreatePopupMenu();for(int i=0;i<LedFrame.MusicStyles.Length;i++)AppendMenuW(music,(cfg.Mode=="music"&&cfg.Style==i)?8u:0u,(UIntPtr)(20+i),LedFrame.MusicStyles[i]);
            AppendMenuW(menu,0x10,(UIntPtr)music,"音樂律動");
            IntPtr custom=CreatePopupMenu();AppendMenuW(custom,cfg.Mode=="custom"?8u:0u,(UIntPtr)40,"目前的自訂燈光");for(int i=0;i<layouts.Count && i<20;i++)AppendMenuW(custom,0,(UIntPtr)(100+i),layouts[i].Name);
            AppendMenuW(menu,0x10,(UIntPtr)custom,"自訂燈光");
            AppendMenuW(menu,0,(UIntPtr)41,"停止並恢復原本燈效");AppendMenuW(menu,0x800,UIntPtr.Zero,null);
            AppendMenuW(menu,0,(UIntPtr)1,"開啟設定");AppendMenuW(menu,0,(UIntPtr)2,"更新電量");AppendMenuW(menu,0,(UIntPtr)3,"Windows 藍牙設定");
            AppendMenuW(menu,AutoStart()?8u:0u,(UIntPtr)42,"開機自動啟動");AppendMenuW(menu,0x800,UIntPtr.Zero,null);AppendMenuW(menu,0,(UIntPtr)4,"結束小工具");
            GetCursorPos(out var point);SetForegroundWindow(hwnd);uint chosen=TrackPopupMenu(menu,0x100|2,point.X,point.Y,0,hwnd,IntPtr.Zero);DestroyMenu(menu);DestroyMenu(music);DestroyMenu(custom);
            if(chosen is >=20 and <26){cfg.Mode="music";cfg.Style=(int)chosen-20;cfg.Save();PollConfig();return IntPtr.Zero;}
            if(chosen==40){cfg.Mode="custom";cfg.Save();PollConfig();return IntPtr.Zero;}
            if(chosen>=100 && chosen-100<layouts.Count){cfg.Mode="custom";cfg.Frame=layouts[(int)chosen-100].Frame;cfg.Save();PollConfig();return IntPtr.Zero;}
            if(chosen==41){cfg.Mode="off";cfg.Save();PollConfig();return IntPtr.Zero;}
            if(chosen==42){SetAutoStart(!AutoStart());return IntPtr.Zero;}
            if(chosen==1)OpenUi();else if(chosen==2)Update();else if(chosen==3)Process.Start(new ProcessStartInfo("ms-settings:bluetooth"){UseShellExecute=true});else if(chosen==4)DestroyWindow(hwnd);return IntPtr.Zero;
        }}
        if(message==2){PostQuitMessage(0);return IntPtr.Zero;}return DefWindowProcW(hwnd,message,wparam,lparam);
    }
}
