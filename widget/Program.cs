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
    static void Put(char* target,int capacity,string text){int n=Math.Min(capacity-1,text.Length);for(int i=0;i<n;i++)target[i]=text[i];target[n]='\0';}
    [STAThread] static void Main(){
        using var mutex=new Mutex(true,@"Local\iLovbeeStudioWidget",out bool first);if(!first)return;
        taskbarCreated=RegisterWindowMessageW("TaskbarCreated");
        var wc=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Proc=Marshal.GetFunctionPointerForDelegate(proc),Instance=GetModuleHandleW(null),Name="iLovbeeBatteryWidget"};
        if(RegisterClassExW(ref wc)==0)return;
        IntPtr window=CreateWindowExW(0,wc.Name!,"iLovbee 電量小工具",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,wc.Instance,IntPtr.Zero);if(window==IntPtr.Zero)return;
        tray.Size=(uint)sizeof(TrayData);tray.Hwnd=window;tray.Id=1;tray.Flags=7;tray.Message=Callback;
        Update(0);SetTimer(window,(UIntPtr)1,60000,IntPtr.Zero);
        while(GetMessageW(out var message,IntPtr.Zero,0,0)>0){TranslateMessage(ref message);DispatchMessageW(ref message);}
        KillTimer(window,(UIntPtr)1);Shell_NotifyIconW(2,ref tray);if(tray.Icon!=IntPtr.Zero)DestroyIcon(tray.Icon);
    }
    static IntPtr MakeIcon(int? level){
        var rgba=new byte[16*16*4];var mask=Enumerable.Repeat((byte)255,64).ToArray();
        for(int y=3;y<13;y++)for(int x=1;x<15;x++){
            bool border=(x==1||x==13||y==3||y==12)&&x<=13;
            bool cap=x==14&&y>=6&&y<=9;
            bool fill=level is int value&&x>=3&&x<3+(int)Math.Round(value/100.0*9)&&y>=5&&y<=10;
            if(!border&&!cap&&!fill)continue;int at=(y*16+x)*4;
            rgba[at]=(byte)(level<=20?45:189);rgba[at+1]=(byte)(level<=20?55:90);rgba[at+2]=(byte)(level<=20?200:24);rgba[at+3]=255;
            mask[y*4+x/8]&=(byte)~(0x80>>(x%8));
        }return CreateIcon(IntPtr.Zero,16,16,1,32,mask,rgba);
    }
    static void Update(uint command=1){
        battery=WindowsBattery.Read();IntPtr previous=tray.Icon;tray.Icon=MakeIcon(battery?.Percent);
        string text=battery==null?"B87 · 尚未取得電量":"B87 · "+battery.Percent+"% · Windows 最近回報 "+(battery.ReportedAt?.ToLocalTime().ToString("HH:mm")??"");
        fixed(char* tip=tray.Tip)Put(tip,128,text);tray.Flags=7;Shell_NotifyIconW(command,ref tray);if(previous!=IntPtr.Zero)DestroyIcon(previous);
        bool recent=battery?.ReportedAt is DateTimeOffset at && DateTimeOffset.Now-at<TimeSpan.FromMinutes(10);
        if(recent&&battery!.Percent<=20&&lastNotice!=battery.Percent){tray.Flags=0x10;fixed(char* title=tray.InfoTitle)Put(title,64,"B87 電量偏低");fixed(char* info=tray.Info)Put(info,256,$"剩餘電量 {battery.Percent}%，請接上充電線。");tray.InfoFlags=2;Shell_NotifyIconW(1,ref tray);lastNotice=battery.Percent;}
        if(battery?.Percent>20)lastNotice=null;
    }
    static void OpenStudio(){
        foreach(var process in Process.GetProcessesByName("iLovbeeStudio.WinUI")){using(process){if(process.MainWindowHandle!=IntPtr.Zero){ShowWindow(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);return;}}}
        string path=Path.Combine(AppContext.BaseDirectory,"iLovbeeStudio.WinUI.exe");
        if(!File.Exists(path)){string saved=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"iLovbeeStudio","program-path.txt");if(File.Exists(saved))path=File.ReadAllText(saved).Trim();}
        if(Path.GetFileName(path)=="iLovbeeStudio.WinUI.exe"&&File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    }
    static IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam){
        if(message==taskbarCreated && taskbarCreated!=0){Update(0);return IntPtr.Zero;}
        if(message==0x113){Update();return IntPtr.Zero;}
        if(message==Callback){uint action=(uint)lparam&0xffff;if(action==0x203){OpenStudio();return IntPtr.Zero;}if(action==0x205||action==0x7b){
            IntPtr menu=CreatePopupMenu();AppendMenuW(menu,2,(UIntPtr)10,battery==null?"B87 · 尚未取得電量":$"B87 · 電量 {battery.Percent}%");AppendMenuW(menu,0x800,UIntPtr.Zero,null);AppendMenuW(menu,0,(UIntPtr)1,"開啟 iLovbee Studio");AppendMenuW(menu,0,(UIntPtr)2,"更新電量");AppendMenuW(menu,0,(UIntPtr)3,"Windows 藍牙設定");AppendMenuW(menu,0x800,UIntPtr.Zero,null);AppendMenuW(menu,0,(UIntPtr)4,"結束小工具");GetCursorPos(out var point);SetForegroundWindow(hwnd);uint chosen=TrackPopupMenu(menu,0x100|2,point.X,point.Y,0,hwnd,IntPtr.Zero);DestroyMenu(menu);
            if(chosen==1)OpenStudio();else if(chosen==2)Update();else if(chosen==3)Process.Start(new ProcessStartInfo("ms-settings:bluetooth"){UseShellExecute=true});else if(chosen==4)DestroyWindow(hwnd);return IntPtr.Zero;
        }}
        if(message==2){PostQuitMessage(0);return IntPtr.Zero;}return DefWindowProcW(hwnd,message,wparam,lparam);
    }
}
