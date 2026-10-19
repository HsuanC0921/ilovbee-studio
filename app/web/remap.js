// 按鍵重設
const LAYERS=["預設層","FN1 層","FN2 層","Tap 層"];
const KEY_LABEL={PrtSc:"Prt",SrcLock:"Scr",CapsLock:"Caps",Backspace:"Back",LShift:"Shift",RShift:"Shift",LControl:"Ctrl",RControl:"Ctrl"};
const plainKeys=()=>{const seen=new Set();return S.st.actions.filter(a=>/^000000(?!00)[0-9A-F]{2}$/.test(a.h)&&!seen.has(a.h)&&seen.add(a.h))};

function assign(slot,code){
  if(!S.draft||slot===59)return;
  const cur=S.draft[S.layer];if(slotHex(cur,slot)===code)return;
  S.undo.push({...S.draft});if(S.undo.length>100)S.undo.shift();S.redo=[];
  S.draft={...S.draft,[S.layer]:cur.slice(0,slot*8)+code+cur.slice(slot*8+8)};
  saveDraft();go("remap");say("已加入變更，按「套用變更」儲存至鍵盤。");
}
const isDirty=(layer,slot)=>slotHex(S.base[layer],slot)!==slotHex(S.draft[layer],slot);

PAGES.remap=body=>{
  title(body,"按鍵重設","自訂按鍵、媒體控制與組合快捷鍵。");
  if(!connected()||!S.draft){body.append(card(sub("尚未讀取鍵盤"),muted("請連接 USB 並切至有線模式，再按「重新讀取」。")));return}
  S.afterNames=()=>go("remap");
  const count=changeCount();
  const layerSel=h("select",{onchange:e=>{S.layer=+e.target.value;go("remap")}},LAYERS.map((n,i)=>h("option",{value:i},n)));layerSel.value=S.layer;
  body.append(row(layerSel,btn("復原",()=>{if(!S.undo.length)return;S.redo.push({...S.draft});S.draft=S.undo.pop();saveDraft();go("remap")},{disabled:!S.undo.length}),
    btn("重做",()=>{if(!S.redo.length)return;S.undo.push({...S.draft});S.draft=S.redo.pop();saveDraft();go("remap")},{disabled:!S.redo.length}),
    h("span",{style:{flex:1}}),
    btn(count?`套用變更（${count}）`:"套用變更",()=>applyDraft(),{primary:true,disabled:!count})));
  body.append(muted(count?`${count} 顆按鍵尚未儲存 · 淺藍色為待套用變更`:"所有變更已儲存"));
  if(!S.sel)S.sel=S.keys.find(k=>k.Id===88)||S.keys[0];
  body.append(keyboardView((b,k)=>{
    const code=slotHex(S.draft[S.layer],k.Slot);
    let label=KEY_LABEL[k.Name]||k.Name;if(k.Id===88)label=code==="00080007"?"桌面":code==="00000068"?"F13":"Cal";
    b.textContent=label;if(label.length>4&&k.Width<40)b.style.fontSize="10px";
    const dirty=isDirty(S.layer,k.Slot);
    b.classList.toggle("sel",S.sel.Id===k.Id);b.classList.toggle("dirty",dirty&&S.sel.Id!==k.Id);
    b.title=`${k.Name} → ${nameOf(code)}${dirty?"（未儲存）":""}`;
    b.onclick=()=>{S.sel=k;go("remap")};
  },stage=>{
    const knob=h("button",{class:"knob",title:"旋鈕按下為鍵盤內建靜音；點此開啟按鍵測試",style:{left:"736px",top:"20px"},onclick:()=>go("test")});stage.append(knob);
  }));
  const k=S.sel,slot=k.Slot,cur=slotHex(S.draft[S.layer],slot),old=slotHex(S.base[S.layer],slot);
  const name=k.Id===88?"小算盤鍵":k.Name;
  const left=h("div",{style:{display:"flex",flexDirection:"column",gap:"12px"}},muted("目前指派"),h("div",{style:{fontSize:"16px",whiteSpace:"pre-line"}},name+"\n"+nameOf(old)+(cur!==old?"\n待套用："+nameOf(cur):"")),
    btn("取消此鍵變更",()=>assign(slot,old),{disabled:cur===old}));
  const protectedKey=slot===59;
  const list=h("div",{class:"list"});let chosen=null;
  const search=h("input",{type:"text",placeholder:"搜尋功能，例如 F13、截圖、靜音",style:{width:"100%"},oninput:()=>fill()});
  const fill=()=>{list.replaceChildren(...S.st.actions.filter(a=>a.n.toLowerCase().includes(search.value.toLowerCase())).map(a=>
    h("div",{class:a.h===cur?"on":"",onclick:()=>{chosen=a;[...list.children].forEach(c=>c.classList.remove("on"));event.currentTarget.classList.add("on");add.disabled=false}},a.n)))};
  const add=btn("加入變更",()=>chosen&&assign(slot,chosen.h),{disabled:true});
  const funcTab=h("div",{style:{display:"flex",flexDirection:"column",gap:"8px"}},search,list,add);fill();
  const mods=[["Ctrl",1],["Shift",2],["Alt",4],["Win",8]].map(([n,v])=>({n,v,box:h("input",{type:"checkbox"})}));
  const mainKey=h("select",null,h("option",{value:""},"選擇主鍵"),plainKeys().map(a=>h("option",{value:a.h},a.n)));
  const comboTab=h("div",{style:{display:"flex",flexDirection:"column",gap:"12px"}},row(mods.map(m=>h("label",{class:"chk"},m.box,m.n))),mainKey,
    btn("加入組合鍵",()=>{if(!mainKey.value){say("請選擇組合鍵的主鍵。");return}const m=mods.reduce((a,x)=>a|(x.box.checked?x.v:0),0);assign(slot,"00"+m.toString(16).padStart(2,"0").toUpperCase()+"00"+mainKey.value.slice(6))}));
  const panes=[funcTab,comboTab];const tabBtns=["功能","組合鍵"].map((t,i)=>h("button",{class:i?"":"on",onclick:()=>{tabBtns.forEach((b,j)=>b.classList.toggle("on",i===j));panes.forEach((p,j)=>p.style.display=i===j?"":"none")}},t));comboTab.style.display="none";
  const right=h("div",null,h("div",{class:"tabs"},tabBtns),h("div",{style:{paddingTop:"12px"}},funcTab,comboTab));
  body.append(card(protectedKey?muted("FN 鍵必須保留，無法變更。"):"",h("div",{class:"split"},left,protectedKey?h("div"):right)));
  const changes=[];for(const l of [0,1,2,3])for(let s=0;s<104;s++)if(isDirty(l,s))changes.push([l,s]);
  const keyName=s=>S.keys.find(k=>k.Slot===s)?.Name??"按鍵 "+s;
  body.append(h("details",{class:"card"},h("summary",null,`變更清單（${changes.length}）`),
    changes.map(([l,s])=>h("div",{class:"muted",style:{padding:"4px 0"}},`${LAYERS[l]} · ${keyName(s)}：${nameOf(slotHex(S.base[l],s))} → ${nameOf(slotHex(S.draft[l],s))}`)),
    row(btn("取消全部變更",()=>{S.undo=[];S.redo=[];S.draft={...S.base};saveDraft();go("remap")}),btn("匯出草稿",()=>download("B87-草稿.json",JSON.stringify({baseline:S.base,desired:S.draft}))))));
};
async function applyDraft(){
  await run("正在儲存按鍵設定……",async()=>{
    const n=changeCount();await api("/api/keymap",{expected:S.base,desired:S.draft});
    S.draft=null;await load();saveDraft();go("remap");say(`已儲存 ${n} 顆按鍵的變更。`);
  });
}
