// 裝置狀態、按鍵測試、設定檔與備份
let schemeSel=-1;
const stamp=()=>{const d=new Date();return d.toTimeString().slice(0,8)+"."+String(d.getMilliseconds()).padStart(3,"0")};

PAGES.status=body=>{
  title(body,"裝置狀態","檢視鍵盤連線與電量資訊。");
  const on=!!S.st?.connected,bat=S.st?.battery;
  body.append(
    card(h("div",{class:"row"},h("span",{class:on?"dot on":"dot"}),h("span",{style:{fontSize:"20px"}},on?"USB 已連線":"USB 未連線")),
      muted("使用 USB 連接鍵盤，即可編輯按鍵、燈效與設定檔。")),
    card(h("div",{style:{fontSize:"24px"}},bat==null?"電量尚未取得":`電量 ${bat}%`),
      bat==null?muted("請連接藍牙以讀取電量。"):h("progress",{max:100,value:bat}),
      row(btn("更新電量",()=>run("正在更新電量……",async()=>{await load();go("status")})),
        h("a",{class:"btn",href:"ms-settings:bluetooth"},"開啟 Windows 藍牙設定"))));
};

PAGES.test=body=>{
  title(body,"按鍵測試","檢視按下、放開及重複輸入事件。");
  const pressed=new Set(),released=new Map(),lines=[];
  function add(s){lines.push(s);if(lines.length>150)lines.shift();log.value=lines.join("\n");log.scrollTop=log.scrollHeight}
  const log=h("textarea",{class:"log",readonly:true,placeholder:"點入這裡，再按實體鍵盤。",
    onblur:()=>pressed.clear(),
    onkeydown:e=>{
      if(e.key==="Tab")return;
      e.preventDefault();
      const dup=e.repeat||pressed.has(e.code),last=released.get(e.code),quick=last!=null&&performance.now()-last<30;
      pressed.add(e.code);
      add(`${stamp()}　按下 ${e.code} (${e.key})${dup?" · 系統重複":""}${quick?" · 短間隔，待檢查":""}`);
    },
    onkeyup:e=>{
      if(e.key==="Tab")return;
      pressed.delete(e.code);released.set(e.code,performance.now());
      add(`${stamp()}　放開 ${e.code}`);
    }});
  body.append(card(row(btn("開始測試",()=>log.focus(),{primary:true}),btn("清除紀錄",()=>{lines.length=0;log.value=""})),
    log,muted("測試期間僅記錄此視窗接收的按鍵。按 Tab 離開測試區。")));
};

