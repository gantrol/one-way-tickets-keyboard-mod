import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Export the same deterministic timeline shown in preview.html.
// Requires Chrome (or CHROME_PATH), Node 22+, and ffmpeg on PATH.
const folder = fileURLToPath(new URL('./', import.meta.url));
const stills = process.argv.find(a=>a.startsWith('--stills='))?.slice(9) || folder;
const profile = mkdtempSync(join(tmpdir(), 'keyboard-demo-'));
const chrome = spawn(process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
  '--headless=new','--remote-debugging-port=0',`--user-data-dir=${profile}`,
  '--no-first-run','--no-default-browser-check','--hide-scrollbars','--allow-file-access-from-files','about:blank',
], { windowsHide:true, stdio:['ignore','ignore','pipe'] });
let socket, encoder;
try {
  const port = await new Promise((done,fail)=>{
    let output='';const timeout=setTimeout(()=>fail(new Error('Chrome startup timed out')),20000);
    chrome.once('error',e=>{clearTimeout(timeout);fail(e)});
    chrome.stderr.on('data',chunk=>{output+=chunk;const m=output.match(/DevTools listening on ws:\/\/[^:]+:(\d+)\//);if(m){clearTimeout(timeout);done(Number(m[1]))}});
  });
  const target=await(await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
  socket=new WebSocket(target.webSocketDebuggerUrl);
  let next=0;const pending=new Map(),errors=[];
  socket.onmessage=message=>{const r=JSON.parse(message.data);if(r.id&&pending.has(r.id)){pending.get(r.id)(r);pending.delete(r.id)}else if(r.method==='Runtime.exceptionThrown')errors.push(r.params.exceptionDetails.exception?.description||r.params.exceptionDetails.text)};
  await new Promise((done,fail)=>{socket.onopen=done;socket.onerror=fail});
  const send=async(method,params={})=>{
    const result=await new Promise((done,fail)=>{const id=++next;const timeout=setTimeout(()=>{pending.delete(id);fail(new Error(`Timed out: ${method}`))},20000);pending.set(id,r=>{clearTimeout(timeout);done(r)});socket.send(JSON.stringify({id,method,params}))});
    if(result.error)throw new Error(JSON.stringify(result.error));return result.result;
  };
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result?.value};
  await send('Runtime.enable');await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride',{width:1280,height:720,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:pathToFileURL(join(folder,'preview.html')).href});
  for(let i=0;i<100;i++){if(await evaluate('typeof window.__setTime === "function"'))break;await new Promise(r=>setTimeout(r,50))}
  await evaluate('document.fonts.ready.then(()=>true)');
  await evaluate('Promise.all([...document.images].map(img=>img.decode())).then(()=>true)');
  const capture=async time=>{await evaluate(`window.__setTime(${time})`);const r=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false,clip:{x:0,y:0,width:1280,height:720,scale:1}});return Buffer.from(r.data,'base64')};
  const samples=[.8,4,9,14,19,23.5,26.5];
  for(let i=0;i<samples.length;i++)writeFileSync(join(stills,`demo-shot-${String(i).padStart(2,'0')}.png`),await capture(samples[i]));
  console.log(`Captured ${samples.length} storyboard stills.`);
  // Catch unwanted scroll, missing artwork and text overruns before exporting.
  const validation=await evaluate(`(()=>({keys:document.querySelectorAll('[id*="-key-"]').length,overflow:document.documentElement.scrollWidth>innerWidth+1,duration:window.__demo.duration,scenes:window.__demo.scenes.length}))()`);
  if(validation.keys!==222||validation.overflow||validation.duration!==28||validation.scenes!==7)throw new Error('Preview validation failed: '+JSON.stringify(validation));
  console.log('Preview validation: '+JSON.stringify(validation));
  if(!process.argv.includes('--stills-only')){
    const output=join(folder,'keyboard-demo-preview.mp4');
    encoder=spawn('ffmpeg',['-hide_banner','-loglevel','error','-y','-f','image2pipe','-vcodec','png','-framerate','30','-i','pipe:0','-an','-c:v','libx264','-preset','fast','-crf','20','-pix_fmt','yuv420p','-movflags','+faststart',output],{windowsHide:true,stdio:['pipe','ignore','pipe']});
    let encoderErrors='';encoder.stderr.on('data',s=>encoderErrors+=s);
    const finished=new Promise((done,fail)=>{encoder.once('error',fail);encoder.once('exit',code=>code===0?done():fail(new Error('ffmpeg failed: '+encoderErrors)))});
    encoder.stdin.on('error',()=>{});
    for(let frame=0;frame<840;frame++){
      const png=await capture(frame/30);
      if(!encoder.stdin.write(png))await once(encoder.stdin,'drain');
      if(frame%150===0)console.log(`Exported ${frame}/840 frames`);
    }
    encoder.stdin.end();await finished;
    if(errors.length)throw new Error(errors.join('\n'));
    console.log('Exported '+output);
  }
} finally {
  socket?.close();encoder?.stdin?.end();if(encoder&&encoder.exitCode===null)encoder.kill();
  const stopped=new Promise(r=>{if(chrome.exitCode!==null)r();else chrome.once('exit',r)});chrome.kill();await stopped;
  // Only the exact temporary profile created by this process may be removed.
  const safeRoot=resolve(tmpdir())+sep+'keyboard-demo-';
  if(resolve(profile).startsWith(safeRoot))try{rmSync(profile,{recursive:true,force:true})}catch{}
}
