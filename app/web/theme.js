// Theme preference is local to this UI; it never changes Windows or keyboard settings.
const Theme=(()=>{
  const media=matchMedia('(prefers-color-scheme: dark)');
  let choice='system';
  try{const saved=localStorage.getItem('theme');if(['system','light','dark'].includes(saved))choice=saved}catch{}
  function apply(){
    document.documentElement.dataset.theme=choice==='system'?(media.matches?'dark':'light'):choice;
    dispatchEvent(new Event('themechange'));
  }
  media.addEventListener('change',apply);apply();
  return {get:()=>choice,set:value=>{
    if(!['system','light','dark'].includes(value))return;
    choice=value;try{localStorage.setItem('theme',value)}catch{}apply();
  }};
})();