PAGES.profiles=body=>{
  title(body,"設定檔與備份","匯出、還原與管理鍵盤設定。");
  const schemes=S.st?.schemes||[];
  if(schemeSel>=schemes.length)schemeSel=-1;
  const pick=()=>{if(schemeSel<0){say("請先選擇一個方案。",true);return null}return schemeSel};
  const nameIn=h("input",{type:"text",maxlength:20,placeholder:"新方案名稱"});
  const list=h("div",{class:"list"},schemes.map((n,i)=>h("div",{class:i===schemeSel?"on":"",onclick:()=>{schemeSel=i;go("profiles")}},n)));
  body.append(card(sub(`按鍵方案（${schemes.length}／20）`),
    muted("方案存在本機，包含預設層、FN1、FN2 與 Tap 四層。套用方案會整份寫入鍵盤，寫入前自動備份並讀回核對。"),
    list,nameIn,
    row(
      btn("另存目前鍵盤為方案",()=>{if(!connected())return say("尚未連接鍵盤，無法另存方案。",true);
        run("正在另存方案……",async()=>{await api("/api/schemes/create",{name:nameIn.value.trim()});await load();go("profiles");say("已另存方案。")})}),
      btn("套用到鍵盤",()=>{const i=pick();if(i==null)return;if(changeCount()>0)return say("按鍵重設頁有尚未套用的變更，請先套用或取消。",true);
        run("正在套用方案……",async()=>{await api("/api/schemes/apply",{index:i});S.draft=null;await load();say("已套用方案並讀回核對。")})}),
      btn("刪除方案",()=>{const i=pick();if(i==null)return;if(!confirm("要刪除這個方案嗎？"))return;
        run("正在刪除方案……",async()=>{await api("/api/schemes/delete",{index:i});schemeSel=-1;await load();go("profiles");say("已刪除方案。")})}),
      btn("重新命名",()=>{const i=pick();if(i==null)return;const name=nameIn.value.trim();if(!name)return say("請輸入新方案名稱。",true);
        run("正在重新命名……",async()=>{await api("/api/schemes/rename",{index:i,name});await load();go("profiles");say("已重新命名。")})}),
      btn("匯出方案",()=>{const i=pick();if(i==null)return;
        run("正在匯出方案……",async()=>{const r=await api("/api/schemes/export?index="+i);download("B87-方案-"+schemes[i]+".json",JSON.stringify(r));say("已匯出方案。")})}),
      btn("匯入方案",async()=>{const text=await pickFile();if(!text)return;
        run("正在匯入方案……",async()=>{await api("/api/schemes/import",JSON.parse(text));await load();go("profiles");say("已匯入方案。")})}))));

  body.append(card(sub("匯出與還原"),row(
    btn("匯出完整設定",()=>run("正在匯出設定……",async()=>{const p=await api("/api/profile");download("B87-"+new Date().toISOString().slice(0,10)+".json",JSON.stringify(p,null,2));say("已匯出完整設定。")})),
    btn("從檔案還原",async()=>{if(changeCount()>0)return say("按鍵重設頁有尚未套用的變更，請先套用或取消。",true);const text=await pickFile();if(!text)return;
      run("正在還原設定……",async()=>{const obj=JSON.parse(text);
        if(!confirm("將覆寫四層映射與燈效。會先建立目前設定的完整備份，再讀回核對。要還原嗎？"))return say("已取消還原。");
        await api("/api/restore",obj);S.draft=null;await load();go("profiles");say("完整設定已還原並讀回核對。")})}),
    btn("立即建立完整備份",()=>run("正在建立備份……",async()=>{const r=await api("/api/backup",{});say("已儲存："+r.file);go("profiles")})))));

  const head=sub("備份"),opts=h("div"),list2=h("div",{class:"list"},muted("載入中……"));
  body.append(card(head,muted("備份檔只存在本機。寫入失敗時的自動還原不依賴這些檔案，關閉自動備份也安全。"),opts,list2));
  api("/api/backups").then(b=>{
    const files=b.files||[],manual=f=>f.endsWith("-manual.json");
    head.textContent=`備份（${files.length}）`;
    const auto=h("input",{type:"checkbox",checked:b.auto});
    const num=v=>h("input",{type:"number",min:1,max:b.max,value:v,style:{width:"80px"}});
    const keepAuto=num(b.keepAuto),keepManual=num(b.keepManual);
    const save=btn("儲存備份設定",()=>run("正在儲存……",async()=>{
      const clamp=x=>Math.min(b.max,Math.max(1,Math.round(+x.value||5)));
      await api("/api/backups/settings",{auto:auto.checked,keepAuto:clamp(keepAuto),keepManual:clamp(keepManual)});go("profiles");say("備份設定已儲存，超出份數的舊備份已刪除。")}));
    opts.replaceChildren(h("label",{class:"chk"},auto,"變更設定前自動備份"),
      row(h("label",null,"自動備份保留"),keepAuto,h("span",null,"份"),h("label",null,"手動備份保留"),keepManual,h("span",null,"份"),save));
    const restoreBk=f=>{if(!confirm("要還原這份備份嗎？"))return;
      run("正在還原備份……",async()=>{await api("/api/restore?name="+encodeURIComponent(f),{});S.draft=null;await load();go("profiles");say("已還原並讀回核對。")})};
    list2.replaceChildren(...(files.length?files.map(f=>h("div",{style:{display:"flex",justifyContent:"space-between",alignItems:"center",gap:"8px"}},
      h("span",null,h("span",{class:"chip"},manual(f)?"手動":"自動")," ",f),btn("還原",()=>restoreBk(f)))):[muted("尚無備份。")]));
  }).catch(e=>say(e.message,true));
};
