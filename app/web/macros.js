// 巨集：清單、事件編輯、錄製、寫入鍵盤
let macroSel=0,evSel=null,macroRec=null;
const HID_NAMES={};
for(let i=0;i<26;i++)HID_NAMES[4+i]=String.fromCharCode(65+i);
for(let i=0;i<9;i++)HID_NAMES[30+i]=String(i+1);
for(let i=0;i<12;i++)HID_NAMES[58+i]="F"+(i+1);
Object.assign(HID_NAMES,{39:"0",40:"Enter",41:"Esc",42:"Backspace",43:"Tab",44:"空白鍵",45:"-",46:"=",47:"[",48:"]",49:"\\",51:";",52:"'",53:"`",54:",",55:".",56:"/",57:"Caps Lock",70:"PrtSc",71:"Scroll Lock",72:"Pause",73:"Insert",74:"Home",75:"PageUp",76:"Delete",77:"End",78:"PageDown",79:"向右",80:"向左",81:"向下",82:"向上",224:"左Ctrl",225:"左Shift",226:"左Alt",227:"左Win",228:"右Ctrl",229:"右Shift",230:"右Alt",231:"右Win"});
const CODE_TO_HID={Enter:0x28,Escape:0x29,Backspace:0x2A,Tab:0x2B,Space:0x2C,Minus:0x2D,Equal:0x2E,BracketLeft:0x2F,BracketRight:0x30,Backslash:0x31,Semicolon:0x33,Quote:0x34,Backquote:0x35,Comma:0x36,Period:0x37,Slash:0x38,CapsLock:0x39,PrintScreen:0x46,ScrollLock:0x47,Pause:0x48,Insert:0x49,Home:0x4A,PageUp:0x4B,Delete:0x4C,End:0x4D,PageDown:0x4E,ArrowRight:0x4F,ArrowLeft:0x50,ArrowDown:0x51,ArrowUp:0x52,ControlLeft:0xE0,ShiftLeft:0xE1,AltLeft:0xE2,MetaLeft:0xE3,ControlRight:0xE4,ShiftRight:0xE5,AltRight:0xE6,MetaRight:0xE7};
for(let i=0;i<26;i++)CODE_TO_HID["Key"+String.fromCharCode(65+i)]=4+i;
for(let i=1;i<=9;i++)CODE_TO_HID["Digit"+i]=29+i;
CODE_TO_HID.Digit0=0x27;
for(let i=1;i<=12;i++)CODE_TO_HID["F"+i]=57+i;

const keyLabel=c=>HID_NAMES[c]||"HID "+hex([c]);
const clampDelay=v=>Math.max(0,Math.min(60000,Math.round(+v)||0));
const DEFAULT_EVENTS=()=>[{Code:0x2C,Up:false,Delay:10},{Code:0x2C,Up:true,Delay:0}];
const macroCopy=()=>(S.st.macros||[]).map(m=>({Name:m.Name,Events:m.Events.map(e=>({...e}))}));
const macroBytes=list=>4*list.length+list.reduce((a,m)=>a+1+2*m.Name.length+4*m.Events.length,0);
function macroCheck(list){
  if(list.length>8)throw new Error("最多 8 個巨集。");
  for(const m of list){
    if(m.Name.length<1||m.Name.length>20)throw new Error("巨集名稱需為 1 到 20 字。");
    if(m.Events.length<1||m.Events.length>60)throw new Error("每個巨集的事件數需為 1 到 60。");
  }
  if(macroBytes(list)>512)throw new Error("超過鍵盤容量 512 位元組。");
}
// 按鍵指向巨集的格式為 03 01 01 <索引>
const macroInUse=()=>[...Object.values(S.st.maps||{}),...Object.values(S.draft||{})].some(l=>{for(let s=0;s<104;s++)if(slotHex(l,s).startsWith("030101"))return true;return false});

