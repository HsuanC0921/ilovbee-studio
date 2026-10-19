// 燈效頁：示意圖、模式縮圖、亮度／速度／顏色、休眠、鍵盤設定
const CAP="#3A414C";
const rgbStr=(r,g,b)=>`rgb(${r|0},${g|0},${b|0})`;
function lightCells(){return S.keys.filter(k=>k.Id!==16&&k.Id!==89)}
function rrect(ctx,x,y,w,h,r){ctx.beginPath();ctx.roundRect(x,y,w,h,r)}

PAGES.lighting=body=>{
  title(body,"燈效");
  const L=connected()?unhex(S.st.lights):null,names=Effects.Names,cells=lightCells();
  const current=L&&L[10]<names.length?L[10]:-1;
  let chosen=current>=0?current:1,level=4,speed=4,colorIndex=7,fixedColor=0;
  const storedLevel=m=>L&&Effects.hasBrightness(m)&&L[56+2*m]>=1&&L[56+2*m]<=4?L[56+2*m]:4;
  const storedSpeed=m=>L&&Effects.hasSpeed(m)&&(L[57+2*m]>>4)>=1&&(L[57+2*m]>>4)<=4?L[57+2*m]>>4:4;
  const storedColor=m=>L&&Effects.hasColor(m)&&(L[57+2*m]&15)<=7?L[57+2*m]&15:7;
  const load0=m=>{level=storedLevel(m);speed=storedSpeed(m);colorIndex=storedColor(m);if(colorIndex<7)fixedColor=colorIndex};load0(chosen);
  const live=S.st.live.mode!=="off";

  // 示意圖
  const canvas=h("canvas",{width:Effects.Width,height:Effects.Height,style:{width:"100%",maxWidth:"560px"},"aria-label":"燈效示意圖"});
  const ctx=canvas.getContext("2d");
  const modeName=h("h2"),hint=h("div",{class:"muted"}),status=h("div",{class:"muted"});
  const applyBtn=btn("套用至鍵盤",()=>applyMode(),{primary:true});
  const mkRange=()=>h("input",{type:"range",min:1,max:4,step:1,style:{width:"220px"}});
  const levelIn=mkRange(),speedIn=mkRange(),levelTxt=h("span",{class:"muted"}),speedTxt=h("span",{class:"muted"});
  levelIn.oninput=()=>{level=+levelIn.value;sync()};speedIn.oninput=()=>{speed=+speedIn.value;sync()};
  const levelRow=row(h("label",null,"亮度"),levelIn,levelTxt),speedRow=row(h("label",null,"速度"),speedIn,speedTxt);
  const swatches=Effects.Palette.map((p,i)=>h("button",{class:"sw",title:Effects.PaletteNames[i],style:{background:rgbStr(...p)},onclick:()=>{if(chosen===2&&i===0){say("呼吸模式的「紅」在實機上會顯示為白色，請改選其他顏色。",true);return}fixedColor=colorIndex=i;sync()}}));
  const mixIn=h("input",{type:"checkbox",onclick:()=>{colorIndex=mixIn.checked?7:fixedColor;sync()}});
  const colorRow=row(h("label",null,"顏色"),h("div",{class:"row",style:{gap:"8px"}},swatches),h("label",{class:"chk",style:{width:"auto"}},mixIn,"混色"));
  const hero=h("div",{class:"hero"},canvas,h("div",null,modeName,hint,status),h("div",{class:"row",style:{gap:"32px",alignItems:"flex-start"}},h("div",{style:{display:"flex",flexDirection:"column",gap:"10px"}},levelRow,speedRow),colorRow),row(applyBtn));
  body.append(hero);

  // 縮圖
  const tiles=h("div",{class:"tiles"}),tileEls=[],usedChips=[];
  names.forEach((n,i)=>{
    const c=h("canvas",{width:Effects.Width,height:Effects.Height}),cx=c.getContext("2d");
    cx.fillStyle="#1B1F25";rrect(cx,0,0,Effects.Width,Effects.Height,18);cx.fill();
    cells.forEach((k,idx)=>{
      const col=Effects.sampleTinted(i,i===4||i===9||i===12?2.3:1.7,k.X+k.Width/2,k.Y+k.Height/2,idx,i===1||i===2?6:7),a=col[3];
      const mix=(lit,cap)=>cap+(lit-cap)*a;cx.fillStyle=rgbStr(mix(col[0],0x3A),mix(col[1],0x41),mix(col[2],0x4C));rrect(cx,k.X,k.Y,k.Width,k.Height,5);cx.fill();
    });
    const chip=h("span",{class:"chip",style:{display:"none"}},"使用中");usedChips.push(chip);
    const t=h("div",{class:"tile",onclick:()=>{chosen=i;load0(i);sync()}},c,h("b",null,n,chip));tileEls.push(t);tiles.append(t);
  });
  body.append(tiles);

  function sync(){
    modeName.textContent=names[chosen];hint.textContent=Effects.Hints[chosen];
    const hb=Effects.hasBrightness(chosen),hs=Effects.hasSpeed(chosen),hc=Effects.hasColor(chosen);
    levelRow.style.display=hb?"":"none";speedRow.style.display=hs?"":"none";colorRow.style.display=hc?"":"none";
    levelIn.value=level;levelTxt.textContent=`${level} / 4`+(level===1?" · 近乎熄滅":"");speedIn.value=speed;speedTxt.textContent=`${speed} / 4`;
    mixIn.checked=colorIndex===7;swatches.forEach((s,i)=>s.classList.toggle("on",colorIndex!==7&&i===colorIndex));
    const changed=chosen!==current||(hb&&level!==storedLevel(chosen))||(hs&&speed!==storedSpeed(chosen))||(hc&&colorIndex!==storedColor(chosen));
    applyBtn.disabled=S.busy||!L||!changed;
    status.textContent=!L?"示意圖僅供預覽；連接 USB 並讀取鍵盤後即可套用。":!changed?"示意圖僅供預覽 · 這是鍵盤目前使用的設定。":chosen!==current?`示意圖僅供預覽 · 目前：${current>=0?names[current]:"未知"} → 套用後：${names[chosen]}`:"示意圖僅供預覽 · 有尚未套用的調整。";
    tileEls.forEach((t,i)=>{t.classList.toggle("on",i===chosen);usedChips[i].style.display=i===current?"":"none"});
  }
  const still=matchMedia("(prefers-reduced-motion: reduce)").matches;let t0=performance.now(),last=0;
  const frame=now=>{
    S.timers.push(requestAnimationFrame(frame));if(now-last<40)return;last=now;
    const t=still?1.2:(now-t0)/1000*(Effects.hasSpeed(chosen)?Effects.SpeedScale[speed]:1);
    const bright=Effects.hasBrightness(chosen)?[0,.08,.8,.9,1][level]:1;
    ctx.globalAlpha=1;ctx.clearRect(0,0,Effects.Width,Effects.Height);ctx.fillStyle="#252A32";rrect(ctx,0,0,Effects.Width,Effects.Height,18);ctx.fill();
    cells.forEach((k,i)=>{
      ctx.globalAlpha=1;ctx.fillStyle=CAP;rrect(ctx,k.X,k.Y,k.Width,k.Height,5);ctx.fill();
      const c=Effects.sampleTinted(chosen,t,k.X+k.Width/2,k.Y+k.Height/2,i,colorIndex),a=c[3]*bright;if(a<.01)return;
      ctx.fillStyle=rgbStr(c[0],c[1],c[2]);ctx.globalAlpha=a*.28;rrect(ctx,k.X-5,k.Y-5,k.Width+10,k.Height+10,9);ctx.fill();
      ctx.globalAlpha=a*.92;rrect(ctx,k.X+2,k.Y+2,k.Width-4,k.Height-4,4);ctx.fill();
    });
  };
  S.timers.push(requestAnimationFrame(frame));sync();
  if(live)body.insertBefore(card(muted("目前有即時燈效（音樂律動／自訂燈光）正在執行。套用燈效時會先停止它並還原鍵盤原本的模式。")),hero);

  async function applyMode(){
    await run("正在讀取並套用燈效……",async()=>{
      if(S.st.live.mode!=="off"){await api("/api/live",{mode:"off"});await load()}
      const before=unhex(S.st.lights),after=before.slice(),m=chosen;after[10]=m;
      if(Effects.hasBrightness(m))after[56+2*m]=level;
      if(Effects.hasSpeed(m)||Effects.hasColor(m)){const hi=Effects.hasSpeed(m)?speed:before[57+2*m]>>4,lo=Effects.hasColor(m)?colorIndex:before[57+2*m]&15;after[57+2*m]=(hi<<4)|lo}
      if(hex(after)!==hex(before)){await api("/api/lights",{expected:hex(before),after:hex(after)});say("燈效已套用並完成讀回核對。")}else say("鍵盤目前已使用這些設定。");
      await load();go("lighting");
    });
  }
  // 以下為不需動畫的設定卡片
  async function patchLights(label,edit,done){
    await run(label,async()=>{
      if(S.st.live.mode!=="off"){await api("/api/live",{mode:"off"});await load()}
      const before=unhex(S.st.lights),after=before.slice();edit(after);
      if(hex(after)!==hex(before)){await api("/api/lights",{expected:hex(before),after:hex(after)});say(done)}
      await load();go("lighting");
    });
  }
  // 無線休眠：payload[24]，單位 30 秒
  const sleepBytes=[0x0A,0x14,0x28,0x3C,0],sleepLabels=["5 分鐘","10 分鐘","20 分鐘","30 分鐘","永不休眠"];
  const storedSleep=L?sleepBytes.indexOf(L[24]):-1;
  const sleepIn=h("input",{type:"range",min:0,max:4,step:1,value:storedSleep>=0?storedSleep:1,disabled:storedSleep<0,style:{width:"260px"}});
  const sleepTxt=h("span",{style:{width:"90px"}}),sleepApply=btn("套用",()=>patchLights("正在套用休眠時間……",a=>{a[24]=sleepBytes[+sleepIn.value]},"休眠時間已套用並完成讀回核對。"));
  const sleepSync=()=>{sleepTxt.textContent=storedSleep>=0?sleepLabels[+sleepIn.value]:"尚未讀取";sleepApply.disabled=S.busy||storedSleep<0||+sleepIn.value===storedSleep};
  sleepIn.oninput=sleepSync;sleepSync();
  body.append(card(sub("無線休眠時間"),muted("藍牙模式下閒置多久後進入休眠。"),row(sleepIn,sleepTxt,sleepApply)));
  // 鍵盤設定：payload[3] 防抖（4＝辦公、1＝遊戲）；payload[22] Tap（0＝關閉，10..127＝時間，預設 20）
  const office=h("input",{type:"checkbox",checked:!!L&&L[3]===4,disabled:!L,onclick:()=>setSync()});
  const tapOn=h("input",{type:"checkbox",checked:!!L&&L[22]!==0,disabled:!L,onclick:()=>setSync()});
  const tapNum=h("input",{type:"number",min:10,max:127,value:L&&L[22]>=10&&L[22]<=127?L[22]:20,style:{width:"90px"},oninput:()=>setSync()});
  const setApply=btn("套用",()=>patchLights("正在套用鍵盤設定……",a=>{a[3]=office.checked?4:1;a[22]=tapOn.checked?Math.min(127,Math.max(10,Math.round(+tapNum.value||20))):0},"鍵盤設定已套用並完成讀回核對。"));
  function setSync(){
    tapNum.disabled=!tapOn.checked;if(!L){setApply.disabled=true;return}
    const tapChanged=tapOn.checked!==(L[22]!==0)||(tapOn.checked&&L[22]!==0&&Math.round(+tapNum.value)!==L[22]);
    setApply.disabled=S.busy||!(office.checked!==(L[3]===4)||tapChanged);
  }
  setSync();
  body.append(card(sub("鍵盤設定"),h("label",{class:"chk"},office,"辦公模式（防抖較長，減少連按誤觸；關閉為遊戲模式，反應最快）"),
    row(h("label",{class:"chk"},tapOn,"啟用 Tap"),h("label",null,"Tap 時間（預設 20）"),tapNum),setApply));
  // 側邊指示燈：payload[26]（0＝亮、1＝滅）。與 Fn+Backspace 切換的是同一個欄位，勾選即套用
  const sideOn=h("input",{type:"checkbox",checked:!!L&&L[26]===0,disabled:!L,onclick:()=>{const want=sideOn.checked;patchLights(want?"正在開啟側燈……":"正在關閉側燈……",a=>{a[26]=want?0:1},want?"側邊指示燈已開啟。":"側邊指示燈已關閉。")}});
  body.append(card(sub("側邊指示燈"),h("label",{class:"chk"},sideOn,"顯示鍵盤兩側的指示燈"),muted("顏色由韌體固定（綠色），目前沒有可改色的設定欄位。")));
  body.append(card(sub("鍵盤快捷鍵"),row(h("span",{class:"chip"},"Fn"),"＋",h("span",{class:"chip"},"Scroll Lock"),muted("切換主背光效果")),
    row(h("span",{class:"chip"},"Fn"),"＋",h("span",{class:"chip"},"Backspace"),muted("切換整體 RGB 燈光（含側燈）"))));
};
