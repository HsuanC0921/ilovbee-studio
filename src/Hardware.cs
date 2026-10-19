using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace iLovbeeStudio;

public sealed record DeviceInfo(string Path, ushort Vid, ushort Pid, ushort Release, ushort UsagePage, ushort Usage, ushort FeatureLength, string Product, string Manufacturer);
public sealed record PowerInfo(byte Status, byte Level, DateTimeOffset ReadAt);
public sealed record DeviceProfile(int Schema, string Device, string Release, DateTimeOffset Created, Dictionary<int,string> Keymaps, string Lighting);
public sealed record PhysicalKey(int Id, string Name, double X, double Y, double Width, double Height, int Slot);
public static class Store {
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iLovbeeStudio");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };   // 僅供非 AOT 的診斷用途；AOT 一律使用 AppJson
    public static string Write<T>(string directory, string name, T value) {
        string folder = Path.Combine(Root, directory); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, name);
        string temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(AppJson.Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException(typeof(T).Name))), Encoding.UTF8); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return file;
    }
    public static DeviceProfile Load(string path) {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("設定檔超過大小上限。");
        var profile = JsonSerializer.Deserialize(File.ReadAllText(path), AppJson.Default.DeviceProfile) ?? throw new InvalidDataException("無效的設定檔。");
        Validate(profile); return profile;
    }
    public static void Validate(DeviceProfile profile) {
        if (profile.Schema != 1 || profile.Device != "258A:0199" || profile.Release != "2010")
            throw new InvalidDataException("此設定檔不適用目前 B87 硬體版本。");
        if (profile.Keymaps is null || profile.Keymaps.Count is not (3 or 4) || !Enumerable.Range(0,profile.Keymaps.Count).All(profile.Keymaps.ContainsKey))
            throw new InvalidDataException("設定檔必須包含完整三層按鍵映射。");
        foreach (var map in profile.Keymaps) {
            if (map.Value is null || map.Value.Length != 832 || Convert.FromHexString(map.Value).Length != 416)
                throw new InvalidDataException("按鍵資料長度不正確。");
        }
        if (profile.Lighting is null || profile.Lighting.Length != 256 || Convert.FromHexString(profile.Lighting).Length != 128)
            throw new InvalidDataException("燈效資料長度不正確。");
    }
}
public static class NativeHid {
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort Vid, Pid, Release; }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort LinkCount, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid,string? enumerator,IntPtr parent,int flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid guid,int index,ref InterfaceData data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,ref InterfaceData data,IntPtr detail,int size,out int needed,IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attrs);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetProductString(SafeFileHandle h,byte[] value,int size);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetManufacturerString(SafeFileHandle h,byte[] value,int size);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_SetFeature(SafeFileHandle h,byte[] value,int size);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetFeature(SafeFileHandle h,byte[] value,int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    /// <summary>寫入設定前先等所有鍵盤按鍵放開（最多 maxMs），避免設定封包與按鍵放開訊號重疊，造成最後一個鍵被重複送出。</summary>
    public static void WaitKeysIdle(int maxMs=1500) {
        var clock=System.Diagnostics.Stopwatch.StartNew();int quiet=0;
        while(clock.ElapsedMilliseconds<maxMs) {
            bool down=false;for(int k=8;k<0xFF && !down;k++)if(k is not (0x10 or 0x11 or 0x12) && (GetAsyncKeyState(k)&0x8000)!=0)down=true;
            quiet=down?0:quiet+10;if(quiet>=150)return;Thread.Sleep(10);
        }
    }
    public static List<DeviceInfo> Enumerate() {
        HidD_GetHidGuid(out Guid guid); IntPtr set = SetupDiGetClassDevsW(ref guid,null,IntPtr.Zero,0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var found = new List<DeviceInfo>();
        try {
            for (int i=0;i<256;i++) {
                var data = new InterfaceData { Size=Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref data)) {
                    int error=Marshal.GetLastWin32Error(); if(error==259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set,ref data,IntPtr.Zero,0,out int size,IntPtr.Zero);
                if(size<8 || size>65536) continue;
                IntPtr ptr=Marshal.AllocHGlobal(size);
                try {
                    Marshal.WriteInt32(ptr,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetailW(set,ref data,ptr,size,out _,IntPtr.Zero)) continue;
                    string path=Marshal.PtrToStringUni(ptr+4) ?? "";
                    if(!path.Contains("vid_258a&pid_0199",StringComparison.OrdinalIgnoreCase)) continue;
                    using var h=Open(path);
                    var attrs = new Attributes { Size=Marshal.SizeOf<Attributes>() };
                    if(!HidD_GetAttributes(h,ref attrs)) continue;
                    if(!HidD_GetPreparsedData(h,out var pp)) continue;
                    Caps caps;
                    try { if(HidP_GetCaps(pp,out caps)<0) continue; }
                    finally { HidD_FreePreparsedData(pp); }
                    var product=new byte[512];var manufacturer=new byte[512];
                    HidD_GetProductString(h,product,product.Length);HidD_GetManufacturerString(h,manufacturer,manufacturer.Length);
                    found.Add(new(path,attrs.Vid,attrs.Pid,attrs.Release,caps.UsagePage,caps.Usage,caps.FeatureLength,
                        Encoding.Unicode.GetString(product).TrimEnd('\0'),Encoding.Unicode.GetString(manufacturer).TrimEnd('\0')));
                } finally { Marshal.FreeHGlobal(ptr); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }
    public static SafeFileHandle Open(string path) {
        var h=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(h.IsInvalid) { int e=Marshal.GetLastWin32Error();h.Dispose();throw new Win32Exception(e,"無法開啟 B87，請確認 USB 連線及其他鍵盤軟體。"); }
        return h;
    }
    public static byte[] Packet(byte command,byte layer,int count) {
        if(count<1 || count>512) throw new ArgumentOutOfRangeException(nameof(count));
        var b=new byte[520];b[0]=6;b[1]=command;b[2]=layer;b[4]=1;b[6]=(byte)count;b[7]=(byte)(count>>8);return b;
    }
    public static byte[] Read(SafeFileHandle h,byte command,byte layer,int count) {
        if(command is not (0x83 or 0x84 or 0x87)) throw new InvalidOperationException("未允許的讀取指令。");
        byte[] request=Packet(command,layer,count);
        if(!HidD_SetFeature(h,request,request.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"裝置查詢失敗。");
        Thread.Sleep(40);var reply=new byte[520];reply[0]=6;
        if(!HidD_GetFeature(h,reply,reply.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"裝置未回應。");
        if(!reply.AsSpan(0,8).SequenceEqual(request.AsSpan(0,8))) throw new IOException("裝置回應不符合查詢；未寫入設定。");
        return reply.AsSpan(8,count).ToArray();
    }
    public static void Write(SafeFileHandle h,byte command,byte layer,byte[] data) {
        if(!((command==3 && data.Length==416 && layer<=3) || (command==4 && data.Length==128 && layer==0) || (command==5 && layer==0 && data.Length>=4 && data.Length<=512) || (command==8 && layer==0 && data.Length==LedFrame.Bytes) || (command==0x0a && layer==0 && data.Length==ColorTable.Bytes)))
            throw new InvalidOperationException("未允許的設定寫入。");
        byte[] b=Packet(command,layer,data.Length);data.CopyTo(b,8);
        if(!HidD_SetFeature(h,b,b.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"寫入失敗；備份仍保留。");
        Thread.Sleep(command switch{5=>200,8=>0,_=>80});
    }
}
public sealed class KeyboardService {
    readonly object gate=new();
    public DeviceInfo? Current { get; private set; }
    public List<DeviceInfo> Scan() {
        lock(gate) {
            var devices=NativeHid.Enumerate();
            var selected=devices.Where(x=>x.FeatureLength==520 && x.UsagePage==0xff00 && x.Usage==1 && x.Release==0x2010).ToList();
            Current=selected.Count==1?selected[0]:null;return devices;
        }
    }
    SafeFileHandle Open() {
        var device=Current ?? throw new InvalidOperationException("未找到相容的 B87 USB 設定介面。請切至有線模式。");
        return NativeHid.Open(device.Path);
    }
    public byte[] ReadMap(int layer) {
        if(layer<0 || layer>3)throw new ArgumentOutOfRangeException(nameof(layer));
        lock(gate) { using var h=Open();return NativeHid.Read(h,0x83,(byte)layer,416); }
    }
    public byte[] ReadLights() { lock(gate){using var h=Open();return NativeHid.Read(h,0x84,0,128);} }
    public PowerInfo ReadPower() { lock(gate){using var h=Open();var b=NativeHid.Read(h,0x87,0,2);return new(b[0],b[1],DateTimeOffset.Now);} }
    public DeviceProfile Capture() {
        lock(gate) {
            var maps=Enumerable.Range(0,4).ToDictionary(i=>i,i=>Convert.ToHexString(ReadMap(i)));
            return new(1,"258A:0199","2010",DateTimeOffset.Now,maps,Convert.ToHexString(ReadLights()));
        }
    }
    /// <summary>建立備份並依設定刪掉多的舊檔。purpose 為 "manual" 是手動備份；自動備份關閉時其餘一律略過並回傳 null。
    /// 寫入失敗的還原用的是記憶體中的寫入前資料，不依賴這些檔案。</summary>
    public string? Backup(string purpose,DeviceProfile? snapshot=null) {
        var s=BackupSettings.Load();
        if(purpose!="manual" && !s.Auto)return null;
        string file=Store.Write("backups",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+purpose+".json",snapshot??Capture());
        Prune(s);return file;
    }
    public static void Prune(BackupSettings s) {
        try{
            var all=Directory.GetFiles(Path.Combine(Store.Root,"backups"),"*.json").OrderDescending().ToList();
            foreach(var old in all.Where(BackupSettings.IsManual).Skip(s.KeepManual).Concat(all.Where(f=>!BackupSettings.IsManual(f)).Skip(s.KeepAuto)))File.Delete(old);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static void CheckConflict() {
        foreach(var p in System.Diagnostics.Process.GetProcessesByName("DeviceDriver")) {
            using(p) {
                try { if(p.MainModule?.FileName.Contains("iLovbee",StringComparison.OrdinalIgnoreCase)==true)
                    throw new InvalidOperationException("請先結束原廠／舊版 iLovbee 程式，再套用設定，避免兩個程式互相覆寫。"); }
                catch(Win32Exception) { throw new InvalidOperationException("有其他 DeviceDriver 程式正在執行，請先確認並結束鍵盤設定軟體。"); }
            }
        }
    }
    public void ApplyMappings(Dictionary<int,byte[]> expected,Dictionary<int,byte[]> desired) {
        MappingEditor.Validate(expected);MappingEditor.Validate(desired);
        foreach(int layer in expected.Keys)if(!expected[layer].AsSpan(59*4,4).SequenceEqual(desired[layer].AsSpan(59*4,4)))throw new ArgumentException("FN 鍵必須保留。");
        lock(gate){
            NativeHid.WaitKeysIdle();CheckConflict();var before=Capture();
            if(expected.Any(p=>!Convert.FromHexString(before.Keymaps[p.Key]).SequenceEqual(p.Value)))throw new IOException("鍵盤設定已變更。請先匯出草稿，再重新讀取鍵盤。");
            var changed=expected.Keys.Where(i=>!expected[i].SequenceEqual(desired[i])).Order().ToArray();if(changed.Length==0)return;
            Backup("before-keymaps",before);
            using var h=Open();var attempted=new List<int>();
            try{
                foreach(int layer in changed){attempted.Add(layer);NativeHid.Write(h,3,(byte)layer,desired[layer]);if(!NativeHid.Read(h,0x83,(byte)layer,416).SequenceEqual(desired[layer]))throw new IOException("按鍵設定讀回不符。");}
                foreach(int layer in expected.Keys)if(!NativeHid.Read(h,0x83,(byte)layer,416).SequenceEqual(desired[layer]))throw new IOException("完整映射核對失敗。");
            }catch(Exception original){
                try{foreach(int layer in attempted.AsEnumerable().Reverse()){NativeHid.Write(h,3,(byte)layer,expected[layer]);if(!NativeHid.Read(h,0x83,(byte)layer,416).SequenceEqual(expected[layer]))throw new IOException("回復讀回不符。");}}
                catch(Exception rollback){throw new IOException("套用失敗，且回復未能確認。請保留備份並重新連接鍵盤。",new AggregateException(original,rollback));}
                throw new IOException("套用失敗，已回復原設定。",original);
            }
        }
    }
    public void Remap(int layer,int slot,byte[] expected,byte[] action) {
        if(layer<0 || layer>3 || slot<0 || slot>=104 || action.Length!=4 || expected.Length!=416) throw new ArgumentException("無效的按鍵映射。");
        lock(gate) {
            NativeHid.WaitKeysIdle();CheckConflict();using var h=Open();var before=NativeHid.Read(h,0x83,(byte)layer,416);
            if(!before.SequenceEqual(expected)) throw new IOException("鍵盤設定已在其他地方變更。請重新讀取後再套用。");
            Backup("before-keymap");
            var after=(byte[])before.Clone();action.CopyTo(after,slot*4);
            if(after.SequenceEqual(before)) return;
            try {
                NativeHid.Write(h,3,(byte)layer,after);
                if(!NativeHid.Read(h,0x83,(byte)layer,416).SequenceEqual(after)) throw new IOException("按鍵寫入後驗證失敗。");
            } catch(Exception original) {
                try { NativeHid.Write(h,3,(byte)layer,before);if(!NativeHid.Read(h,0x83,(byte)layer,416).SequenceEqual(before))throw new IOException("原設定讀回不符。"); }
                catch(Exception rollback) { throw new IOException("套用失敗，且還原未能確認。請保留備份並重新連接鍵盤。",new AggregateException(original,rollback)); }
                throw new IOException("套用失敗，已還原原設定。",original);
            }
        }
    }
    /// <summary>寫入巨集緩衝區（指令 5）。原廠流程不讀回巨集，因此此處無法讀回驗證；先備份，且需在按鍵指向巨集之前寫入。</summary>
    public void WriteMacros(byte[] buffer) {
        if(buffer.Length<4 || buffer.Length>MacroCodec.MaxBytes) throw new ArgumentException("無效的巨集資料。");
        lock(gate) { NativeHid.WaitKeysIdle();CheckConflict();using var h=Open();Backup("before-macro");NativeHid.Write(h,5,0,buffer); }
    }
    /// <summary>送出一格即時燈效（指令 8，僅在燈效模式 18 時顯示）。不寫入設定，因此不備份。</summary>
    public void SendFrame(byte[] rgb) { lock(gate){using var h=Open();NativeHid.Write(h,8,0,rgb);} }
    /// <summary>設定某模式色票第 0 格（原廠的「自訂 RGB」）。指令 0x0a 沒有讀取指令，所以以本機保存的表為準整張送出。</summary>
    public void SetCustomColor(int mode,byte[] rgb) {
        lock(gate) {
            NativeHid.WaitKeysIdle();CheckConflict();using var h=Open();
            var table=ColorTable.Load();ColorTable.SetCustom(table,mode,rgb);
            NativeHid.Write(h,0x0a,0,table);ColorTable.Save(table);
        }
    }
    public void SetLights(byte[] expected,byte[] after) {
        if(expected.Length!=128 || after.Length!=128) throw new ArgumentException("無效的燈效設定。");
        lock(gate) {
            NativeHid.WaitKeysIdle();CheckConflict();using var h=Open();var before=NativeHid.Read(h,0x84,0,128);
            if(!before.SequenceEqual(expected)) throw new IOException("燈效設定已變更，請重新讀取。");
            Backup("before-lighting");
            try {
                NativeHid.Write(h,4,0,after);
                if(!NativeHid.Read(h,0x84,0,128).SequenceEqual(after)) throw new IOException("燈效讀回驗證失敗。");
            } catch(Exception original) {
                try { NativeHid.Write(h,4,0,before);if(!NativeHid.Read(h,0x84,0,128).SequenceEqual(before))throw new IOException("還原讀回不符。"); }
                catch(Exception rollback){throw new IOException("燈效套用及還原未能確認，請保留備份。",new AggregateException(original,rollback));}
                throw new IOException("燈效套用失敗，已還原。",original);
            }
        }
    }
    public void Restore(DeviceProfile profile) {
        Store.Validate(profile);
        lock(gate) {
            NativeHid.WaitKeysIdle();CheckConflict();var before=Capture();Backup("before-restore",before);
            void Apply(DeviceProfile p) {
                using var h=Open();
                foreach(var layer in p.Keymaps.OrderBy(x=>x.Key)) {
                    var map=Convert.FromHexString(layer.Value);NativeHid.Write(h,3,(byte)layer.Key,map);
                    if(!NativeHid.Read(h,0x83,(byte)layer.Key,416).SequenceEqual(map))throw new IOException("按鍵還原驗證失敗。");
                }
                var lights=Convert.FromHexString(p.Lighting);NativeHid.Write(h,4,0,lights);
                if(!NativeHid.Read(h,0x84,0,128).SequenceEqual(lights))throw new IOException("燈效還原驗證失敗。");
            }
            try { Apply(profile); }
            catch(Exception original) {
                try { Apply(before); } catch(Exception rollback) { throw new IOException("還原失敗且回復未能確認，請保留備份。",new AggregateException(original,rollback)); }
                throw new IOException("還原失敗，已回復還原前設定。",original);
            }
        }
    }
}
/// <summary>
/// 燈效色票表（指令 0x0a，420 bytes）：20 個模式 × 7 色 × RGB，列索引同 payload[10]。
/// payload 顏色索引 0..6 取該模式這一列的第 0..6 格；第 0 格就是原廠色輪的「自訂 RGB」（預設紅）。
/// </summary>
public static class ColorTable {
    public const int Modes=20,Slots=7,Bytes=Modes*Slots*3;
    static readonly byte[] Standard=Convert.FromHexString("FF000000FF000000FFFFFF00FF00FF00FFFFFFFFFF");   // 原廠預設：紅綠藍黃紫青白
    static string FilePath=>Path.Combine(Store.Root,"colortable.hex");
    public static byte[] Default(){var t=new byte[Bytes];for(int m=1;m<Modes;m++)Standard.CopyTo(t,m*Slots*3);return t;}
    public static byte[] Load(){
        try{var t=Convert.FromHexString(File.ReadAllText(FilePath).Trim());if(t.Length==Bytes)return t;}
        catch(Exception e)when(e is IOException or FormatException){}
        return Default();
    }
    public static void Save(byte[] t){Directory.CreateDirectory(Store.Root);File.WriteAllText(FilePath,Convert.ToHexString(t));}
    public static byte[] Custom(byte[] t,int mode)=>t.AsSpan(mode*Slots*3,3).ToArray();
    public static void SetCustom(byte[] t,int mode,byte[] rgb){
        if(mode<1||mode>=Modes||rgb.Length!=3)throw new ArgumentException("無效的自訂顏色。");
        rgb.CopyTo(t,mode*Slots*3);
    }
}

/// <summary>備份設定（backup.ini）：Auto＝寫入前自動備份；KeepAuto／KeepManual＝自動、手動備份各保留最新幾份。</summary>
public sealed class BackupSettings {
    public const int MaxKeep=100;
    public bool Auto=true;
    public int KeepAuto=5,KeepManual=5;
    static string FilePath=>Path.Combine(Store.Root,"backup.ini");
    public static bool IsManual(string file)=>file.EndsWith("-manual.json",StringComparison.OrdinalIgnoreCase);
    public static BackupSettings Load(){
        var s=new BackupSettings();
        try{
            foreach(var line in File.ReadAllLines(FilePath)){
                int i=line.IndexOf('=');if(i<1)continue;string k=line[..i].Trim(),v=line[(i+1)..].Trim();
                if(k=="auto")s.Auto=v!="0";
                else if(k=="keepauto"&&int.TryParse(v,out int a))s.KeepAuto=Math.Clamp(a,1,MaxKeep);
                else if(k=="keepmanual"&&int.TryParse(v,out int m))s.KeepManual=Math.Clamp(m,1,MaxKeep);
            }
        }catch(IOException){}
        return s;
    }
    public void Save(){Directory.CreateDirectory(Store.Root);File.WriteAllLines(FilePath,new[]{"auto="+(Auto?1:0),"keepauto="+KeepAuto,"keepmanual="+KeepManual});}
}
