using System.Reflection;
using System.Text;
using System.Text.Json;

namespace iLovbeeStudio;

/// <summary>設定介面的本機 API 與靜態網頁；網頁只在本機回送位址提供，且需要啟動時產生的一次性權杖。</summary>
internal sealed class Api {
    readonly KeyboardService service;
    readonly LiveController live;
    readonly Action applyLive;
    public string Token{get;}=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    public int Port{get;set;}
    public Api(KeyboardService service,LiveController live,Action applyLive){this.service=service;this.live=live;this.applyLive=applyLive;}

    internal static byte[] Resource(string name){
        using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);if(s==null)return Array.Empty<byte>();
        using var ms=new MemoryStream();s.CopyTo(ms);return ms.ToArray();
    }
    static HttpResponse Json(Action<Utf8JsonWriter> write){using var ms=new MemoryStream();using(var w=new Utf8JsonWriter(ms))write(w);return new(200,"application/json; charset=utf-8",ms.ToArray());}
    static HttpResponse Raw(byte[] json)=>new(200,"application/json; charset=utf-8",json);
    static HttpResponse Ok()=>Json(w=>{w.WriteStartObject();w.WriteBoolean("ok",true);w.WriteEndObject();});
    static HttpResponse Error(string message)=>new(400,"application/json; charset=utf-8",JsonSerializer.SerializeToUtf8Bytes(message,AppJson.Default.String));

    public HttpResponse Handle(HttpRequest r){
        if(!r.Headers.TryGetValue("Host",out var host)||host!=$"127.0.0.1:{Port}")return HttpResponse.Text(403,"forbidden");
        if(r.Headers.TryGetValue("Origin",out var origin)&&origin!=$"http://127.0.0.1:{Port}")return HttpResponse.Text(403,"forbidden");
        bool authorised=r.Headers.TryGetValue("Cookie",out var cookie)&&cookie.Split(';').Any(c=>c.Trim()=="ilv="+Token);
        if(r.Method=="GET"&&r.Path=="/"&&!authorised){
            if(!r.Query.Split('&').Contains("t="+Token))return HttpResponse.Text(403,"forbidden");
            return new(200,"text/html; charset=utf-8",Resource("web.index.html"),"ilv="+Token+"; Path=/; HttpOnly; SameSite=Strict");
        }
        if(!authorised)return HttpResponse.Text(403,"forbidden");
        if(r.Method=="GET"&&StaticFiles.TryGetValue(r.Path,out var file)){var body=Resource("web."+r.Path[1..]);return new(200,file,body);}
        try{
            switch(r.Method+" "+r.Path){
                case "GET /":return new(200,"text/html; charset=utf-8",Resource("web.index.html"));
                case "GET /keyboard.json":return Raw(Resource("keyboard.json"));
                case "GET /api/state":return State();
                case "GET /api/ping":return Json(w=>{w.WriteStartObject();w.WriteBoolean("connected",Connected());w.WriteEndObject();});
                case "POST /api/lights":return SetLights(Doc(r));
                case "POST /api/color":{using var d=Doc(r);service.SetCustomColor(d.RootElement.GetProperty("mode").GetInt32(),Convert.FromHexString(Str(d,"rgb")));return Ok();}
                case "POST /api/keymap":return ApplyKeymap(Doc(r));
                case "POST /api/macros/save":{var items=JsonSerializer.Deserialize(r.Body,AppJson.Default.ListMacro)??throw new ArgumentException("無效的巨集。");MacroLibrary.Replace(items);return Ok();}
                case "POST /api/macros/write":{var items=MacroLibrary.Items.ToList();if(items.Count==0)throw new InvalidOperationException("尚無巨集。");MacroCodec.Validate(items);service.WriteMacros(MacroCodec.Build(items));return Ok();}
                case "POST /api/schemes/create":return CreateScheme(Doc(r));
                case "POST /api/schemes/apply":return ApplyScheme(Doc(r));
                case "POST /api/schemes/delete":{int i=Index(Doc(r),SchemeLibrary.Items.Count);SchemeLibrary.Items.RemoveAt(i);SchemeLibrary.Save();return Ok();}
                case "POST /api/schemes/rename":{using var d=Doc(r);int i=Index(d,SchemeLibrary.Items.Count);SchemeLibrary.Rename(i,Str(d,"name"));return Ok();}
                case "POST /api/schemes/import":{var s=JsonSerializer.Deserialize(r.Body,AppJson.Default.Scheme)??throw new ArgumentException("無效的方案檔。");var made=SchemeLibrary.Create(s.Name??"",SchemeLibrary.Decode(s));SchemeLibrary.Items.Add(made);SchemeLibrary.Save();return Ok();}
                case "GET /api/schemes/export":{int i=int.Parse(r.Query.Replace("index=",""));if(i<0||i>=SchemeLibrary.Items.Count)throw new ArgumentException("沒有這個方案。");return Raw(JsonSerializer.SerializeToUtf8Bytes(SchemeLibrary.Items[i],AppJson.Default.Scheme));}
                case "POST /api/layouts/put":{using var d=Doc(r);LightLayoutLibrary.Put(Str(d,"name"),Convert.FromHexString(Str(d,"frame")),d.RootElement.TryGetProperty("fx",out var fx)?Convert.FromHexString(fx.GetString()??""):null);return Ok();}
                case "POST /api/layouts/delete":{using var d=Doc(r);LightLayoutLibrary.Items.RemoveAll(l=>l.Name==Str(d,"name"));LightLayoutLibrary.Save();return Ok();}
                case "GET /api/live":{var c=LiveConfig.Load();return Json(w=>{w.WriteStartObject();w.WriteString("mode",c.Mode);w.WriteNumber("style",c.Style);w.WriteNumber("gain",c.Gain);w.WriteNumber("colormode",c.ColorMode);w.WriteNumber("colordir",c.ColorDir);w.WriteStartArray("colors");foreach(var col in c.Colors)w.WriteStringValue(col);w.WriteEndArray();w.WriteString("status",live.Status);w.WriteEndObject();});}
                case "POST /api/live":return SetLive(Doc(r));
                case "POST /api/decode":return Decode(Doc(r));
                case "POST /api/backup":{string path=service.Backup("manual")!;return Json(w=>{w.WriteStartObject();w.WriteString("file",Path.GetFileName(path));w.WriteEndObject();});}
                case "GET /api/backups":return Backups();
                case "POST /api/backups/settings":{using var d=Doc(r);var e=d.RootElement;var s=new BackupSettings{Auto=e.GetProperty("auto").GetBoolean(),
                    KeepAuto=Math.Clamp(e.GetProperty("keepAuto").GetInt32(),1,BackupSettings.MaxKeep),KeepManual=Math.Clamp(e.GetProperty("keepManual").GetInt32(),1,BackupSettings.MaxKeep)};
                    s.Save();KeyboardService.Prune(s);return Ok();}
                case "GET /api/profile":return Raw(JsonSerializer.SerializeToUtf8Bytes(service.Capture(),AppJson.Default.DeviceProfile));
                case "POST /api/restore":return Restore(r);
                default:return HttpResponse.Text(404,"not found");
            }
        }catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException or FormatException or KeyNotFoundException or System.ComponentModel.Win32Exception or InvalidDataException or NotSupportedException){
            return Error(e.Message);
        }
    }
    static readonly Dictionary<string,string> StaticFiles=new(){["/effects.js"]="text/javascript; charset=utf-8",["/core.js"]="text/javascript; charset=utf-8",["/remap.js"]="text/javascript; charset=utf-8",["/macros.js"]="text/javascript; charset=utf-8",["/lighting.js"]="text/javascript; charset=utf-8",["/custom.js"]="text/javascript; charset=utf-8",["/misc.js"]="text/javascript; charset=utf-8",["/main.js"]="text/javascript; charset=utf-8",["/style.css"]="text/css; charset=utf-8",["/app.ico"]="image/x-icon"};
    static JsonDocument Doc(HttpRequest r)=>JsonDocument.Parse(r.Body);
    static string Str(JsonDocument d,string name)=>d.RootElement.GetProperty(name).GetString()??"";
    static int Index(JsonDocument d,int count){int i=d.RootElement.GetProperty("index").GetInt32();if(i<0||i>=count)throw new ArgumentException("沒有這個項目。");return i;}
    static Dictionary<int,byte[]> Maps(JsonElement e)=>e.EnumerateObject().ToDictionary(p=>int.Parse(p.Name),p=>Convert.FromHexString(p.Value.GetString()??""));

    bool Connected(){try{service.Scan();}catch(Exception e)when(e is System.ComponentModel.Win32Exception or IOException){}return service.Current!=null;}

    HttpResponse State(){
        DeviceProfile? profile=null;string? error=null;
        if(Connected()){try{profile=service.Capture();}catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException){error=e.Message;}}
        var battery=WindowsBattery.Read();var cfg=LiveConfig.Load();
        var actions=KeyActions.All().Concat(MacroLibrary.Items.Select((m,i)=>new KeyAction("巨集："+m.Name,MacroCodec.KeyCode(i)))).ToList();
        return Json(w=>{
            w.WriteStartObject();
            w.WriteBoolean("connected",service.Current!=null);if(error!=null)w.WriteString("error",error);
            if(profile!=null){
                w.WriteString("lights",profile.Lighting);w.WriteString("colortable",Convert.ToHexString(ColorTable.Load()));
                w.WriteStartObject("maps");foreach(var m in profile.Keymaps)w.WriteString(m.Key.ToString(),m.Value);w.WriteEndObject();
                w.WriteStartObject("names");foreach(var m in profile.Keymaps){
                    w.WriteStartArray(m.Key.ToString());var bytes=Convert.FromHexString(m.Value);
                    for(int s=0;s<104;s++)w.WriteStringValue(KeyActions.Decode(bytes.AsSpan(s*4,4).ToArray()));w.WriteEndArray();
                }w.WriteEndObject();
            }
            if(battery?.Percent is int percent){w.WriteNumber("battery",percent);}
            w.WriteStartArray("actions");foreach(var a in actions){w.WriteStartObject();w.WriteString("n",a.Name);w.WriteString("h",Convert.ToHexString(a.Bytes));w.WriteEndObject();}w.WriteEndArray();
            w.WritePropertyName("macros");w.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(MacroLibrary.Items,AppJson.Default.ListMacro),true);
            w.WriteStartArray("schemes");foreach(var s in SchemeLibrary.Items)w.WriteStringValue(s.Name);w.WriteEndArray();
            w.WritePropertyName("layouts");w.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(LightLayoutLibrary.Items,AppJson.Default.ListLightLayout),true);
            w.WriteStartObject("live");w.WriteString("mode",cfg.Mode);w.WriteNumber("style",cfg.Style);w.WriteNumber("gain",cfg.Gain);w.WriteNumber("colormode",cfg.ColorMode);w.WriteNumber("colordir",cfg.ColorDir);w.WriteStartArray("colors");foreach(var col in cfg.Colors)w.WriteStringValue(col);w.WriteEndArray();w.WriteString("frame",Convert.ToHexString(cfg.Frame));w.WriteString("fx",Convert.ToHexString(cfg.Fx));w.WriteNumber("fxspeed",cfg.FxSpeed);w.WriteStartArray("effects");foreach(var n in LedFrame.KeyEffects)w.WriteStringValue(n);w.WriteEndArray();w.WriteString("status",live.Status);
            w.WriteStartArray("styles");foreach(var s in LedFrame.MusicStyles)w.WriteStringValue(s);w.WriteEndArray();w.WriteEndObject();
            w.WriteEndObject();
        });
    }
    HttpResponse SetLights(JsonDocument d){
        using(d){service.SetLights(Convert.FromHexString(Str(d,"expected")),Convert.FromHexString(Str(d,"after")));}
        return Ok();
    }
    HttpResponse ApplyKeymap(JsonDocument d){
        using(d){
            var before=Maps(d.RootElement.GetProperty("expected"));var after=Maps(d.RootElement.GetProperty("desired"));
            bool macroChanged=after.Any(p=>Enumerable.Range(0,104).Any(s=>MacroCodec.IsMacroKey(p.Value.AsSpan(s*4,4)) && !before[p.Key].AsSpan(s*4,4).SequenceEqual(p.Value.AsSpan(s*4,4))));
            if(macroChanged){
                var items=MacroLibrary.Items.ToList();MacroCodec.Validate(items);
                if(after.Any(p=>Enumerable.Range(0,104).Any(s=>MacroCodec.IsMacroKey(p.Value.AsSpan(s*4,4)) && p.Value[s*4+3]>=items.Count)))throw new InvalidOperationException("按鍵指向不存在的巨集。");
                service.WriteMacros(MacroCodec.Build(items));
            }
            service.ApplyMappings(before,after);
        }
        return Ok();
    }
    HttpResponse CreateScheme(JsonDocument d){
        using(d){
            var maps=service.Capture().Keymaps.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));
            var items=SchemeLibrary.Items;string name=Str(d,"name");if(string.IsNullOrWhiteSpace(name))name="方案"+(items.Count+1);
            items.Add(SchemeLibrary.Create(name,maps));SchemeLibrary.Save();
        }
        return Ok();
    }
    HttpResponse ApplyScheme(JsonDocument d){
        using(d){
            var scheme=SchemeLibrary.Items[Index(d,SchemeLibrary.Items.Count)];var desired=SchemeLibrary.Decode(scheme);
            var before=service.Capture().Keymaps.ToDictionary(p=>p.Key,p=>Convert.FromHexString(p.Value));
            service.ApplyMappings(before,desired);
        }
        return Ok();
    }
    HttpResponse SetLive(JsonDocument d){
        using(d){
            var e=d.RootElement;var c=LiveConfig.Load();
            string mode=Str(d,"mode");if(mode is not ("off" or "music" or "custom" or "keep"))throw new ArgumentException("無效的模式。");if(mode!="keep")c.Mode=mode;   // keep＝只改樣式／靈敏度／畫面，不改模式（避免畫面上過期的狀態蓋掉托盤剛切的模式）
            if(e.TryGetProperty("style",out var s))c.Style=Math.Clamp(s.GetInt32(),0,LedFrame.MusicStyles.Length-1);
            if(e.TryGetProperty("gain",out var g))c.Gain=Math.Clamp(g.GetDouble(),5,200);
            if(e.TryGetProperty("colormode",out var cm))c.ColorMode=Math.Clamp(cm.GetInt32(),0,1);
            if(e.TryGetProperty("colordir",out var cd))c.ColorDir=Math.Clamp(cd.GetInt32(),0,3);
            if(e.TryGetProperty("colors",out var cl)){var l=cl.EnumerateArray().Select(x=>x.GetString()??"").Where(x=>x.Length==6).Select(x=>Convert.ToHexString(Convert.FromHexString(x))).Take(MusicColor.MaxStops).ToArray();if(l.Length>0)c.Colors=l;}
            if(e.TryGetProperty("frame",out var f)&&f.GetString() is {Length:LedFrame.Bytes*2} hex)c.Frame=Convert.FromHexString(hex);
            if(e.TryGetProperty("fx",out var x)&&x.GetString() is {Length:LedFrame.Keys*2} fxHex)c.Fx=Convert.FromHexString(fxHex).Select(b=>(byte)Math.Min((int)b,LedFrame.KeyEffects.Length-1)).ToArray();
            if(e.TryGetProperty("fxspeed",out var fs))c.FxSpeed=Math.Clamp(fs.GetInt32(),1,4);
            c.Save();
        }
        applyLive();
        return Ok();
    }
    HttpResponse Decode(JsonDocument d){
        using(d){
            var codes=d.RootElement.GetProperty("codes").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()??"")).ToList();
            return Json(w=>{w.WriteStartArray();foreach(var c in codes)w.WriteStringValue(KeyActions.Decode(c));w.WriteEndArray();});
        }
    }
    static string BackupFolder=>Path.Combine(Store.Root,"backups");
    HttpResponse Backups(){
        var files=Directory.Exists(BackupFolder)?Directory.GetFiles(BackupFolder,"*.json").OrderDescending().Select(Path.GetFileName).ToArray():Array.Empty<string?>();
        var s=BackupSettings.Load();
        return Json(w=>{w.WriteStartObject();w.WriteBoolean("auto",s.Auto);w.WriteNumber("keepAuto",s.KeepAuto);w.WriteNumber("keepManual",s.KeepManual);w.WriteNumber("max",BackupSettings.MaxKeep);
            w.WriteStartArray("files");foreach(var f in files)w.WriteStringValue(f);w.WriteEndArray();w.WriteEndObject();});
    }
    HttpResponse Restore(HttpRequest r){
        DeviceProfile profile;
        if(r.Query.StartsWith("name=")){
            string name=Uri.UnescapeDataString(r.Query[5..]);
            if(name!=Path.GetFileName(name)||!name.EndsWith(".json"))throw new ArgumentException("無效的備份檔名。");
            profile=Store.Load(Path.Combine(BackupFolder,name));
        }else{
            profile=JsonSerializer.Deserialize(r.Body,AppJson.Default.DeviceProfile)??throw new InvalidDataException("無效的設定檔。");Store.Validate(profile);
        }
        service.Restore(profile);return Ok();
    }
}
