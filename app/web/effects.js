// 燈效示意圖的軟體模擬（由 src/LightEffects.cs 移植）。模式索引同鍵盤 payload[10]；實際效果以鍵盤韌體為準。
const Effects=(()=>{
  const Width=789,Height=313,KeyCount=87;
  const Names=["關閉主背光","恆亮","呼吸","夢幻彩虹","一觸即發","雨中漫步","彩虹輪盤","漣漪擴散","繁星閃爍","踏雪無痕","流動貪食蛇","霓虹流動","如影隨形","正弦光波","左右掃描","旋轉風車","七彩瀑布","花開富貴","自訂燈效"];
  const Hints=["主背光全部熄滅，鍵盤輸入不受影響。","所有按鍵維持固定亮度。","整體亮度緩慢漸強、漸弱。","彩虹色彩沿鍵盤平緩流動。","按下的按鍵瞬間亮起，隨即淡出。","光點如雨滴沿各列落下。","色相繞鍵盤中心旋轉。","光圈由隨機位置向外擴散。","按鍵隨機明滅，如同星空。","按過的按鍵緩緩淡出，留下殘影。","光帶沿鍵盤逐列蛇行。","彩色條紋橫向流動。","按鍵亮起後，光向周邊按鍵擴散。","光波沿正弦曲線橫向行進。","光條由左至右來回掃過。","彩色扇葉繞中心旋轉。","色彩由上往下流瀉。","光環自中心綻放並擴大。","使用逐鍵自訂配色。"];
  const hasBrightness=m=>m>=1&&m<=17, hasSpeed=m=>m>=2&&m<=17, hasColor=m=>m===1||m===2||(m>=4&&m<=14);
  const PaletteNames=["紅","綠","深藍","黃","紫","淺藍","白"];
  const Palette=[[255,64,64],[70,230,110],[40,80,255],[255,220,0],[185,70,255],[80,210,255],[255,255,255]];
  const SpeedScale=[0,.35,.6,.85,1.2];
  const Flower=[9,9,8,7,6,5,4,3,2,2,3,5,5,7,8,9,9,8,7,6,5,4,3,2,1,1,2,3,5,6,7,8,9,8,7,6,5,4,3,2,1,0,1,2,3,5,6,7,8,9,8,7,6,5,4,3,2,1,1,2,3,5,9,8,7,6,5,4,3,2,2,2,3,5,7,9,9,8,5,5,5,5,6,7,8,8,6];
  const Stream=[0,2,3,4,5,7,8,9,10,11,11,12,12,13,14,0,1,2,3,4,5,6,7,8,9,10,11,11,12,13,14,15,0,1,2,3,4,5,6,7,8,9,10,11,11,12,13,14,15,0,1,2,3,4,5,6,7,8,9,10,11,12,0,1,2,3,4,5,6,7,8,9,10,12,14,0,0,1,5,9,10,11,12,13,14,15,12];
  const Wheel=[7,8,8,9,9,10,10,11,11,12,12,13,13,14,15,7,7,8,8,9,9,10,10,11,11,11,12,12,13,14,15,16,7,8,8,9,9,10,10,10,11,11,11,12,13,13,14,15,16,6,6,5,5,4,4,3,3,2,2,1,1,1,6,6,5,5,4,3,3,2,2,1,1,0,18,6,6,5,3,1,0,0,0,19,18,17,13];
  const StreamCount=20,WheelCount=20;
  const clamp=(v,a,b)=>Math.min(b,Math.max(a,v)), frac=v=>v-Math.floor(v);
  const hash=n=>{let x=Math.imul(n>>>0,2654435761)>>>0;x=(x^(x>>>15))>>>0;x=Math.imul(x,2246822519)>>>0;x=(x^(x>>>13))>>>0;return (x&0xFFFFFF)/0x1000000};
  const row=y=>y<80?0:y<111?1:y<152?2:y<192?3:y<233?4:5;
  const pathM=(x,y)=>{const r=row(y);return r*800+(r%2===0?x:789-x)};
  const smooth=(d,w)=>Math.exp(-d*d/(w*w));
  const rgb=(i,r,g,b)=>[r,g,b,clamp(i,0,1)];
  function hsv(h,i){
    h-=Math.floor(h);const s=.9,v=1,k=h*6,sector=Math.floor(k),f=k-sector,p=v*(1-s),q=v*(1-s*f),t=v*(1-s*(1-f));
    const [r,g,b]=[[v,t,p],[q,v,p],[p,v,t],[p,q,v],[t,p,v],[v,p,q]][sector%6];
    return [r*255,g*255,b*255,clamp(i,0,1)];
  }
  function pulse(t,ordinal,rate,decay){
    let best=0;const n=Math.floor(t*rate);
    for(let k=n;k>n-12&&k>=0;k--)if(Math.floor(hash(k*7+3)*KeyCount)===ordinal)best=Math.max(best,Math.exp(-(t-k/rate)/decay));
    return best;
  }
  const pingPong=v=>{v-=Math.floor(v/2)*2;return v>1?2-v:v};
  // 回傳 [r,g,b,強度 0..1]
  function sample(mode,t,x,y,ordinal){
    const dx=x-Width/2,dy=y-165,angle=Math.atan2(dy,dx);
    switch(mode){
      case 1:return rgb(1,255,226,170);
      case 2:return rgb(.12+.88*(.5+.5*Math.sin(t*2.2-Math.PI/2)),255,110,200);
      case 3:return hsv(t*.15,1);
      case 4:{let best=0,hue=0;const rate=2,n=Math.floor(t*rate),rows=[53,106,147,187,228,269];
        for(let k=n;k>n-5&&k>=0;k--){const c=Math.floor(hash(k*7+3)*KeyCount),cx=40+hash(c+11)*700,ry=rows[Math.floor(hash(c+23)*6)],age=t-k/rate,d=Math.abs(x-cx);
          const v=Math.abs(y-ry)<20?Math.max(smooth(d-age*520,50)*Math.exp(-age/.9),d<14?Math.exp(-age/.5):0):0;if(v>best){best=v;hue=hash(k*3)}}
        return hsv(hue,best)}
      case 5:{const zone=Math.floor(x/113),period=2.4+hash(zone+7)*1.6,tt=t+hash(zone)*period,drop=Math.floor(tt/period),head=(tt/period-drop)*9-1.5,d=head-row(y);
        return hsv(hash(drop*7+zone),d>=0&&d<2.5?1-d/2.5:0)}
      case 6:{const a=frac(angle/(2*Math.PI));return hsv(Math.floor(a*6)/6+t*.12,1)}
      case 7:{const period=2.2,k=Math.floor(t/period),age=t/period-k,c=Math.floor(hash(k)*KeyCount),cx=40+hash(c+11)*700,cy=50+hash(c+23)*220,d=Math.hypot(x-cx,y-cy);
        return hsv(hash(k+5),smooth(d-age*950,90)*(1-age*.6))}
      case 8:{const phase=hash(ordinal+400),s=.5+.5*Math.sin(2*Math.PI*(t*(.18+.3*hash(ordinal+900))+phase));return hsv(hash(ordinal+77),.25+.75*s)}
      case 9:return hsv(hash(ordinal*13+5)+t*.02,.6+.4*(.5+.5*Math.sin(t*1.3+ordinal*1.7)));
      case 10:{const M=4789,ph=t*.14%2,head=(ph<1?ph:2-ph)*M,m=pathM(x,y),delta=ph<1?head-m:m-head;return rgb(delta>=0&&delta<320?1-delta/320:0,90,255,140)}
      case 11:{const m=pathM(x,y)/4789;return hsv(m*2-t*.25,.3+.7*(.5+.5*Math.sin(m*Math.PI*6-t*3)))}
      case 12:return hsv(hash(ordinal+5),pulse(t,ordinal,4,.5));
      case 13:{const xc=295,w=x<xc?Math.sin(t*3+x/70):Math.sin(t*3+(x-xc)/70+Math.PI);return hsv(x/Width*.6+.1,.3+.7*smooth(y-165-62*w,30))}
      case 14:{const bar=pingPong(t*.55)*(StreamCount-1),g=Stream[ordinal];return hsv(bar/StreamCount*.5+.45,smooth(g-bar,1.6))}
      case 15:{const n=WheelCount,g=Wheel[ordinal];let best=0;for(let arm=0;arm<2;arm++){const d=(((t*2.5+arm*n/2-g)%n)+n)%n;best=Math.max(best,d<9?1-d/9:0)}return hsv(g/n+t*.03,.45+.55*best)}
      case 16:return hsv(y/Height*.7-t*.3,1);
      case 17:{const d=Math.hypot(x-418,y-187),r=(t*150)%560;return hsv(r/560,smooth(d-r,55))}
      case 18:return hsv(Math.floor(x/Width*7)/7,1);
      default:return [0,0,0,0];
    }
  }
  // 套用色票：colorIndex 0..6 固定色，7 混色
  function sampleTinted(mode,t,x,y,ordinal,colorIndex){
    const c=sample(mode,t,x,y,ordinal);
    if(!hasColor(mode)||mode===9||c[3]===0)return c;
    if(colorIndex>=0&&colorIndex<7){const p=Palette[colorIndex];return [p[0],p[1],p[2],c[3]]}
    return mode===1||mode===2?hsv(t*.12,c[3]):c;
  }
  return {Width,Height,Names,Hints,hasBrightness,hasSpeed,hasColor,PaletteNames,Palette,SpeedScale,sampleTinted};
})();
