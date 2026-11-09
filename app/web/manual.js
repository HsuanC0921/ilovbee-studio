// 操作資訊依 B87 隨附手冊；「鍵盤操作」不表示已有 USB 控制指令。
const MANUAL=[
  ["連線","USB 有線連線","連接 USB，將背面開關切到中間有線位置。","軟體可設定","status"],
  ["連線","藍牙配對／三裝置切換","開關切至 B。長按 Fn＋Q／W／E 約 3 秒進入配對；配對後短按同組快捷鍵切換裝置。Windows 8 以上及行動裝置選擇 B87 5.0。","鍵盤操作",null],
  ["連線","2.4G 接收器配對","開關切至 G。長按 Fn＋P，P 鍵快閃時插入接收器；停止閃爍表示配對完成。出廠已配對，通常不用重做。","鍵盤操作",null],
  ["電量","查看電量","無線模式按 Fn＋Enter，以數字列背光顯示電量；1 亮為約 10%，1 到 0 全亮為約 100%。軟體顯示 Windows 最近一次藍牙回報的數值。","部分支援","status"],
  ["燈光","背光亮度與速度","Fn＋↑／↓ 調整亮度，Fn＋←／→ 調整速度。也可在燈效頁調整後套用。","軟體可設定","lighting"],
  ["燈光","背光模式與單色","Fn＋Scroll 切換動態背光，Fn＋Home 切換單色背光。軟體提供模式 1–17、自訂 RGB 及混色。","軟體可設定","lighting"],
  ["燈光","側邊燈與關燈","Fn＋Insert 切換側燈顏色，也可在燈效頁選擇側燈色彩。Fn＋Backspace 切換背光及側邊燈。側燈切色可另外指派到實體按鍵。","軟體可設定","lighting",["08000001"]],
  ["燈光","儲存在鍵盤的自訂背光","按 Fn＋1 選擇自訂背光，再按 Fn＋Print 進入錄製；按需要上色的按鍵，完成後再按 Fn＋Print 儲存退出。可將「機內背光 1」和「機內背光錄製／儲存」分別指派到實體按鍵，再依相同順序操作。2、3 為固定燈效，不能錄製。錄製會更新鍵盤原有圖樣，軟體備份不含此圖樣。","部分支援",null,["08092000","08070000"]],
  ["燈光","電腦即時逐鍵 RGB","自訂燈光頁可繪製顏色、指定逐鍵行為並儲存配置。此功能由程式持續傳送，和鍵盤自身儲存的背光錄製不同。","軟體可設定","custom"],
  ["系統","Windows／Mac 模式","在裝置狀態頁讀取或切換鍵盤系統模式。也可用 Fn＋A 切換 Windows、Fn＋S 切換 Mac，或將兩種功能分別指派到實體按鍵。","軟體可設定","status",["0700000E","0700000F"]],
  ["系統","Win 鍵鎖定","在裝置狀態頁讀取或切換 Win Lock，也可按 Fn＋Win，或將「Win Lock 切換」指派到實體按鍵。使用快捷鍵後請重新讀取。","軟體可設定","status",["07000001"]],
  ["系統","恢復出廠設定","長按 Fn＋空白鍵約 3 秒。此操作會清除鍵盤設定，操作前請先到設定檔頁建立備份。","鍵盤操作","profiles"],
  ["多媒體","旋鈕音量與靜音","順時針提高音量，逆時針降低音量，按下切換靜音。旋鈕未有已確認的重新指派方式。","鍵盤操作","test"],
  ["多媒體","Windows Fn 多媒體鍵","Windows 模式：Fn＋F1 電腦、F2 瀏覽器、F3 郵件、F4 小算盤、F5 播放器、F6 停止、F7 上一首、F8 播放／暫停、F9 下一首、F10 靜音、F11／F12 音量。軟體可重新指派已支援的媒體動作。","部分支援","remap"],
  ["多媒體","Mac Fn 多媒體鍵","Mac 模式：Fn＋F1／F2 螢幕亮度、F5／F6 鍵盤亮度、F7 上一首、F8 播放／暫停、F9 下一首、F10 靜音、F11／F12 音量。","鍵盤操作",null]
];
PAGES.manual=body=>{
  title(body,"手冊功能","查找 B87 的連線、快捷鍵與軟體支援範圍。");
  const search=h("input",{id:"manual-search",type:"search",placeholder:"搜尋功能或快捷鍵，例如：Mac、配對、側燈"});
  const filter=h("select",{id:"manual-filter"},["全部功能","軟體可設定","部分支援","鍵盤操作"].map(n=>h("option",{value:n},n)));
  const count=h("p",{class:"muted","aria-live":"polite"});
  const list=h("div",{class:"manual-list"});
  const draw=()=>{
    const q=search.value.trim().toLocaleLowerCase();
    const items=MANUAL.filter(m=>(filter.value==="全部功能"||m[3]===filter.value)&&m.slice(0,4).join(" ").toLocaleLowerCase().includes(q));
    count.textContent=`${items.length} 項功能`;
    list.replaceChildren(...items.map(([group,name,description,status,page,actions=[]])=>h("section",{class:"manual-item"},
      h("div",{class:"manual-item-head"},h("h2",null,name),h("span",{class:"chip"},status)),
      h("p",null,description),h("div",{class:"row"},h("span",{class:"muted"},group),page?btn(page==="profiles"?"前往備份":page==="test"?"開啟按鍵測試":"前往設定",()=>go(page)):null,
        actions.map(code=>{const action=S.st.actions.find(a=>a.h===code);return action?btn("指派："+action.n,()=>{S.actionQuery=action.n;go("remap");say("選擇實體按鍵，再選取功能、加入變更並套用。按該鍵才會觸發。")}):null})))));
    if(!items.length)list.append(muted("沒有符合的功能，請更換搜尋文字或篩選條件。"));
  };
  search.addEventListener("input",draw);filter.addEventListener("change",draw);
  body.append(h("div",{class:"manual-tools"},h("label",{for:"manual-search"},"搜尋功能"),search,h("label",{for:"manual-filter"},"支援範圍"),filter),
    h("p",{class:"muted"},"「指派」會前往按鍵草稿，套用後需按實體鍵觸發。機內背光錄製仍由鍵盤執行；即時燈效執行時，請先停止，再操作機內背光。"),count,list);draw();
};
