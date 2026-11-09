// Playwright CLI: run-code --filename scripts/check-keyboard-ui.js
// Requires an isolated --demo host. Never run these writes on real hardware.
async page => {
  const original=await page.evaluate(async()=>{
    const state=await(await fetch('/api/state')).json();
    if(!state.demo)throw Error('This test requires --demo.');
    return await(await fetch('/api/profile')).json();
  });
  const checks=[];
  const read=()=>page.evaluate(async()=>await(await fetch('/api/profile')).json());
  const unhex=s=>Array.from(s.match(/../g),b=>parseInt(b,16));
  const wait=()=>page.waitForFunction(()=>!S.busy);
  const nav=async name=>page.getByRole('navigation').getByRole('button',{name,exact:true}).click();
  const verify=async(before,changes)=>{
    const actual=await read(),want=unhex(before.Lighting);
    for(const [offset,value] of Object.entries(changes))want[+offset]=value;
    if(JSON.stringify(unhex(actual.Lighting))!==JSON.stringify(want))throw Error('Unrelated keyboard setting changed');
    if(JSON.stringify(actual.Keymaps)!==JSON.stringify(before.Keymaps))throw Error('Keyboard setting changed keymaps');
    return actual;
  };
  try {
    await nav('燈效');
    let before=await read();
    const color=(unhex(before.Lighting)[18]+1)%11;
    await page.getByRole('combobox',{name:'側燈色彩',exact:true}).selectOption(String(color));
    await page.getByRole('button',{name:'套用側燈色彩',exact:true}).click();await wait();
    await verify(before,{18:color});checks.push('side color changes only its field');
    await nav('裝置狀態');before=await read();
    await page.getByRole('checkbox',{name:'Win Lock（鎖定 Win 鍵）',exact:true}).setChecked(unhex(before.Lighting)[15]!==1);await wait();
    await verify(before,{15:unhex(before.Lighting)[15]===1?0:1});checks.push('Win Lock read/write');
    for(const [mode,value] of [['mac',1],['windows',0]]) {
      before=await read();
      await page.getByRole('combobox',{name:'鍵盤系統模式',exact:true}).selectOption(mode);
      await page.getByRole('button',{name:'套用系統模式',exact:true}).click();await wait();
      await verify(before,{0:value});checks.push(mode+' mode read/write');
    }
    before=await read();
    await page.evaluate(async before=>{
      const b=before.Lighting.match(/../g);b[25]=b[25]==='91'?'92':'91';
      const r=await fetch('/api/lights',{method:'POST',body:JSON.stringify({expected:before.Lighting,after:b.join('')})});
      if(!r.ok)throw Error(await r.text());
    },before);
    const changedOutside=await read(),locked=unhex(before.Lighting)[15]===1;
    await page.getByRole('checkbox',{name:'Win Lock（鎖定 Win 鍵）',exact:true}).setChecked(!locked);await wait();
    if(!await page.locator('#msg').getByText('已變更',{exact:false}).isVisible())throw Error('Stale state did not show a refresh error');
    if(await page.getByRole('checkbox',{name:'Win Lock（鎖定 Win 鍵）',exact:true}).isChecked()!==locked)throw Error('Rejected Win Lock change is displayed as applied');
    if((await read()).Lighting!==changedOutside.Lighting)throw Error('Stale UI overwrote a physical change');
    await page.getByRole('button',{name:'重新讀取',exact:true}).click();await wait();
    checks.push('stale controls reject writes and restore displayed state');
    await nav('手冊功能');
    const recording=page.locator('.manual-item').filter({has:page.getByRole('heading',{name:'儲存在鍵盤的自訂背光',exact:true})});
    if(!await recording.getByText('軟體備份不含此圖樣',{exact:false}).isVisible())throw Error('Onboard recording warning missing');
    await recording.getByRole('button',{name:'指派：機內背光錄製／儲存（鍵盤內建）',exact:true}).click();
    await page.locator('.key[title^="Cal →"]').click();
    if(await page.getByRole('searchbox',{name:'搜尋按鍵功能',exact:true}).inputValue()!=='機內背光錄製／儲存（鍵盤內建）')throw Error('Native function was lost when choosing a key');
    await page.locator('.list > div').getByText('機內背光錄製／儲存（鍵盤內建）',{exact:true}).click();
    await page.getByRole('button',{name:'加入變更',exact:true}).click();
    before=await read();
    await page.getByRole('button',{name:'套用變更（1）',exact:true}).click();await wait();
    const after=await read(),want={...before.Keymaps};
    want['0']=want['0'].slice(0,78*8)+'08070000'+want['0'].slice(79*8);
    if(JSON.stringify(after.Keymaps)!==JSON.stringify(want)||after.Lighting!==before.Lighting)throw Error('Native action assignment did not preserve other settings');
    checks.push('manual recording warning and physical action assignment');
    await nav('裝置狀態');
    const requests=[];
    const track=request=>{if(/\/api\/(state|profile|keyboard|lights)$/.test(new URL(request.url()).pathname))requests.push(request.url());};
    page.on('request',track);await page.waitForTimeout(9000);page.off('request',track);
    if(requests.length)throw Error('Idle page repeatedly reads or writes hardware settings');
    checks.push('idle page does not poll keyboard settings');
    return {status:'pass',checks};
  } finally {
    await page.evaluate(async original=>{
      const r=await fetch('/api/restore',{method:'POST',body:JSON.stringify(original)});
      if(!r.ok)throw Error(await r.text());
      localStorage.clear();
    },original);
    await page.reload();
  }
}
