// Run against an isolated demo host using Playwright CLI:
// playwright-cli run-code --filename scripts/check-lighting-ui.js
async page => {
  const original=await page.evaluate(async()=>{
    const state=await (await fetch('/api/state')).json();
    if(!state.demo)throw Error('This regression test requires --demo; real hardware is not modified.');
    return await (await fetch('/api/profile')).json();
  });
  const read=()=>page.evaluate(async()=>await(await fetch('/api/profile')).json());
  const post=(path,body)=>page.evaluate(async({path,body})=>{
    const response=await fetch(path,{method:'POST',body:JSON.stringify(body)});
    if(!response.ok)throw Error(await response.text());
  },{path,body});
  const hex=bytes=>Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('').toUpperCase();
  const unhex=text=>Array.from(text.match(/../g),x=>parseInt(x,16));
  const apply=async name=>{
    await page.locator('.tile').filter({hasText:name}).click();
    const button=page.getByRole('button',{name:'套用至鍵盤',exact:true});
    if(!await button.isEnabled())throw Error('Apply is incorrectly disabled for '+name);
    await button.click();await page.waitForFunction(()=>!S.busy);
    return unhex((await read()).Lighting);
  };
  try {
    // The remembered mode is already 11; changing only payload[10] would be a no-op.
    const before=unhex(original.Lighting),other=before.slice();other[9]=1;other[10]=11;other[3]=3;other[22]=0;
    await post('/api/lights',{expected:hex(before),after:hex(other)});
    await page.reload();await page.getByRole('navigation').getByRole('button',{name:'燈效',exact:true}).click();
    await page.getByText('目前：鍵盤背光配置',{exact:false}).waitFor();
    const on=await apply('霓虹流動');
    if(on[9]!==0||on[10]!==11)throw Error('Selecting a builtin mode did not leave the other lighting configuration');
    const off=await apply('關閉主背光');
    if(off[9]!==0||off[10]!==0)throw Error('Builtin off state mismatch');
    const again=await apply('霓虹流動');
    if(again[9]!==0||again[10]!==11)throw Error('Re-enabling a builtin mode failed');
    const settings=page.locator('.card').filter({has:page.getByRole('heading',{name:'鍵盤設定',exact:true})});
    await settings.getByRole('checkbox',{name:'啟用 Tap',exact:true}).check();
    await settings.getByRole('button',{name:'套用',exact:true}).click();await page.waitForFunction(()=>!S.busy);
    const tap=unhex((await read()).Lighting);
    if(tap[3]!==3||tap[22]!==20)throw Error('Changing Tap overwrote the original debounce value');
    const office=settings.getByRole('checkbox',{name:/辦公模式/});
    await office.check();await office.uncheck();
    await settings.getByRole('button',{name:'套用',exact:true}).click();await page.waitForFunction(()=>!S.busy);
    if(unhex((await read()).Lighting)[3]!==1)throw Error('Explicitly choosing game mode did not change debounce');
    return {status:'pass',checks:['other configuration is identified','remembered mode can be applied and enables builtin path','off applies correctly','builtin mode can be re-enabled','Tap preserves original debounce','explicit debounce change applies']};
  } finally {await post('/api/restore',original);}
}