async function macroSave(list,msg,sel){
  macroCheck(list);
  await api("/api/macros/save",list);
  S.st.macros=list;macroRec=null;
  if(sel!==undefined)macroSel=sel;
  macroSel=Math.max(0,Math.min(macroSel,list.length-1));
  if(evSel!=null&&evSel>=(list[macroSel]?.Events.length??0))evSel=null;
  go("macros");say(msg);
}
const macroEdit=(fn,msg,sel)=>run("正在儲存巨集……",async()=>{const list=macroCopy();fn(list);await macroSave(list,msg,sel)});

function macroRecAdd(code,up,cnt){
  const r=macroRec;if(!r||r.ev.length>=60)return;
  const now=performance.now();
  if(r.ev.length)r.ev[r.ev.length-1].Delay=clampDelay(now-r.t);
  r.ev.push({Code:code,Up:up,Delay:0});r.t=now;
  cnt.textContent=`已錄 ${r.ev.length} 個事件（上限 60）`;
  if(r.ev.length===60)macroRecStop();
}
function macroRecStop(){
  if(!macroRec)return;
  const ev=macroRec.ev;
  macroEdit(l=>{l[macroSel].Events=ev},`已錄製 ${ev.length} 個事件。`);
}
function macroAdd(){
  const n=(S.st.macros||[]).length+1;
  macroEdit(l=>{l.push({Name:"巨集"+n,Events:DEFAULT_EVENTS()})},`已新增「巨集${n}」。`,n-1);
}
async function importMacros(){
  const txt=await pickFile(".json");if(txt==null)return;
  let add;try{add=JSON.parse(txt)}catch{return say("檔案不是有效的 JSON。",true)}
  if(!Array.isArray(add))return say("巨集庫格式錯誤，需要陣列。",true);
  const at=(S.st.macros||[]).length;
  macroEdit(l=>{l.push(...add.map(x=>({Name:String(x.Name??""),Events:(x.Events||[]).map(e=>({Code:+e.Code,Up:!!e.Up,Delay:clampDelay(e.Delay)}))})))},`已匯入 ${add.length} 個巨集。`,at);
}

