#!/usr/bin/env node
// 用法: node --experimental-websocket evalfile.js <js文件>   (CDP_MATCH 环境变量选页面)
// 把文件里的表达式整段 eval 到页面里，打印 JSON 结果。避免命令行引号转义地狱。
const http = require('http');
const fs = require('fs');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;

function httpJSON(p) {
  return new Promise((resolve, reject) => {
    const req = http.get({ host: '127.0.0.1', port: PORT, path: p }, (res) => {
      let b = '';
      res.on('data', (d) => (b += d));
      res.on('end', () => { try { resolve(JSON.parse(b)); } catch (e) { reject(new Error('bad json')); } });
    });
    req.on('error', reject);
    req.setTimeout(8000, () => req.destroy(new Error('http timeout')));
  });
}

class CDP {
  constructor(url) { this.url = url; this.id = 0; this.pending = new Map(); }
  connect() {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(this.url);
      this.ws = ws;
      const t = setTimeout(() => reject(new Error('ws connect timeout')), 10000);
      ws.onopen = () => { clearTimeout(t); resolve(this); };
      ws.onerror = () => { clearTimeout(t); reject(new Error('ws error')); };
      ws.onmessage = (ev) => {
        let m; try { m = JSON.parse(ev.data); } catch { return; }
        if (m.id && this.pending.has(m.id)) {
          const { resolve: r, reject: j } = this.pending.get(m.id);
          this.pending.delete(m.id);
          if (m.error) j(new Error(m.error.message || 'cdp error')); else r(m.result);
        }
      };
    });
  }
  send(method, params = {}, timeoutMs = 20000) {
    const id = ++this.id;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
      setTimeout(() => { if (this.pending.has(id)) { this.pending.delete(id); reject(new Error('timeout ' + method)); } }, timeoutMs);
    });
  }
  close() { try { this.ws.close(); } catch {} }
}

(async () => {
  const file = process.argv[2];
  if (!file) throw new Error('usage: evalfile.js <file>');
  const expr = fs.readFileSync(file, 'utf8');
  const list = await httpJSON('/json/list');
  const pages = list.filter((t) => t.type === 'page');
  const match = process.env.CDP_MATCH || 'zhipin';
  const t = pages.find((p) => p.url.includes(match));
  if (!t) throw new Error('no page matching ' + match + '; tabs=' + pages.map((p) => p.url).join(' | '));
  const c = new CDP(t.webSocketDebuggerUrl);
  await c.connect();
  try {
    const r = await c.send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true, userGesture: true });
    if (r.exceptionDetails) {
      const d = r.exceptionDetails;
      throw new Error('JS: ' + ((d.exception && (d.exception.description || d.exception.value)) || d.text));
    }
    const v = r.result ? r.result.value : undefined;
    console.log(typeof v === 'string' ? v : JSON.stringify(v, null, 1));
  } finally { c.close(); }
})().catch((e) => { console.error('ERR ' + e.message); process.exit(2); });
