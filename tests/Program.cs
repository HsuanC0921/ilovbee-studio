using iLovbeeStudio;
using System.Text.Json;

if(args.Length==1 && args[0]=="--battery") { Console.WriteLine(JsonSerializer.Serialize(WindowsBattery.Read(),Store.Json));return; }
if(args.Length==1 && args[0]=="--hardware-smoke") {
    var service=new KeyboardService();service.Scan();var initial=service.Capture();
    var before=initial.Keymaps.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));var after=MappingEditor.Clone(before);
    foreach(int layer in new[]{0,1})KeyActions.Code(0x68).CopyTo(after[layer],78*4);
    try {
        service.ApplyMappings(before,after);var readback=service.Capture();
        if(readback.Keymaps.Any(p=>!Convert.FromHexString(p.Value).SequenceEqual(after[p.Key])) || readback.Lighting!=initial.Lighting)throw new Exception("Batch readback differs.");
        Console.WriteLine("PASS actual two-layer batch readback; lighting unchanged");
    } finally {
        var current=service.Capture().Keymaps.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));service.ApplyMappings(current,before);
        var restored=service.Capture();if(restored.Keymaps.Any(p=>p.Value!=initial.Keymaps[p.Key]) || restored.Lighting!=initial.Lighting)throw new Exception("Restoration differs; preserve automatic backup.");
        Console.WriteLine("PASS original three-layer mapping and lighting restored");
    }
    return;
}

if(args.Length==2 && args[0]=="--probe") {
    var service=new KeyboardService();var devices=service.Scan();var profile=service.Capture();var power=service.ReadPower();
    File.WriteAllText(args[1],JsonSerializer.Serialize(new{Devices=devices,Profile=profile,Power=power,CalculatorMapping=profile.Keymaps[0].Substring(78*8,8)},Store.Json));return;
}
int passed=0;
void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Unexpected acceptance: "+name);}
Check(Convert.ToHexString(NativeHid.Packet(0x83,0,416).AsSpan(0,8))=="068300000100A001","HID query header");
Reject(()=>NativeHid.Packet(0x83,0,513),"oversized packet");
Reject(()=>NativeHid.Packet(0x83,0,0),"empty packet");
var profileTest=new DeviceProfile(1,"258A:0199","2010",DateTimeOffset.Now,Enumerable.Range(0,3).ToDictionary(i=>i,_=>new string('0',832)),new string('0',256));
Store.Validate(profileTest);Check(true,"complete profile");
Reject(()=>Store.Validate(profileTest with{Device="342D:E469"}),"wrong hardware");
Reject(()=>Store.Validate(profileTest with{Keymaps=new Dictionary<int,string>{{0,new string('0',832)}}}),"incomplete layers");
Reject(()=>Store.Validate(profileTest with{Lighting="00"}),"truncated lighting");
Check(Convert.ToHexString(KeyActions.Code(0x68))=="00000068","F13 action");
Check(Convert.ToHexString(KeyActions.Code(7,8))=="00080007","Win+D action");
Check(KeyActions.Decode(Convert.FromHexString("02000192"))=="小算盤","calculator decoding");
Reject(()=>new KeyboardService().Remap(4,78,new byte[416],new byte[4]),"invalid layer before device access");
var editor=new MappingEditor();var baseline=Enumerable.Range(0,4).ToDictionary(i=>i,_=>new byte[416]);editor.Load(baseline);
editor.Assign(0,78,KeyActions.Code(7,8));editor.Assign(1,1,KeyActions.Code(0x68));Check(editor.ChangeCount==2,"multi-layer draft count");
Check(baseline[0].All(b=>b==0),"draft preserves source buffer");editor.Undo();Check(editor.ChangeCount==1,"undo across layers");editor.Redo();Check(editor.ChangeCount==2,"redo across layers");
editor.Revert(0,78);Check(editor.ChangeCount==1,"revert selected key");editor.Discard();Check(editor.ChangeCount==0 && !editor.CanUndo,"discard draft");
Reject(()=>editor.Assign(0,59,KeyActions.Code(7)),"FN protected");
Reject(()=>editor.Assign(4,1,KeyActions.Code(7)),"draft layer bound");
editor.Assign(0,78,KeyActions.Code(7,8));var saved=MappingEditor.Clone(editor.Draft);editor.Load(baseline);editor.Resume(saved);Check(editor.ChangeCount==1,"resume saved draft");
Check(KeyActions.Decode(KeyActions.Code(4,5))=="Ctrl＋Alt＋A","custom chord label");
// 原廠實機封包（Frida 記錄）：T1＝A,B；T2＝LShift+C。
Macro Mac(string n,params (byte c,bool up,int d)[] e)=>new(n,e.Select(x=>new MacroEvent(x.c,x.up,x.d)).ToList());
var t1=Mac("T1",(4,false,10),(4,true,100),(5,false,10),(5,true,0));var t2=Mac("T2",(0xE1,false,10),(6,false,10),(6,true,30),(0xE1,true,0));
var t1Body="0454003100"+"00000A04"+"80006404"+"00000A05"+"80000005";
Check(Convert.ToHexString(MacroCodec.Build(new[]{t1}))=="04001500"+t1Body,"vendor single-macro layout");
Check(Convert.ToHexString(MacroCodec.Build(new[]{t2,t1}))=="080015001D001500"+"0454003200"+"10000AE1"+"00000A06"+"80001E06"+"900000E1"+t1Body,"vendor two-macro layout");
Check(Convert.ToHexString(MacroCodec.KeyCode(1))=="03010101","macro key code");
Reject(()=>MacroCodec.Validate(new[]{Mac("",(4,false,0))}),"empty macro name");
Reject(()=>MacroCodec.Validate(Enumerable.Range(0,9).Select(i=>Mac("m"+i,(4,false,0))).ToList()),"macro count limit");
Reject(()=>MacroCodec.Validate(Enumerable.Range(0,8).Select(i=>Mac("m"+i,Enumerable.Range(0,60).Select(_=>(byte)4).Select(c=>(c,false,0)).ToArray())).ToList()),"macro capacity limit");
Console.WriteLine($"{passed} passed, 0 failed");