PAGES.macros=body=>{
  title(body,"巨集","錄製或手動編輯按鍵序列，寫入鍵盤後可指派給按鍵。");
  if(!S.st){body.append(card(sub("尚未讀取鍵盤"),muted("請按「重新讀取」。")));return}
  const list=S.st.macros||[];
  if(macroSel>=list.length)macroSel=Math.max(0,list.length-1);
  const i=macroSel,m=list[i];
  body.append(card(sub(`巨集清單（${list.length}／8）`),
    h("div",{class:"list"},list.map((x,k)=>h("div",{class:k===i?"on":"",onclick:()=>{macroSel=k;evSel=null;macroRec=null;go("macros")}},`${x.Name}（${x.Events.length} 個事件）`))),
    row(btn("新增",macroAdd,{disabled:list.length>=8}),
      btn("複製",()=>{const n=(m.Name+"副本").slice(0,20);macroEdit(l=>{l.push({Name:n,Events:l[i].Events.map(e=>({...e}))})},"已複製巨集。",list.length)},{disabled:!m}),
      btn("刪除",()=>{if(macroInUse())return say("此巨集已指派給按鍵，請先到「按鍵重設」改回其他功能。",true);macroEdit(l=>{l.splice(i,1)},"已刪除巨集。",Math.max(0,i-1))},{disabled:!m}),
      btn("匯出巨集庫",()=>download("B87-巨集庫.json",JSON.stringify(list,null,2)),{disabled:!list.length}),
      btn("匯入巨集（加到清單後面）",importMacros))));

  if(!m){body.append(card(muted("尚無巨集，請按「新增」建立。")));}
  else{
    const nameIn=h("input",{type:"text",value:m.Name,maxlength:20,style:{width:"100%"},onchange:e=>{const v=e.target.value.trim();macroEdit(l=>{l[i].Name=v},"已更新名稱。")}});
    const tbl=h("table",{class:"tbl"},h("tr",null,["#","按鍵","動作","延遲"].map(t=>h("th",null,t))),
      m.Events.map((e,j)=>h("tr",{class:j===evSel?"on":"",style:{cursor:"pointer"},onclick:()=>{evSel=j;go("macros")}},
        h("td",null,j+1),h("td",null,keyLabel(e.Code)),h("td",null,e.Up?"放開":"按下"),h("td",null,e.Delay+" ms"))));
    const dIn=h("input",{type:"number",min:0,max:60000,value:evSel!=null?m.Events[evSel].Delay:10,style:{width:"100px"}});
    const keySel=h("select",null,Object.keys(HID_NAMES).map(Number).sort((a,b)=>a-b).map(c=>h("option",{value:c},HID_NAMES[c])));
    keySel.value="44";
    const modeSel=h("select",null,[["click","點擊（按下後放開）"],["down","按下"],["up","放開"]].map(([v,t])=>h("option",{value:v},t)));
    const addIn=h("input",{type:"number",min:0,max:60000,value:10,style:{width:"100px"}});

    let recBox=null;
    const recCtl=macroRec
      ?row(btn("停止並儲存",macroRecStop,{primary:true}),btn("取消錄製",()=>{macroRec=null;go("macros")}))
      :row(btn("開始錄製",()=>{macroRec={ev:[],t:0};evSel=null;go("macros")}));
    if(macroRec){
      const cnt=h("div",{class:"muted"},`已錄 ${macroRec.ev.length} 個事件（上限 60）`);
      recBox=h("div",{tabindex:0,style:{border:"2px dashed currentColor",borderRadius:"8px",padding:"20px",textAlign:"center",outline:"none"},
        onkeydown:e=>{e.preventDefault();if(e.repeat)return;const c=CODE_TO_HID[e.code];if(c!=null)macroRecAdd(c,false,cnt)},
        onkeyup:e=>{e.preventDefault();const c=CODE_TO_HID[e.code];if(c!=null)macroRecAdd(c,true,cnt)}},
        h("div",null,"錄製中：按下鍵盤按鍵…"),cnt);
    }

    body.append(card(sub("編輯："+m.Name),
      row(h("span",null,"名稱"),nameIn),
      tbl,
      row(btn("刪除所選事件",()=>{const j=evSel;if(j!=null)macroEdit(l=>{l[i].Events.splice(j,1)},"已刪除事件。")},{disabled:evSel==null}),
        btn("清空",()=>macroEdit(l=>{l[i].Events=DEFAULT_EVENTS()},"已清空，保留一個預設事件（鍵盤至少需要 1 個事件）。")),
        h("span",{style:{flex:1}}),dIn,
        btn("套用延遲",()=>{const j=evSel;if(j==null)return say("請先點選要調整的事件。",true);const d=clampDelay(dIn.value);macroEdit(l=>{l[i].Events[j].Delay=d},`已設定延遲 ${d} ms。`)},{disabled:evSel==null})),
      sub("手動加入事件"),
      row(keySel,modeSel,addIn,btn("加入",()=>{const c=+keySel.value,mode=modeSel.value,d=clampDelay(addIn.value);macroEdit(l=>{const ev=l[i].Events;if(mode!=="up")ev.push({Code:c,Up:false,Delay:d});if(mode!=="down")ev.push({Code:c,Up:true,Delay:d})},"已加入事件。")})),
      sub("錄製"),
      recCtl,recBox||muted("錄製會取代此巨集目前的事件，最多 60 個。")));
    if(recBox)recBox.focus();
  }

  const bytes=macroBytes(list);
  body.append(card(sub("寫入鍵盤"),muted(`目前容量 ${bytes}／512 位元組`),
    muted("鍵盤不會回讀巨集內容，寫入後請實際測試指派的按鍵。"),
    muted("寫入後到「按鍵重設」把巨集指派給按鍵（功能清單裡的「巨集：名稱」）。"),
    row(btn("寫入巨集到鍵盤",()=>run("正在寫入巨集……",async()=>{await api("/api/macros/write",{});say("巨集已寫入鍵盤。")}),{primary:true,disabled:!connected()||!list.length}))));
};
