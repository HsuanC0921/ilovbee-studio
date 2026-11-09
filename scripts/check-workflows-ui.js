// C# workflow regression on an isolated --demo host. Uses real UI controls;
// File-picker contents are a test double. Native dialogs are tested separately with CLI.
async page => {
  const read=path=>page.evaluate(async path=>await(await fetch(path)).json(),path);
  const post=(path,body)=>page.evaluate(async({path,body})=>{
    const r=await fetch(path,{method:'POST',body:JSON.stringify(body)});if(!r.ok)throw Error(await r.text());return await r.json();
  },{path,body});
  const state=await read('/api/state'),original=await read('/api/profile');
  if(!state.demo)throw Error('Demo required.');
  const checks=[];
  await page.evaluate(()=>{window.__workflowPickFile=pickFile;window.__workflowConfirm=confirm;window.confirm=()=>true});
  const wait=()=>page.waitForFunction(()=>!S.busy);
  const click=async name=>{await page.getByRole('button',{name,exact:true}).click();await wait()};
  const nav=name=>page.getByRole('navigation').getByRole('button',{name,exact:true}).click();
  const download=async name=>{
    const event=page.waitForEvent('download');await click(name);const file=await event;
    if(await file.failure())throw Error('Download failed');return file.suggestedFilename();
  };
  const upload=async(name,value)=>{
    await page.evaluate(value=>{window.pickFile=async()=>JSON.stringify(value)},value);
    await click(name);
  };
  try {
    await nav('巨集');await click('新增');
    const macro=page.getByRole('textbox',{name:'巨集名稱',exact:true});
    await macro.fill('<b>測試</b>');await macro.press('Tab');await wait();
    if(await page.locator('.list b').count())throw Error('Macro name became markup');
    await page.getByRole('combobox',{name:'新增事件按鍵',exact:true}).selectOption('4');
    await click('加入');
    await click('開始錄製');await page.keyboard.press('b');await click('停止並儲存');
    const events=(await read('/api/state')).macros.at(-1).Events;
    if(events.length!==2||events.some(e=>e.Code!==5)||events[0].Up||!events[1].Up)throw Error('Macro recording round trip failed');
    await click('複製');await click('刪除');
    if(await download('匯出巨集庫')!=='B87-巨集庫.json')throw Error('Wrong macro export');
    await upload('匯入巨集（加到清單後面）',[{Name:'匯入測試',Events:[{Code:4,Up:false,Delay:10},{Code:4,Up:true,Delay:0}]}]);
    if(!(await read('/api/state')).macros.some(m=>m.Name==='匯入測試'))throw Error('Macro import failed');
    await click('寫入巨集到鍵盤');checks.push('macro edit, record, duplicate, delete, import, export, upload; names are text');
    await nav('按鍵重設');
    await page.getByRole('combobox',{name:'按鍵層',exact:true}).selectOption('0');
    await page.locator('.key[title^="Cal →"]').click();
    const target=original.Keymaps['0'].slice(78*8,79*8)==='00000068'?['F14','00000069']:['F13（供巨集／腳本使用）','00000068'];
    await page.getByRole('searchbox',{name:'搜尋按鍵功能',exact:true}).fill(target[0]);
    await page.locator('.list').getByRole('button',{name:target[0],exact:true}).click();await click('加入變更');
    await click('復原');if(await page.getByRole('button',{name:'套用變更',exact:true}).isEnabled())throw Error('Undo left a dirty draft');
    await click('重做');await page.reload();
    if(!await page.getByRole('button',{name:'套用變更（1）',exact:true}).isEnabled())throw Error('Draft did not resume');
    await click('套用變更（1）');
    const changed=await read('/api/profile');if(changed.Keymaps['0'].slice(78*8,79*8)!==target[1])throw Error('Mapping not applied');
    checks.push('mapping draft, undo, redo, reload resume, verified apply');
    await nav('設定檔');await page.getByRole('textbox',{name:'方案名稱',exact:true}).fill('UI 測試方案');await click('另存目前鍵盤為方案');
    await page.locator('.list').getByRole('button',{name:'UI 測試方案',exact:true}).click();
    await page.getByRole('textbox',{name:'方案名稱',exact:true}).fill('UI 測試改名');await click('重新命名');
    await page.locator('.list').getByRole('button',{name:'UI 測試改名',exact:true}).click();
    if(!String(await download('匯出方案')).endsWith('.json'))throw Error('Scheme export failed');
    const names=(await read('/api/state')).schemes,index=names.indexOf('UI 測試改名');
    const scheme=await read('/api/schemes/export?index='+index);scheme.Name='UI 匯入方案';await upload('匯入方案',scheme);
    await page.locator('.list').getByRole('button',{name:'UI 匯入方案',exact:true}).click();await click('套用到鍵盤');
    await click('立即建立完整備份');
    if(!await page.locator('.list').getByRole('button',{name:'還原',exact:true}).count())throw Error('Backup not listed');
    if(!String(await download('匯出完整設定')).startsWith('B87-'))throw Error('Profile export failed');
    await post('/api/restore',original);
    if((await read('/api/profile')).Keymaps['0']!==original.Keymaps['0'])throw Error('Profile file restore failed');
    checks.push('scheme create, rename, import, export, apply; backup create/export and profile API restore');
    await nav('自訂燈光');await click('彩虹');await page.waitForTimeout(250);
    await page.getByRole('textbox',{name:'配置名稱',exact:true}).fill('UI 燈光配置');await click('儲存目前顏色');
    await page.getByRole('combobox',{name:'燈光配置',exact:true}).selectOption({label:'UI 燈光配置'});await click('載入');
    await click('套用自訂燈光');await page.waitForFunction(async()=> (await(await fetch('/api/live')).json()).status==='自訂燈光');
    await click('停止並恢復原本燈效');
    await click('開始音樂律動');await page.waitForFunction(async()=> (await(await fetch('/api/live')).json()).status.startsWith('音樂律動'));
    await page.getByRole('combobox',{name:'音樂模式',exact:true}).selectOption('11');await page.waitForTimeout(150);
    await click('停止並恢復原本燈效');
    await page.getByRole('combobox',{name:'燈光配置',exact:true}).selectOption({label:'UI 燈光配置'});await click('刪除');
    checks.push('custom RGB paint, layout save/load/delete, stream start/stop, simulated music and style');
    await nav('按鍵測試');await click('開始測試');await page.keyboard.press('a');
    if(!await page.locator('textarea.log').inputValue().then(x=>x.includes('KeyA')))throw Error('Key test log missing');
    await click('清除紀錄');if(await page.locator('textarea.log').inputValue())throw Error('Key test clear failed');
    checks.push('keyboard event display and clear (synthetic UI input only)');
    return {status:'pass',checks};
  } finally {
    await post('/api/live',{mode:'off'});await post('/api/restore',original);
    await post('/api/macros/save',state.macros);
    const after=await read('/api/state');
    for(let i=after.schemes.length-1;i>=0;i--)if(!state.schemes.includes(after.schemes[i]))await post('/api/schemes/delete',{index:i});
    for(const layout of after.layouts)if(!state.layouts.some(l=>l.Name===layout.Name))await post('/api/layouts/delete',{name:layout.Name});
    await page.evaluate(()=>{window.pickFile=window.__workflowPickFile;window.confirm=window.__workflowConfirm;delete window.__workflowPickFile;delete window.__workflowConfirm;localStorage.removeItem('draft')});await page.reload();
  }
}
