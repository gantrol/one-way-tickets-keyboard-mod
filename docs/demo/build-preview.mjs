import { readFileSync, writeFileSync } from 'node:fs';

// Reuse the three editable keyboard drawings; no external runtime or packages.
const modes = ['base', 'shift', 'alt'];
const assets = modes.map(mode => {
  const file = mode === 'base' ? 'keyboard-layout-caps-lock.svg' : `keyboard-layout-${mode}.svg`;
  const svg = readFileSync(new URL(`../${file}`, import.meta.url), 'utf8');
  const defs = svg.match(/<defs>([\s\S]*?)<\/defs>/)[1];
  const start = svg.indexOf('<g filter="url(#caseShadow)">');
  const end = svg.indexOf('<path class="annotation-line"');
  if (start < 0 || end < start) throw new Error(`Missing keyboard artwork: ${file}`);
  const prefix = content => content.replace(/id="([^"]+)"/g, `id="${mode}-$1"`).replace(/url\(#([^)]*)\)/g, `url(#${mode}-$1)`);
  const keys = [...svg.matchAll(/<g id="(key-[^"]+)" aria-label="([^"]+)" transform="translate\(([\d.]+) ([\d.]+)\)">/g)].map(m => {
    const size = svg.slice(m.index, m.index + 1000).match(/<rect x="0" y="0" width="([\d.]+)" height="([\d.]+)"/);
    return { id: `${mode}-${m[1]}`, label: m[2].split('：')[0], x: +m[3], y: +m[4], w: +size[1], h: +size[2] };
  });
  if (keys.length !== 74) throw new Error(`Expected 74 keys: ${file}`);
  return { mode, keys, svg: `<svg class="keyboard-art" id="art-${mode}" xmlns="http://www.w3.org/2000/svg" viewBox="0 210 1600 690" role="img" aria-label="${mode} 键盘"><defs>${prefix(defs)}</defs><g font-family="'Microsoft YaHei UI','Microsoft YaHei',Arial,sans-serif">${prefix(svg.slice(start,end))}</g><g id="focus-${mode}" pointer-events="none"></g></svg>` };
});

const scenes = [
  { start:0, end:2, mode:'base', tag:'双手就位', title:['少点鼠标，','多用键盘。'], desc:'《再再启程》键盘 Mod', keys:[], color:'#487d6a', subtitle:'《再再启程》键盘 Mod' },
  { start:2, end:7, mode:'base', tag:'01 / 全武器托管', title:['按一下，','武器交给托管。'], desc:'自动开火与装填', keys:['Caps Lock'], color:'#52788e', subtitle:'Caps Lock · 全武器托管' },
  { start:7, end:12, mode:'base', tag:'02 / 紧急抢修', title:['按住空格，','工具自动到位。'], desc:'处理眼前故障，减少频繁点击', keys:['Space'], color:'#487d6a', subtitle:'按住 Space · 自动拎工具抢修' },
  { start:12, end:17, mode:'shift', tag:'03 / 武器副操作 2', title:['先按住 Shift，','再按住 A。'], desc:'以 1 号槽加特林为例：持续装填', keys:['Shift','A'], color:'#997936', subtitle:'Shift + A · 持续装填', note:'具体动作随槽位内的武器变化。' },
  { start:17, end:22, mode:'alt', tag:'04 / 武器副操作 3', title:['先按住 Alt，','再按住 A。'], desc:'同一把加特林：持续搬运弹药', keys:['Alt','A'], color:'#997936', subtitle:'Alt + A · 持续搬弹', note:'具体动作随槽位内的武器变化。' },
  { start:22, end:25, mode:'alt', tag:'05 / 人物轮换', title:['按住 Alt，','点按 Y。'], desc:'1 号角色与替补轮换', keys:['Alt','Y'], color:'#647e57', subtitle:'Alt + Y · 与替补轮换' },
  { start:25, end:28, mode:'base', tag:'现在开始', title:['三张键位图，','随时查阅。'], desc:'安装方法与键位说明见 README', keys:[], color:'#487d6a', subtitle:'键位与安装方法见 README' },
];

