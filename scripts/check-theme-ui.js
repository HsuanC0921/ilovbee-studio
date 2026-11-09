// Native-sized browser rendering checks; window operations are mocked in this capture.
// Real native window rendering and caption controls are checked separately.
async page => {
  const state=await page.evaluate(async()=>await(await fetch('/api/state')).json());
  if(!state.demo)throw Error('Demo required.');
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  const checks=[],screens=[];
  await page.addInitScript(()=>{
    window.__TAURI__={window:{getCurrentWindow:()=>({minimize:async()=>{},toggleMaximize:async()=>{},close:async()=>{}})}};
  });
  await page.reload();
  const nav=name=>page.getByRole('navigation').getByRole('button',{name,exact:true}).click();
  try {
    await page.emulateMedia({colorScheme:'dark'});
    await page.getByRole('combobox',{name:'介面主題',exact:true}).selectOption('system');
    await page.waitForFunction(()=>document.documentElement.dataset.theme==='dark');
    await page.getByRole('combobox',{name:'介面主題',exact:true}).selectOption('light');await page.reload();
    if(await page.evaluate(()=>document.documentElement.dataset.theme)!=='light')throw Error('Manual theme did not persist');
    await page.getByRole('combobox',{name:'介面主題',exact:true}).selectOption('system');
    await page.emulateMedia({colorScheme:'light'});
    await page.waitForFunction(()=>document.documentElement.dataset.theme==='light');
    checks.push('system follows changes; manual override persists');
    for(const [width,theme] of [[1440,'light'],[1440,'dark'],[1030,'light'],[1030,'dark'],[600,'dark']]){
      await page.setViewportSize({width,height:900});
      await page.getByRole('combobox',{name:'介面主題',exact:true}).selectOption(theme);
      for(const name of ['按鍵重設','巨集','燈效','自訂燈光','裝置狀態','按鍵測試','設定檔','手冊功能']){
        await nav(name);
        const result=await page.evaluate(()=>{
          const rgb=value=>(value.match(/[\d.]+/g)||[]).slice(0,3).map(Number);
          const luminance=values=>values.map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);
          const contrast=(a,b)=>{const x=luminance(rgb(a)),y=luminance(rgb(b));return (Math.max(x,y)+.05)/(Math.min(x,y)+.05)};
          const back=element=>{
            for(let e=element;e;e=e.parentElement){const c=getComputedStyle(e).backgroundColor;if(c!=='rgba(0, 0, 0, 0)'&&c!=='transparent')return c}
            return 'rgb(255,255,255)';
          };
          const buttons=[...document.querySelectorAll('nav button')].map(button=>{
            const span=button.querySelector('span'),bounds=span.getBoundingClientRect(),style=getComputedStyle(button);
            return {text:span.textContent,visible:bounds.width>0&&bounds.height>0,ratio:contrast(style.color,back(button))};
          });
          const body=document.getElementById('body');
          return {buttons,opaque:getComputedStyle(document.querySelector('aside')).backgroundColor!=='rgba(0, 0, 0, 0)',
            overflow:document.documentElement.scrollWidth>innerWidth+1||body.scrollWidth>body.clientWidth+1||document.documentElement.scrollHeight>innerHeight+1,
            invalidObject:body.textContent.includes('[object Object]')};
        });
        if(result.overflow||!result.opaque||result.invalidObject||result.buttons.some(b=>!b.visible||b.ratio<4.5))throw Error(JSON.stringify({width,theme,name,...result}));
        checks.push(`${width} ${theme} ${name}: labels, contrast, layout`);
        if(name==='按鍵重設'||(name==='巨集'&&width===1440&&theme==='dark')){
          const file=`.impeccable/review/${width}-${theme}-${name==='巨集'?'macros':'remap'}.png`;
          await page.screenshot({path:file,fullPage:true});screens.push(file);
        }
      }
    }
    if(errors.length)throw Error(errors.join('\n'));
    return {status:'pass',checks,screens,errors};
  } finally {
    await page.getByRole('combobox',{name:'介面主題',exact:true}).selectOption('system');
    await page.setViewportSize({width:1440,height:900});await nav('按鍵重設');
  }
}
