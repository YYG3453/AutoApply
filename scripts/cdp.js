#!/usr/bin/env node
// Minimal Chrome DevTools Protocol client (no deps; needs: node --experimental-websocket)
const http = require('http');
const fs = require('fs');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;

function httpJSON(path) {
  return new Promise((resolve, reject) => {
    const req = http.get({ host: '127.0.0.1', port: PORT, path }, (res) => {
      let b = '';
      res.on('data', (d) => (b += d));
      res.on('end', () => {
        try { resolve(JSON.parse(b)); } catch (e) { reject(new Error('bad json: ' + b.slice(0, 200))); }
      });
    });
    req.on('error', reject);
    req.setTimeout(8000, () => { req.destroy(new Error('http timeout')); });
  });
}

class CDP {
  constructor(url) {
    this.url = url;
    this.id = 0;
    this.pending = new Map();
    this.ws = null;
  }
  connect() {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(this.url);
      this.ws = ws;
      const t = setTimeout(() => reject(new Error('ws connect timeout')), 10000);
      ws.onopen = () => { clearTimeout(t); resolve(this); };
      ws.onerror = (e) => { clearTimeout(t); reject(new Error('ws error')); };
      ws.onmessage = (ev) => {
        let msg;
        try { msg = JSON.parse(ev.data); } catch { return; }
        if (msg.id && this.pending.has(msg.id)) {
          const { resolve: r, reject: j } = this.pending.get(msg.id);
          this.pending.delete(msg.id);
          if (msg.error) j(new Error(msg.error.message || JSON.stringify(msg.error)));
          else r(msg.result);
        }
      };
    });
  }
  send(method, params = {}, timeoutMs = 30000) {
    const id = ++this.id;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
      setTimeout(() => {
        if (this.pending.has(id)) { this.pending.delete(id); reject(new Error('cdp timeout: ' + method)); }
      }, timeoutMs);
    });
  }
  close() { try { this.ws.close(); } catch {} }
}

async function openPage(match) {
  const list = await httpJSON('/json/list');
  const pages = list.filter((t) => t.type === 'page');
  let t = pages.find((p) => (match ? p.url.includes(match) : true)) || pages[0];
  if (!t) throw new Error('no page target');
  const c = new CDP(t.webSocketDebuggerUrl);
  await c.connect();
  // NOTE: do not await Runtime.enable/Page.enable here - they can take 30s+ on this page.
  return { cdp: c, target: t };
}

async function evalJS(cdp, expr, awaitPromise = true) {
  const r = await cdp.send('Runtime.evaluate', {
    expression: expr,
    returnByValue: true,
    awaitPromise,
    userGesture: true,
  });
  if (r.exceptionDetails) {
    const d = r.exceptionDetails;
    throw new Error('JS error: ' + (d.exception && (d.exception.description || d.exception.value)) || d.text);
  }
  return r.result ? r.result.value : undefined;
}

