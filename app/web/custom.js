// 自訂燈光：逐鍵上色、音樂律動、配置庫（由常駐程式持續送出畫面）
const FRAME_BYTES=126*3;
const cust={frame:null,paint:"#ff4000",gain:40,style:0,timer:0,cmode:0,cdir:0,colors:["#ff2060","#20c0ff"]};
function hsvRgb(hh,s,v){hh=((hh%360)+360)%360/60;const c=v*s,x=c*(1-Math.abs(hh%2-1)),m=v-c,i=Math.floor(hh);
  const [r,g,b]=[[c,x,0],[x,c,0],[0,c,x],[0,x,c],[x,0,c],[c,0,x]][i%6];return [r+m,g+m,b+m].map(z=>Math.round(z*255))}
const rowOf=k=>Math.min(5,Math.max(0,Math.round((k.Y-38)/43)));
const toHex6=(r,g,b)=>[r,g,b].map(x=>x.toString(16).padStart(2,"0")).join("");
function initCustom(){
  if(cust.frame)return;
  const saved=S.st.live.frame;const lib=S.st.layouts[0]?.Frame;
  cust.frame=unhex(saved&&/[1-9A-F]/.test(saved)?saved:(lib||"0".repeat(FRAME_BYTES*2)));
  cust.gain=S.st.live.gain;cust.style=S.st.live.style;cust.cmode=S.st.live.colormode;cust.cdir=S.st.live.colordir;cust.colors=S.st.live.colors.map(c=>"#"+c);
}
async function setLive(mode){
  await run("正在傳給常駐程式……",async()=>{
    await api("/api/live",{mode,style:cust.style,gain:cust.gain,frame:hex(cust.frame)});await load();go("custom");
    say(mode==="off"?"已停止即時燈效並還原原本的模式。":mode==="music"?"音樂律動執行中。播放任何聲音即可看到效果。":"自訂燈光已送到鍵盤。");
  });
}
// 只更新畫面（模式維持伺服器上的現況）；自訂燈光執行中時會立刻顯示在鍵盤上
function pushFrame(){
  clearTimeout(cust.timer);
  cust.timer=setTimeout(()=>api("/api/live",{mode:"keep",frame:hex(cust.frame)}).catch(e=>say(e.message,true)),120);
}
// 樣式／靈敏度同樣只送變更的欄位，不改模式
const tune=(o,msg)=>api("/api/live",{mode:"keep",...o}).then(()=>msg&&say(msg)).catch(e=>say(e.message,true));
PAGES.custom=body=>{
  title(body,"自訂燈光","逐鍵設定顏色，或讓燈光跟著電腦播放的聲音律動。由系統匣的常駐程式持續送出，關閉這個視窗也會繼續。");
  if(!connected()){body.append(card(sub("尚未讀取鍵盤"),muted("請連接 USB 並切至有線模式，再按「重新讀取」。")));return}
  initCustom();const live=S.st.live;
  const stateText=live.mode==="off"?"未執行":live.mode==="music"?"音樂律動執行中 · "+live.styles[live.style]:"自訂燈光執行中";
  const gain=h("input",{type:"range",min:5,max:200,step:5,value:cust.gain,style:{width:"260px"},oninput:()=>{cust.gain=+gain.value},onchange:()=>tune({gain:cust.gain})});
  const send=(redraw)=>tune({colormode:cust.cmode,colordir:cust.cdir,colors:cust.colors.map(c=>c.slice(1))},"已更新音樂配色。").then(()=>redraw&&go("custom"));
  const cmode=h("select",{onchange:()=>{cust.cmode=+cmode.value;send(true)}},[["彩虹混色（依樣式自動變色）",0],["自訂配色",1]].map(([n,v])=>h("option",{value:v},n)));cmode.value=cust.cmode;
  const cdir=h("select",{onchange:()=>{cust.cdir=+cdir.value;send()}},[["沿高度（柱子下到上）",0],["沿頻率（左低右高）",1],["隨時間流動",2],["依音量強弱",3]].map(([n,v])=>h("option",{value:v},n)));cdir.value=cust.cdir;
  const presets=[["火焰","#ff2000,#ff8000,#ffe000"],["海洋","#0040ff,#00c0ff,#80ffe0"],["霓虹","#ff00c0,#8000ff,#00e0ff"],["日落","#ffd000,#ff4080,#6020c0"],["森林","#00a040,#80e000"],["冰火","#ff3000,#ffffff,#0080ff"]];
  const chips=cust.colors.map((c,i)=>h("input",{type:"color",value:c,title:"顏色 "+(i+1)+"（右鍵移除）",oncontextmenu:e=>{e.preventDefault();if(cust.colors.length>1){cust.colors.splice(i,1);send(true)}},onchange:e=>{cust.colors[i]=e.target.value;send()}}));
  const custom=cust.cmode==1;
  const style=h("select",{onchange:()=>{cust.style=+style.value;tune({style:cust.style},"已切換音樂模式。")}},live.styles.map((n,i)=>h("option",{value:i},n)));style.value=cust.style;
  body.append(card(sub("目前狀態"),h("div",{style:{fontSize:"20px",color:live.mode==="off"?"var(--muted)":"var(--blue)"}},stateText),live.status&&live.status.startsWith("錯誤")?muted(live.status):"",
    row(btn("套用自訂燈光",()=>setLive("custom")),btn("開始音樂律動",()=>setLive("music")),btn("停止並恢復原本燈效",()=>setLive("off"),{disabled:live.mode==="off"})),
    row(h("label",null,"音樂模式"),style,h("label",null,"音樂靈敏度"),gain),
    row(h("label",null,"顏色"),cmode),
    custom?row(h("label",null,"配色"),...chips,btn("＋",()=>{if(cust.colors.length<6){cust.colors.push(cust.colors[cust.colors.length-1]);send(true)}},{disabled:cust.colors.length>=6}),muted("最多 6 色，依序漸變；右鍵點色塊可移除。")):"",
    custom?row(h("label",null,"漸層方向"),cdir):"",
    custom?row(h("label",null,"預設配色"),...presets.map(([n,c])=>btn(n,()=>{cust.colors=c.split(",");send(true)}))):""));

  // 逐鍵上色
  const buttons=new Map();
  const paintKey=k=>{const b=buttons.get(k.Id),o=k.Slot*3,[r,g,bl]=[cust.frame[o],cust.frame[o+1],cust.frame[o+2]];
    b.style.background=`rgb(${r},${g},${bl})`;b.style.color=(r*.3+g*.6+bl*.1)>140?"#111":"#fff";b.style.borderColor="#5a6270"};
  const kb=keyboardView((b,k)=>{b.textContent=k.Name.length>5?k.Name.slice(0,4):k.Name;b.style.fontSize="9px";buttons.set(k.Id,b);
    b.onclick=()=>{const rgb=unhex(cust.paint.slice(1));cust.frame.set(rgb,k.Slot*3);paintKey(k);pushFrame()}});
  S.keys.forEach(k=>{if(buttons.has(k.Id))paintKey(k)});
  const fill=fn=>{S.keys.forEach(k=>{if(!buttons.has(k.Id))return;cust.frame.set(fn(k),k.Slot*3);paintKey(k)});pushFrame()};
  const color=h("input",{type:"color",value:cust.paint,oninput:()=>{cust.paint=color.value}});
  body.append(card(sub("逐鍵顏色"),muted("先選顏色，再點按鍵上色。自訂燈光執行中時，變更會立即顯示在鍵盤上。"),
    h("div",{class:"row",style:{alignItems:"flex-start",gap:"24px"}},h("div",{style:{flex:1,minWidth:"320px"}},kb),
      h("div",{style:{display:"flex",flexDirection:"column",gap:"8px"}},color,
        btn("全部填入此色",()=>fill(()=>unhex(cust.paint.slice(1)))),
        btn("彩虹",()=>fill(k=>hsvRgb((k.X-20)/760*300,1,1))),
        btn("依排漸層",()=>fill(k=>hsvRgb(rowOf(k)*50,1,1))),
        btn("全部清除",()=>fill(()=>[0,0,0]))))));

  // 配置庫
  const layouts=S.st.layouts,sel=h("select",null,h("option",{value:""},"選擇配置"),layouts.map((l,i)=>h("option",{value:i},l.Name)));
  const name=h("input",{type:"text",maxlength:20,placeholder:"配置名稱"});
  body.append(card(sub(`燈光配置（${layouts.length}／20）`),
    row(sel,btn("載入",()=>{if(sel.value==="")return;cust.frame=unhex(layouts[+sel.value].Frame);pushFrame();go("custom")}),
      btn("刪除",()=>run("正在刪除……",async()=>{if(sel.value==="")return;await api("/api/layouts/delete",{name:layouts[+sel.value].Name});await load();go("custom")}))),
    row(name,btn("儲存目前顏色",()=>run("正在儲存……",async()=>{await api("/api/layouts/put",{name:name.value.trim()||"配置"+(layouts.length+1),frame:hex(cust.frame)});await load();go("custom");say("已儲存燈光配置。")})))));
};
