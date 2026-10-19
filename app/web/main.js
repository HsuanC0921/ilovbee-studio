// 啟動：載入按鍵座標與鍵盤狀態，之後定時檢查連線
(async()=>{
  shell();
  try{S.keys=await (await fetch("/keyboard.json")).json()}catch{say("無法載入鍵盤配置。",true)}
  try{await load()}catch(e){say(e.message,true)}
  go(S.page);say(S.st?.connected?"鍵盤設定已同步。":"找不到相容 USB 設定介面。請連接 USB 並切至有線模式。");
  // 托盤選單也能切換即時燈效；自訂燈光頁開著時同步最新狀態
  setInterval(async()=>{
    if(S.page!=="custom"||S.busy||document.hidden||!S.st)return;
    try{const l=await api("/api/live"),c=S.st.live;
      if(l.mode!==c.mode||l.style!==c.style||l.status!==c.status){Object.assign(c,l);cust.style=l.style;if(!document.activeElement?.closest?.(".card input,select"))go("custom")}}catch{}
  },2500);
  setInterval(async()=>{
    if(S.busy||document.hidden||changeCount()>0)return;
    try{const p=await api("/api/ping");if(p.connected!==!!S.st?.connected){await load();go(S.page);say(p.connected?"鍵盤已連線。":"鍵盤未連線，請使用 USB 連接以編輯設定。")}}catch{}
  },8000);
})();
