// 共用：DOM 小工具、API 呼叫、全域狀態、訊息列、頁面框架
const S={st:null,keys:[],page:"remap",busy:false,layer:0,sel:null,base:null,draft:null,undo:[],redo:[],timers:[]};
const ICONS={
  remap:'<rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6.5 10h.01M10 10h.01M14 10h.01M17.5 10h.01M7.5 14h9"/>',
  macros:'<rect x="3.5" y="3.5" width="17" height="17" rx="4.5"/><path d="M10 8.6v6.8l5.4-3.4z"/>',
  lighting:'<circle cx="12" cy="12" r="4"/><path d="M12 3.5v2M12 18.5v2M3.5 12h2M18.5 12h2M6 6l1.4 1.4M16.6 16.6L18 18M18 6l-1.4 1.4M7.4 16.6L6 18"/>',
  custom:'<path d="M12 3.5a8.5 8.5 0 100 17c1.3 0 2-.8 2-1.8 0-1.4-1.2-1.5-1.2-2.6 0-.9.7-1.6 1.7-1.6H17a3.5 3.5 0 003.5-3.5C20.5 6.7 16.7 3.5 12 3.5z"/><path d="M7.6 11h.01M10 7.8h.01M14.4 7.8h.01"/>',
  status:'<rect x="2.5" y="8" width="17" height="9" rx="2.5"/><path d="M21.5 11v3M6 12.5h6"/>',
  test:'<rect x="4" y="4" width="16" height="16" rx="4.5"/><path d="M8.8 12.4l2.3 2.3 4.2-4.6"/>',
  profiles:'<path d="M5 4h11l3 3v13H5z"/><path d="M8 4v5h7V4M8 20v-6h8v6"/>'};
const icon=id=>{const s=document.createElementNS("http://www.w3.org/2000/svg","svg");s.setAttribute("viewBox","0 0 24 24");s.setAttribute("class","ico");s.innerHTML=ICONS[id]||"";return s};
const NAV=[["remap","按鍵重設"],["macros","巨集"],["lighting","燈效"],["custom","自訂燈光"],["status","裝置狀態"],["test","按鍵測試"],["profiles","設定檔"]];

function h(tag,props,...kids){
  const e=document.createElement(tag);
  for(const [k,v] of Object.entries(props||{})){
    if(k==="class")e.className=v;
    else if(k.startsWith("on"))e.addEventListener(k.slice(2),v);
    else if(k==="style"&&typeof v==="object")Object.assign(e.style,v);
    else if(k==="value"||k==="checked"||k==="disabled")e[k]=v;
    else if(v===true)e.setAttribute(k,"");
    else if(v!==false&&v!=null)e.setAttribute(k,v);
  }
  for(const c of kids.flat(Infinity))if(c!==false&&c!=null)e.append(c.nodeType?c:document.createTextNode(String(c)));
  return e;
}
const card=(...k)=>h("div",{class:"card"},...k);
const sub=t=>h("h3",null,t);
const muted=t=>h("div",{class:"muted"},t);
const row=(...k)=>h("div",{class:"row"},...k);
const btn=(label,fn,o={})=>h("button",{class:"btn"+(o.primary?" primary":""),disabled:!!o.disabled,onclick:fn,title:o.title},label);

async function api(path,body){
  const res=await fetch(path,body===undefined?{}:{method:"POST",body:JSON.stringify(body)});
  const text=await res.text();
  if(!res.ok){let msg=text;try{msg=JSON.parse(text)}catch{}throw new Error(typeof msg==="string"?msg:"操作失敗。")}
  return text?JSON.parse(text):null;
}
function say(msg,err=false){const f=document.getElementById("msg");if(!f)return;f.textContent=msg;f.className=err?"err":""}
async function run(label,fn){
  if(S.busy){say("前一個操作尚未完成，請稍候。");return}
  S.busy=true;say(label);
  try{await fn()}catch(e){say(e.message,true)}finally{S.busy=false}
}
const hex=b=>Array.from(b,x=>x.toString(16).padStart(2,"0")).join("").toUpperCase();
const unhex=s=>Uint8Array.from(s.match(/../g)||[],x=>parseInt(x,16));
const slotHex=(layerHex,slot)=>layerHex.substr(slot*8,8);
const connected=()=>!!(S.st&&S.st.connected&&S.st.lights);
function download(name,text,type="application/json"){const a=h("a",{href:URL.createObjectURL(new Blob([text],{type})),download:name});document.body.append(a);a.click();a.remove()}
function pickFile(accept=".json"){return new Promise(res=>{const i=h("input",{type:"file",accept});i.onchange=async()=>res(i.files[0]?await i.files[0].text():null);i.click()})}
function clearTimers(){S.timers.forEach(t=>{clearInterval(t);cancelAnimationFrame(t)});S.timers=[]}

