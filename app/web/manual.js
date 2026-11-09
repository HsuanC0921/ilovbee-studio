// 操作資訊依 B87 隨附手冊；「鍵盤操作」不表示已有 USB 控制指令。
const MANUAL=[
  ["連線","USB 有線連線","連接 USB，將背面開關切到中間有線位置。","軟體可設定","status"],
  ["連線","藍牙配對／三裝置切換","開關切至 B。長按 Fn＋Q／W／E 約 3 秒進入配對；配對後短按同組快捷鍵切換裝置。Windows 8 以上及行動裝置選擇 B87 5.0。","鍵盤操作",null],
  ["連線","2.4G 接收器配對","開關切至 G。長按 Fn＋P，P 鍵快閃時插入接收器；停止閃爍表示配對完成。出廠已配對，通常不用重做。","鍵盤操作",null],
  ["電量","查看電量","無線模式按 Fn＋Enter，以數字列背光顯示電量；1 亮為約 10%，1 到 0 全亮為約 100%。軟體顯示 Windows 最近一次藍牙回報的數值。","部分支援","status"],
  ["燈光","背光亮度與速度","Fn＋↑／↓ 調整亮度，Fn＋←／→ 調整速度。也可在燈效頁調整後套用。","軟體可設定","lighting"],
  ["燈光","背光模式與單色","Fn＋Scroll 切換動態背光，Fn＋Home 切換單色背光。軟體提供模式 1–17、自訂 RGB 及混色。","軟體可設定","lighting"],
  ["燈光","側邊燈與關燈","手冊列有 Fn＋Insert 側邊光顏色切換，Fn＋Backspace 關閉背光及側邊燈。軟體可調主背光與指示燈開關；側燈切色仍需快捷鍵。","部分支援","lighting"],
  ["燈光","儲存在鍵盤的自訂背光","按 Fn＋1 選擇自訂背光，再按 Fn＋Print 進入錄製；按需要上色的按鍵，完成後再按 Fn＋Print 儲存退出。手冊註明 2、3 為固定燈效，不能錄製。","鍵盤操作",null],
  ["燈光","電腦即時逐鍵 RGB","自訂燈光頁可繪製顏色、指定逐鍵行為並儲存配置。此功能由程式持續傳送，和鍵盤自身儲存的背光錄製不同。","軟體可設定","custom"],
  ["系統","Windows／Mac 模式","Fn＋A 切換 Windows 模式；Fn＋S 切換 Mac 模式。這是鍵盤自身的模式；切換後請重新讀取設定。","鍵盤操作",null],
  ["系統","Win 鍵鎖定","按 Fn＋Win 切換 Win 鍵鎖定。手冊的 Win Lock 指示燈可用來確認狀態。","鍵盤操作",null],
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
    list.replaceChildren(...items.map(([group,name,description,status,page])=>h("section",{class:"manual-item"},
      h("div",{class:"manual-item-head"},h("h2",null,name),h("span",{class:"chip"},status)),
      h("p",null,description),h("div",{class:"row"},h("span",{class:"muted"},group),page?btn(page==="profiles"?"前往備份":page==="test"?"開啟按鍵測試":"前往設定",()=>go(page)):null))));
    if(!items.length)list.append(muted("沒有符合的功能，請更換搜尋文字或篩選條件。"));
  };
  search.addEventListener("input",draw);filter.addEventListener("change",draw);
  body.append(h("div",{class:"manual-tools"},h("label",{for:"manual-search"},"搜尋功能"),search,h("label",{for:"manual-filter"},"支援範圍"),filter),
    h("p",{class:"muted"},"「鍵盤操作」表示請在實體鍵盤執行。配對、系統切換、Win 鎖定與機內背光錄製目前沒有軟體控制按鈕。"),count,list);draw();
};
