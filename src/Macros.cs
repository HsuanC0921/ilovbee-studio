using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace iLovbeeStudio;
/// <summary>巨集事件：Code 為 HID 用途碼（0xE0..0xE7 為修飾鍵），Delay 為此事件之後的等待毫秒。</summary>
public sealed record MacroEvent(byte Code,bool Up,int Delay);
public sealed record Macro(string Name,List<MacroEvent> Events);
/// <summary>巨集緩衝區編碼，格式由原廠程式的實機封包確認（見 docs/firmware.md）。</summary>
public static class MacroCodec {
    public const int MaxBytes=512,MaxMacros=8,MaxEvents=60,MaxName=20;
    // 原廠表格（HID 用途碼 ↔ Windows 虛擬鍵碼），取自原廠程式 0x5D2EB8。
    static readonly (byte Hid,byte Vk)[] Table={(0x64,0xE2),(0x29,0x1B),(0x3A,0x70),(0x3B,0x71),(0x3C,0x72),(0x3D,0x73),(0x3E,0x74),(0x3F,0x75),(0x40,0x76),(0x41,0x77),(0x42,0x78),(0x43,0x79),(0x44,0x7A),(0x45,0x7B),
        (0x2B,0x09),(0x39,0x14),(0x28,0x0D),(0x2A,0x08),(0x2C,0x20),(0x2F,0xDB),(0x30,0xDD),(0x31,0xDC),(0x33,0xBA),(0x34,0xDE),(0x36,0xBC),(0x37,0xBE),(0x38,0xBF),(0x35,0xC0),(0x2E,0xBB),(0x2D,0xBD),
        (0x46,0x2C),(0x47,0x91),(0x48,0x13),(0x49,0x2D),(0x4A,0x24),(0x4B,0x21),(0x4C,0x2E),(0x4D,0x23),(0x4E,0x22),(0x52,0x26),(0x51,0x28),(0x50,0x25),(0x4F,0x27),
        (0x59,0x61),(0x5A,0x62),(0x5B,0x63),(0x5C,0x64),(0x5D,0x65),(0x5E,0x66),(0x5F,0x67),(0x60,0x68),(0x61,0x69),(0x62,0x60),(0x57,0x6B),(0x56,0x6D),(0x55,0x6A),(0x54,0x6F),(0x53,0x90),(0x63,0x6E),
        (0xE0,0xA2),(0xE1,0xA0),(0xE2,0xA4),(0xE3,0x5B),(0xE4,0xA3),(0xE5,0xA1),(0xE6,0xA5),(0xE7,0x5C),(0x65,0x5D)};
    static readonly Dictionary<int,byte> vkToHid=BuildMap();
    static Dictionary<int,byte> BuildMap(){
        var map=Table.ToDictionary(t=>(int)t.Vk,t=>t.Hid);
        for(int i=0;i<26;i++)map[0x41+i]=(byte)(4+i);                 // A..Z
        for(int i=0;i<9;i++)map[0x31+i]=(byte)(0x1e+i);map[0x30]=0x27; // 1..9, 0
        for(int i=0;i<12;i++)map[0x70+i]=(byte)(0x3a+i);               // F1..F12
        return map;
    }
    public static bool TryHid(int virtualKey,out byte hid)=>vkToHid.TryGetValue(virtualKey,out hid);
    public static string KeyName(byte code){
        if(code is >=0xE0 and <=0xE7)return new[]{"左 Ctrl","左 Shift","左 Alt","左 Win","右 Ctrl","右 Shift","右 Alt","右 Win"}[code-0xE0];
        var named=KeyActions.All().FirstOrDefault(a=>a.Bytes.SequenceEqual(KeyActions.Code(code)));
        return named?.Name??$"HID {code:X2}";
    }
    static byte[] Body(Macro m){
        var name=Encoding.Unicode.GetBytes(m.Name);var b=new List<byte>{(byte)name.Length};b.AddRange(name);
        foreach(var e in m.Events){
            int d=Math.Clamp(e.Delay,0,0xFFFFF);
            b.Add((byte)((e.Code>=0xE0?0x10:0)|(d>>16&0xF)|(e.Up?0x80:0)));b.Add((byte)(d>>8));b.Add((byte)d);b.Add(e.Code);
        }
        return b.ToArray();
    }
    public static void Validate(IReadOnlyList<Macro> macros){
        if(macros.Count>MaxMacros)throw new ArgumentException($"巨集最多 {MaxMacros} 個。");
        foreach(var m in macros){
            if(m.Name.Length is 0 or >MaxName)throw new ArgumentException($"巨集名稱需為 1 到 {MaxName} 個字。");
            if(m.Events.Count is 0 or >MaxEvents)throw new ArgumentException($"「{m.Name}」的事件數需為 1 到 {MaxEvents}。");
            if(m.Events.Any(e=>e.Code==0))throw new ArgumentException($"「{m.Name}」含無效按鍵。");
        }
        if(Build(macros).Length>MaxBytes)throw new ArgumentException("巨集總容量超過鍵盤上限。");
    }
    /// <summary>索引表（每個巨集 4 位元組：偏移、大小，皆 16 位元小端序）＋各巨集本體。</summary>
    public static byte[] Build(IReadOnlyList<Macro> macros){
        var bodies=macros.Select(Body).ToList();var data=new List<byte>();int offset=4*macros.Count;
        foreach(var body in bodies){data.AddRange(BitConverter.GetBytes((ushort)offset));data.AddRange(BitConverter.GetBytes((ushort)body.Length));offset+=body.Length;}
        foreach(var body in bodies)data.AddRange(body);
        return data.ToArray();
    }
    /// <summary>按鍵指向第 index 個巨集的 4 位元組鍵碼。</summary>
    public static byte[] KeyCode(int index)=>new byte[]{3,1,1,(byte)index};
    public static bool IsMacroKey(ReadOnlySpan<byte> code)=>code.Length==4 && code[0]==3 && code[1]==1 && code[2]==1;
}
/// <summary>本機巨集庫；順序即鍵盤上的巨集編號。</summary>
public static class MacroLibrary {
    static string FilePath=>Path.Combine(Store.Root,"macros.json");
    public static List<Macro> Items{get;private set;}=Load();
    static List<Macro> Load(){try{return JsonSerializer.Deserialize<List<Macro>>(File.ReadAllText(FilePath),Store.Json)??new();}catch{return new();}}
    public static void Save(){Store.Write("","macros.json",Items);}
    public static void Replace(List<Macro> items){MacroCodec.Validate(items);Items=items;Save();}
}