const html = `<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>键盘 Mod · 28 秒分镜预览</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#eeeee7;color:#293c31;font-family:'Microsoft YaHei UI','Microsoft YaHei',Arial,sans-serif}.page{max-width:1280px;margin:0 auto}.screen{width:100%;aspect-ratio:16/9;position:relative;overflow:hidden;background:#fcfaf6}#stage{width:1280px;height:720px;position:absolute;inset:0;transform-origin:top left;overflow:hidden;background:radial-gradient(ellipse at 76% 58%,#e7eddf 0%,#f4f4ec 36%,#fcfaf6 78%)}.brand{position:absolute;left:56px;top:37px;font-size:16px;letter-spacing:2px;font-weight:650;color:#55684f}.draft{position:absolute;right:56px;top:28px;padding:11px 17px;border:1px solid #d9dfd1;border-radius:24px;background:#f8faf3;color:#627259;font-size:15px}.rule{position:absolute;top:79px;left:56px;right:56px;height:1px;background:#dce2d3}.copy{position:absolute;left:56px;top:139px;width:420px}.tag{font-size:17px;font-weight:650;letter-spacing:1.8px;margin-bottom:23px}.headline{font-size:46px;line-height:1.3;letter-spacing:-.8px;font-weight:800;margin:0}.description{font-size:20px;line-height:1.8;margin-top:23px;color:#5c6c5b;max-width:380px}.chord{display:flex;align-items:center;gap:12px;margin-top:28px;min-height:62px}.keychip{min-width:63px;padding:14px 18px;border-radius:12px;background:#fdfcf7;border:1.4px solid #bac6b3;box-shadow:0 5px 0 #d4ddcd;color:#3e5944;font-size:24px;font-weight:750;text-align:center}.plus{font-size:25px;color:#8b9983}.example{margin-top:22px;max-width:390px;font-size:15px;line-height:1.8;color:#6d7865}.visual{position:absolute;left:454px;top:219px;width:790px;height:340px;transform-origin:60% 50%}.keyboard-art{position:absolute;inset:0;width:100%;height:100%;overflow:visible;display:none;filter:drop-shadow(0 18px 20px #64795613)}.keyboard-art.active{display:block}.visual-tag{position:absolute;top:182px;right:62px;font-size:12px;letter-spacing:2px;color:#89957f}.caption{position:absolute;left:56px;right:56px;top:591px;border-top:1px solid #dce2d3;padding-top:20px;font-size:23px;font-weight:650;text-align:center;color:#3a5441}.footer{position:absolute;left:56px;right:56px;bottom:28px;display:flex;align-items:center;gap:18px}.step-label{font-size:12px;letter-spacing:2px;color:#788570;width:56px}.timeline{display:flex;gap:5px;height:4px;flex:1}.timeline span{position:relative;background:#dbe2d4;border-radius:4px;overflow:hidden}.timeline i{display:block;height:100%;background:#647e57;transform-origin:left;transform:scaleX(0)}.time{font-size:13px;font-variant-numeric:tabular-nums;color:#748169;width:91px;text-align:right}.endcards{position:absolute;left:52px;top:466px;display:none;gap:9px}.endcard{padding:9px 11px;border-radius:9px;border:1px solid #d2dcc8;background:#f8faf4;font-size:14px;color:#53684c}.toolbar{display:flex;align-items:center;gap:16px;padding:18px 24px;background:#f8f9f3;border-top:1px solid #d9dfd0}.toolbar button{border:1px solid #c5d0bd;border-radius:9px;background:#fffdf8;padding:9px 16px;color:#37533e;font:inherit;cursor:pointer}.toolbar input{flex:1;accent-color:#647e57;min-width:60px}.toolbar output{font-size:13px;color:#65745d;font-variant-numeric:tabular-nums}.help{padding:0 24px 22px;background:#f8f9f3;color:#6e7a65;font-size:13px;line-height:1.7}.help a{color:#52788e}button:focus-visible,input:focus-visible{outline:2px solid #52788e;outline-offset:3px}@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto}}
.endcards{top:450px}.endcard{margin:0;padding:6px;background:#f8faf4}.endcard img{display:block;width:108px;height:81px;object-fit:contain;border-radius:4px}.endcard figcaption{padding-top:5px;text-align:center;font-size:12px}
</style></head><body><main class="page"><div class="screen"><div id="stage">
<div class="brand">《再再启程》 / KEYBOARD MOD</div><div class="draft">分镜预览 · 非游戏实录</div><div class="rule"></div>
<section class="copy"><div class="tag" id="tag"></div><h1 class="headline" id="headline"></h1><div class="description" id="description"></div><div class="chord" id="chord"></div><div class="example" id="example"></div></section>
<div class="visual-tag">KEYBOARD / 按键示意</div><div class="visual" id="visual">${assets.map(a=>a.svg).join('')}</div>
<div class="endcards" id="endcards"><figure class="endcard"><img src="../keyboard-layout-caps-lock.svg" alt="常规键位图"><figcaption>常规键位</figcaption></figure><figure class="endcard"><img src="../keyboard-layout-shift.svg" alt="Shift 键位图"><figcaption>按住 Shift</figcaption></figure><figure class="endcard"><img src="../keyboard-layout-alt.svg" alt="Alt 键位图"><figcaption>按住 Alt</figcaption></figure></div>
<div class="caption" id="caption"></div><div class="footer"><span class="step-label" id="step"></span><div class="timeline" id="timeline"></div><span class="time" id="time"></span></div>
</div></div><div class="toolbar"><button id="play" type="button" aria-label="播放分镜">播放</button><button id="restart" type="button">重播</button><input id="seek" aria-label="播放进度（秒）" type="range" min="0" max="28" step="0.01" value="0"><output id="clock">00:00 / 00:28</output></div><div class="help">28 秒 · 无配音 · 当前仅展示按键与剪辑节奏，游戏镜头待补录。<a href="STORYBOARD.md">查看分镜与录屏清单</a> · <a href="keyboard-demo-preview.mp4">MP4 预览</a>。空格播放 / 暂停，方向键快进 / 后退。</div></main>
<script>
const scenes=${JSON.stringify(scenes)}, assets=${JSON.stringify(assets.map(({mode,keys})=>({mode,keys})))};
const $=id=>document.getElementById(id), TOTAL=28;
let time=0,playing=false,previous=0,current=-1;
const timeline=scenes.map((s,i)=>{const bar=document.createElement('span');bar.style.flex=s.end-s.start;bar.title=s.tag;const fill=document.createElement('i');bar.append(fill);$('timeline').append(bar);return fill;});
const fmt=t=>String(Math.floor(t/60)).padStart(2,'0')+':'+String(Math.floor(t%60)).padStart(2,'0');
function pressAmount(label,s,local){if(!s.keys.includes(label))return 0;if(label===s.keys[0]&&s.keys.length>1)return local>.25?1:0;if(label==='Caps Lock')return Math.max(0,1-Math.abs(local-.55)/.2);if(s.tag.includes('人物'))return Math.max(0,1-Math.abs(local-.9)/.22);return local>.55?1:0;}
function render(t){
 time=Math.min(TOTAL,Math.max(0,t));
 let index=scenes.findIndex(s=>time<s.end);if(index<0)index=scenes.length-1;
 const s=scenes[index],local=time-s.start,p=Math.max(0,Math.min(1,local/(s.end-s.start)));
 if(index!==current){current=index;$('tag').textContent=s.tag;$('tag').style.color=s.color;$('headline').replaceChildren(...s.title.map((line,i)=>{const span=document.createElement('span');span.textContent=line;span.style.display='block';return span;}));$('description').textContent=s.desc;$('caption').textContent=s.subtitle;$('example').textContent=s.note||'';$('chord').innerHTML=s.keys.map((k,i)=>(i?'<span class="plus">+</span>':'')+'<span class="keychip">'+k+'</span>').join('');$('endcards').style.display=index===6?'flex':'none';for(const a of assets)$('art-'+a.mode).classList.toggle('active',a.mode===s.mode);}
 const enter=Math.min(1,local/.32),ease=1-(1-enter)**3;
 document.querySelector('.copy').style.opacity=ease;document.querySelector('.copy').style.transform='translateY('+((1-ease)*10)+'px)';
 $('visual').style.transform='translateY('+((1-ease)*5)+'px) scale('+(1+p*.018)+')';
 const asset=assets.find(a=>a.mode===s.mode), focus=$('focus-'+s.mode);let rings='';
 for(const key of asset.keys){const el=$(key.id),main=s.keys.includes(key.label)&&!(key.label===s.keys[0]&&key.x>1000);el.style.opacity=s.keys.length?(main?1:.45):1;const amount=main?pressAmount(key.label,s,local):0;el.setAttribute('transform','translate('+key.x+' '+(key.y+amount*3)+')');if(main){const pulse=.65+.25*Math.sin(local*2.8);rings+='<rect x="'+(key.x-3)+'" y="'+(key.y-3+amount*3)+'" width="'+(key.w+6)+'" height="'+(key.h+6)+'" rx="17" fill="none" stroke="'+s.color+'" stroke-opacity="'+pulse+'" stroke-width="4.5"/>';}}
 focus.innerHTML=rings;for(let i=0;i<timeline.length;i++){const scene=scenes[i];timeline[i].style.transform='scaleX('+Math.max(0,Math.min(1,(time-scene.start)/(scene.end-scene.start)))+')';} $('step').textContent=String(index+1).padStart(2,'0')+' / 07';$('time').textContent=fmt(time)+' / 00:28';$('clock').textContent=fmt(time)+' / 00:28';$('seek').value=time;
 return {time,scene:index+1,mode:s.mode,caption:s.subtitle};
}
window.__setTime=t=>{playing=false;$('play').textContent='播放';return render(t)};
window.__demo={duration:TOTAL,scenes};
function toggle(){if(time>=TOTAL)render(0);playing=!playing;$('play').textContent=playing?'暂停':'播放';previous=performance.now()}
$('play').onclick=toggle;$('restart').onclick=()=>{render(0);playing=true;previous=performance.now();$('play').textContent='暂停'};$('seek').oninput=e=>window.__setTime(+e.target.value);
window.addEventListener('keydown',e=>{if(e.target.matches('input,button'))return;if(e.code==='Space'){e.preventDefault();toggle()}else if(e.code==='ArrowRight'||e.code==='ArrowLeft'){e.preventDefault();window.__setTime(time+(e.code==='ArrowRight'?1:-1))}});
function tick(now){if(playing){const dt=Math.min(.1,(now-previous)/1000);render(time+dt);if(time>=TOTAL){playing=false;$('play').textContent='重播';}}previous=now;requestAnimationFrame(tick)}
function fit(){$('stage').style.transform='scale('+(document.querySelector('.screen').clientWidth/1280)+')'}new ResizeObserver(fit).observe(document.querySelector('.screen'));fit();
const requested=Number(new URLSearchParams(location.search).get('t')||0);render(requested);requestAnimationFrame(tick);
</script></body></html>`;

writeFileSync(new URL('./preview.html', import.meta.url), html);
console.log('Built docs/demo/preview.html (28-second deterministic storyboard).');
