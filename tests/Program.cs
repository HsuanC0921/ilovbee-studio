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
{var kf=new byte[LedFrame.Bytes];var kx=new byte[LedFrame.Keys];for(int s=0;s<4;s++){LedFrame.Set(kf,s,200,0,0);kx[s]=(byte)s;}
 var ka=LedFrame.Animate(kf,kx,0.4/0.7,2);   // 速度 2＝0.7 Hz；t 落在第 0.4 圈：閃爍亮、呼吸接近最亮
 Check(ka[0]==200&&ka[3]==200&&ka[6]>150&&ka[6]<200&&kf[6]==200,"key fx static/blink on/breathe; source untouched");
 var kb=LedFrame.Animate(kf,kx,0.6/0.7,2);Check(kb[3]==0&&kb[4]==0,"key blink off in second half");
 Check(Math.Max(ka[9],Math.Max(ka[10],ka[11]))==200,"key rainbow keeps brightness");}
{var bs=new BackupSettings();Check(bs.Auto&&bs.KeepAuto==5&&bs.KeepManual==5,"backup defaults: on, 5 auto, 5 manual");
 Check(BackupSettings.IsManual("x-manual.json")&&!BackupSettings.IsManual("x-before-lighting.json"),"manual backup detection");}
Check(Convert.ToHexString(NativeHid.Packet(0x0a,0,ColorTable.Bytes).AsSpan(0,8))=="060A00000100A401","color table header matches vendor");
{var t=ColorTable.Default();Check(Convert.ToHexString(ColorTable.Custom(t,0))=="000000"&&Convert.ToHexString(ColorTable.Custom(t,2))=="FF0000"&&t[2*21+20]==0xFF,"default color table");
 ColorTable.SetCustom(t,2,new byte[]{0,0,0xCC});Check(t[42]==0&&t[44]==0xCC&&t[45]==0,"custom color only touches slot 0");Reject(()=>ColorTable.SetCustom(t,0,new byte[3]),"mode 0 has no color");}
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
Check(LedFrame.Hsv(0,1,1)==(255,0,0)&&LedFrame.Hsv(120,1,1)==(0,255,0)&&LedFrame.Hsv(240,1,1)==(0,0,255),"hsv primaries");
var fr=new byte[LedFrame.Bytes];LedFrame.Set(fr,125,1,2,3);Check(fr[375]==1&&fr[377]==3,"frame slot layout");
var musicKeys=new[]{new PhysicalKey(1,"A",100,254,28,30,10),new PhysicalKey(2,"B",100,38,28,30,20)};
var loud=Enumerable.Repeat(1f,LedFrame.Bands).ToArray();var quiet=new float[LedFrame.Bands];
for(int st=0;st<LedFrame.MusicStyles.Length;st++){var viz=new MusicVisualizer(musicKeys);bool lit=false;for(int i=1;i<=12;i++)lit|=viz.Render(st,loud,i*.1).Any(b=>b>0);
    Check(lit,"music style lights keys "+st);}
Check(new MusicVisualizer(musicKeys).Render(0,quiet,0).All(b=>b==0),"spectrum silence is dark");
{ // 隨機頻譜、全部樣式、全部按鍵：不得丟例外，框格長度固定
    var rng=new Random(7);var allKeys=Enumerable.Range(0,87).Select(i=>new PhysicalKey(i,"k"+i,20+i%15*50,38+i/15*43,28,30,i)).ToList();
    for(int st=0;st<LedFrame.MusicStyles.Length;st++){var viz=new MusicVisualizer(allKeys);bool ok=true;
        for(int i=0;i<300;i++){var b=Enumerable.Range(0,LedFrame.Bands).Select(_=>(float)(rng.NextDouble()*(i%50<10?0:1))).ToArray();ok&=viz.Render(st,b,i/30.0).Length==LedFrame.Bytes;}
        Check(ok,"random spectrum style "+st);}
}
{ // 音樂配色：單色只會產生該色的比例、兩色漸層落在兩色之間、混色等同 HSV
    byte[] R={255,0,0},G={0,255,0},B={0,0,255};
    var red=new MusicColor(1,new[]{R},0);var p1=red.Pick(123,1,.5,0);Check(p1.R==128&&p1.G==0&&p1.B==0,"solid color scales");
    var white=red.Pick(0,0,1,0);Check(white.R==255&&white.G==255&&white.B==255,"solid keeps white accent");
    var tri=new MusicColor(1,new[]{R,G,B},0);var a0=tri.Pick(0,1,1,0);var a1=tri.Pick(0,1,1,.5);var a2=tri.Pick(0,1,1,1);var am=tri.Pick(0,1,1,.25);
    Check(MusicColor.Mix.Pick(0,1,1,0)==LedFrame.Hsv(0,1,1),"mix equals hsv");
    Check(a0.R==255&&a1.G==255&&a2.B==255&&am.R==128&&am.G==128,"multi-stop gradient");
}
Console.WriteLine($"{passed} passed, 0 failed");

