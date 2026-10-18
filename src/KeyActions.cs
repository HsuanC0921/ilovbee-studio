using System;
using System.Collections.Generic;
using System.Linq;
namespace iLovbeeStudio;
public sealed record KeyAction(string Name,byte[] Bytes) { public override string ToString()=>Name; }
public static class KeyActions {
    public static byte[] Code(byte usage,byte modifier=0)=>new byte[]{0,modifier,0,usage};
    public static List<KeyAction> All() {
        var a=new List<KeyAction> {
            new("顯示桌面／回復視窗（Win＋D）",Code(7,8)),new("F13（供巨集／腳本使用）",Code(0x68)),
            new("區域截圖（Win＋Shift＋S）",Code(0x16,10)),new("停用按鍵",new byte[4]),
            new("小算盤",Convert.FromHexString("02000192")),new("播放／暫停",Convert.FromHexString("020000CD")),
            new("靜音",Convert.FromHexString("020000E2")),new("上一首",Convert.FromHexString("020000B6")),new("下一首",Convert.FromHexString("020000B5")),
            new("複製（Ctrl＋C）",Code(6,1)),new("貼上（Ctrl＋V）",Code(0x19,1)),new("復原（Ctrl＋Z）",Code(0x1d,1)),
            new("儲存（Ctrl＋S）",Code(0x16,1)),new("鎖定電腦（Win＋L）",Code(0xf,8))
        };
        for(int i=0;i<26;i++)a.Add(new(((char)('A'+i)).ToString(),Code((byte)(4+i))));
        for(int i=1;i<=12;i++)a.Add(new("F"+i,Code((byte)(0x39+i))));
        for(int i=13;i<=24;i++)a.Add(new("F"+i,Code((byte)(0x68+i-13))));
        for(int i=1;i<=9;i++)a.Add(new(i.ToString(),Code((byte)(0x1d+i))));
        a.Add(new("0",Code(0x27)));
        a.AddRange(new[]{new KeyAction("Ctrl",Code(0,1)),new KeyAction("Shift",Code(0,2)),new KeyAction("Alt",Code(0,4)),new KeyAction("Win",Code(0,8))});
        foreach(var key in new[]{("-",0x2d),("=",0x2e),("[",0x2f),("]",0x30),("\\",0x31),(";",0x33),("'",0x34),("`",0x35),(",",0x36),(".",0x37),("/",0x38),("Caps Lock",0x39),("Print Screen",0x46),("Scroll Lock",0x47),("Pause",0x48),("Insert",0x49),("選單鍵",0x65)})a.Add(new(key.Item1,Code((byte)key.Item2)));
        foreach(var t in new[]{("Enter",0x28),("Escape",0x29),("Backspace",0x2a),("Tab",0x2b),("空白鍵",0x2c),
            ("Delete",0x4c),("Home",0x4a),("End",0x4d),("Page Up",0x4b),("Page Down",0x4e),("向右",0x4f),("向左",0x50),("向下",0x51),("向上",0x52)})
            a.Add(new(t.Item1,Code((byte)t.Item2)));
        return a;
    }
    public static string Decode(byte[] b) {
        if(b.Length!=4)return "未知";
        var named=All().FirstOrDefault(x=>x.Bytes.SequenceEqual(b));if(named!=null)return named.Name;
        if(b[0]==0 && b[2]==0) {
            string key=b[3] switch{0x35=>"Grave",0x2d=>"-",0x2e=>"=",0x2f=>"[",0x30=>"]",0x31=>"Backslash",0x33=>";",0x34=>"Quote",0x36=>",",0x37=>".",0x38=>"/",0x39=>"Caps Lock",0x46=>"Print Screen",0x47=>"Scroll Lock",0x48=>"Pause",_=>$"HID {b[3]:X2}"};
            if(b[1]!=0){
                string baseName=All().FirstOrDefault(a=>a.Bytes.SequenceEqual(Code(b[3])))?.Name??key;
                var names=new[]{"Ctrl","Shift","Alt","Win","Ctrl","Shift","Alt","Win"};
                string mods=string.Join("＋",Enumerable.Range(0,8).Where(i=>(b[1]&(1<<i))!=0).Select(i=>names[i]).Distinct());
                return b[3]==0?mods:mods+"＋"+baseName;
            }
            return key;
        }
        return b[0]==0x0d?"Fn 功能層":"鍵盤內建功能";
    }
}