async function main() {
  const [action, ...rest] = process.argv.slice(2);
  if (action === 'targets') {
    const list = await httpJSON('/json/list');
    console.log(JSON.stringify(list.map((t) => ({ type: t.type, title: t.title, url: t.url })), null, 1));
    return;
  }
  if (action === 'version') {
    console.log(JSON.stringify(await httpJSON('/json/version'), null, 1));
    return;
  }
  if (action === 'newtab' || action === 'listtabs' || action === 'closetab' || action === 'activetab') {
    const ver = await httpJSON('/json/version');
    const b = new CDP(ver.webSocketDebuggerUrl);
    await b.connect();
    try {
      if (action === 'newtab') {
        const r = await b.send('Target.createTarget', { url: rest[0] || 'about:blank' });
        console.log('created ' + r.targetId);
      } else if (action === 'closetab') {
        const r = await b.send('Target.getTargets', {});
        const hit = (r.targetInfos || []).filter((t) => t.type === 'page' && t.url.indexOf(rest[0]) >= 0);
        for (const t of hit) { await b.send('Target.closeTarget', { targetId: t.targetId }); console.log('closed ' + t.url); }
        if (!hit.length) console.log('no match');
      } else if (action === 'activetab') {
        const r = await b.send('Target.getTargets', {});
        const hit = (r.targetInfos || []).filter((t) => t.type === 'page' && t.url.indexOf(rest[0]) >= 0);
        if (!hit.length) console.log('no match');
        for (const t of hit) { await b.send('Target.activateTarget', { targetId: t.targetId }); console.log('activated ' + t.url); }
      } else {
        const r = await b.send('Target.getTargets', {});
        console.log(JSON.stringify((r.targetInfos || []).filter((t) => t.type === 'page').map((t) => ({ id: t.targetId, url: t.url, title: t.title })), null, 1));
      }
    } finally { b.close(); }
    return;
  }
  const match = process.env.CDP_MATCH || 'zhipin';
  const { cdp, target } = await openPage(match);
  try {
    if (action === 'eval') {
      const out = await evalJS(cdp, rest.join(' '));
      console.log(typeof out === 'string' ? out : JSON.stringify(out, null, 1));
    } else if (action === 'goto') {
      await cdp.send('Page.navigate', { url: rest[0] });
      await new Promise((r) => setTimeout(r, 1200));
      console.log('navigating');
    } else if (action === 'url') {
      console.log(await evalJS(cdp, 'location.href'));
    } else if (action === 'text') {
      const out = await evalJS(cdp, 'document.body ? document.body.innerText.slice(0, 4000) : ""');
      console.log(out);
    } else if (action === 'raw') {
      const params = rest[1] ? JSON.parse(rest[1]) : {};
      const out = await cdp.send(rest[0], params, 15000);
      const s = JSON.stringify(out);
      console.log(s.length > 800 ? s.slice(0, 800) + '...' : s);
    } else if (action === 'shot') {
      try { await cdp.send('Page.setWebLifecycleState', { state: 'active' }, 4000); } catch {}
      const r = await cdp.send('Page.captureScreenshot', { format: 'jpeg', quality: 80, captureBeyondViewport: false }, 15000);
      const out = rest[0] || 'shot.png';
      fs.writeFileSync(out, Buffer.from(r.data, 'base64'));
      console.log(out);
    } else if (action === 'click') {
      const out = await evalJS(cdp, rest.join(' '));
      console.log(JSON.stringify(out));
    } else if (action === 'type') {
      await cdp.send('Input.insertText', { text: rest.join(' ') });
      console.log('typed');
    } else if (action === 'key') {
      const keys = rest[0].split('+');
      const key = keys.pop();
      const mods = keys.reduce((a, k) => a | ({ alt: 1, ctrl: 2, meta: 4, shift: 8 }[k] || 0), 0);
      const codes = { Enter: [13, 'Enter'], Tab: [9, 'Tab'], Escape: [27, 'Escape'], Backspace: [8, 'Backspace'] };
      const [vk, code] = codes[key] || [key.toUpperCase().charCodeAt(0), 'Key' + key.toUpperCase()];
      await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, code, key, modifiers: mods });
      await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, code, key, modifiers: mods });
      console.log('key ' + key);
    } else if (action === 'mouse') {
      const [x, y, kind] = rest;
      const type = kind || 'click';
      await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: +x, y: +y });
      if (type === 'click') {
        await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: +x, y: +y, button: 'left', clickCount: 1 });
        await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: +x, y: +y, button: 'left', clickCount: 1 });
      }
      console.log('mouse ok');
    } else if (action === 'wheel') {
      const [x, y, dy] = rest;
      await cdp.send('Input.dispatchMouseEvent', { type: 'mouseWheel', x: +x, y: +y, deltaX: 0, deltaY: +dy });
      console.log('wheel ok');
    } else if (action === 'wait') {
      await new Promise((r) => setTimeout(r, Number(rest[0] || 1000)));
      console.log('waited');
    } else {
      throw new Error('unknown action: ' + action);
    }
  } finally {
    cdp.close();
  }
}

main().catch((e) => { console.error('ERR ' + e.message); process.exit(2); });