// 按鍵名稱：已知功能的名稱表，未知的向伺服器查詢
const names={map:new Map(),pending:new Set()};
function nameOf(h8){
  if(S.st){for(const a of S.st.actions)if(!names.map.has(a.h))names.map.set(a.h,a.n)}
  if(names.map.has(h8))return names.map.get(h8);
  if(!names.pending.has(h8)){names.pending.add(h8);api("/api/decode",{codes:[h8]}).then(r=>{names.map.set(h8,r[0]);if(S.afterNames)S.afterNames()}).catch(()=>{})}
  return "…";
}

// 載入狀態；保留尚未套用的草稿（鍵盤內容沒變時）
async function load(){
  S.st=await api("/api/state");
  if(S.st.maps){
    const same=S.base&&[0,1,2,3].every(l=>S.base[l]===S.st.maps[l]);
    S.base={...S.st.maps};
    if(!same){S.draft=null;S.undo=[];S.redo=[];}
    if(!S.draft){
      S.draft={...S.base};
      try{const saved=JSON.parse(localStorage.getItem("draft")||"null");
        if(saved&&[0,1,2,3].every(l=>saved.baseline[l]===S.base[l]))S.draft={...saved.desired}}catch{}
    }
    for(const l of [0,1,2,3])for(let s=0;s<104;s++)names.map.set(slotHex(S.base[l],s),S.st.names[l][s]);
  }else{S.base=null;S.draft=null}
  updateHeader();
}
function saveDraft(){try{if(S.base)localStorage.setItem("draft",JSON.stringify({baseline:S.base,desired:S.draft}))}catch{}}
function changeCount(){if(!S.base)return 0;let n=0;for(const l of [0,1,2,3])for(let s=0;s<104;s++)if(slotHex(S.base[l],s)!==slotHex(S.draft[l],s))n++;return n}

function updateHeader(){
  const dev=document.getElementById("dev"),pw=document.getElementById("pw");if(!dev)return;
  dev.textContent=S.st?.connected?"B87 · USB 已連線":"B87 · USB 未連線";
  pw.textContent=S.st?.battery!=null?`電量 ${S.st.battery}% · Windows 最近回報`:"電量尚未取得";
}
// 在 Tauri 視窗（無系統標題列）中，拖曳區與視窗控制鍵覆蓋在版面最上方；一般瀏覽器不出現
function titlebar(){
  const w=window.__TAURI__?.window?.getCurrentWindow?.();if(!w)return null;
  document.documentElement.classList.add("tauri");
  const c=(label,glyph,fn,cls)=>h("button",{class:cls,tabindex:-1,"aria-label":label,title:label,onclick:fn},glyph);
  return h("div",{id:"titlebar","data-tauri-drag-region":""},
    h("div",{class:"wc"},c("最小化","",()=>w.minimize()),c("最大化","",()=>w.toggleMaximize()),c("關閉","",()=>w.close(),"x")));
}
function shell(){
  const app=document.getElementById("app");const tb=titlebar();if(tb&&!document.getElementById("titlebar"))app.before(tb);app.replaceChildren(
    h("aside",{"data-tauri-drag-region":""},h("div",{class:"brand"},h("b",null,"B87"),h("span",null,"鍵盤設定")),
      h("nav",null,NAV.map(([id,name])=>h("button",{id:"nav-"+id,onclick:()=>go(id),title:name},icon(id),h("span",null,name))))),
    h("main",null,
      h("header",{"data-tauri-drag-region":""},h("div",null,h("b",{id:"dev"},"B87 · 讀取中"),h("small",{id:"pw"},"")),btn("重新讀取",()=>refresh())),
      h("div",{id:"body"}),h("footer",{id:"msg"},"就緒")));
}
const PAGES={};
function go(id){
  clearTimers();S.afterNames=null;S.page=id;
  for(const [n] of NAV)document.getElementById("nav-"+n).classList.toggle("on",n===id);
  const body=document.getElementById("body");body.replaceChildren();PAGES[id](body);
}
async function refresh(){
  if(changeCount()>0){say("請先套用或取消按鍵變更，再重新讀取。");return}
  await run("正在讀取鍵盤……",async()=>{await load();go(S.page);say(S.st.connected?"鍵盤設定已同步。":"找不到相容 USB 設定介面。請連接 USB 並切至有線模式。")});
}
function title(body,t,lead){body.append(h("h1",null,t),lead?h("p",{class:"lead"},lead):"")}

// 鍵盤畫面（800×300 座標）：fn 回傳每顆鍵的設定
function keyboardView(each,extra){
  const stage=h("div",{class:"stage"});
  for(const k of S.keys){
    if(k.Id===16||k.Id===89)continue;
    const b=h("button",{class:"key",title:k.Name,style:{left:(k.X-4)+"px",top:(k.Y-15)+"px",width:(k.Width+8)+"px",height:(k.Height+6)+"px"}});
    each(b,k);stage.append(b);
  }
  if(extra)extra(stage);
  const wrap=h("div",{class:"stage-wrap"},stage);
  const fit=()=>{const s=Math.min(1,wrap.clientWidth/800);stage.style.transform=`scale(${s})`;wrap.style.height=(300*s)+"px"};
  new ResizeObserver(fit).observe(wrap);requestAnimationFrame(fit);
  return wrap;
}
