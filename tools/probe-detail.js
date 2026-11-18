#!/usr/bin/env node
// Probe a job site: open fresh tab -> click first card -> dump detail-panel text candidates -> close tab.
// usage: node --experimental-websocket tools/probe-detail.js <url> <cardSelector> [clickSelector]
const http = require('http');
const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;
const URL_ = process.argv[2];
const CARD = process.argv[3] || 'li.job-card-box';
const CLICK = process.argv[4] || 'a.job-name';

function req(method, path) {
  return new Promise((resolve, reject) => {
    const r = http.request({ host: '127.0.0.1', port: PORT, path, method }, (res) => {
      let b = '';
      res.on('data', (d) => (b += d));
      res.on('end', () => resolve(b));
    });
    r.on('error', reject);
    r.setTimeout(20000, () => r.destroy(new Error('http timeout')));
    r.end();
  });
}

class CDP {
  constructor(url) { this.url = url; this.id = 0; this.pending = new Map(); }
  connect() {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(this.url);
      this.ws = ws;
      const t = setTimeout(() => reject(new Error('ws timeout')), 15000);
      ws.onopen = () => { clearTimeout(t); resolve(this); };
      ws.onerror = () => reject(new Error('ws error'));
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
  send(method, params = {}, timeoutMs = 60000) {
    const id = ++this.id;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
      setTimeout(() => { if (this.pending.has(id)) { this.pending.delete(id); reject(new Error('timeout ' + method)); } }, timeoutMs);
    });
  }
  close() { try { this.ws.close(); } catch {} }
}

const EXPR = `(async () => {
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const CARD = ${JSON.stringify(CARD)};
  const CLICK = ${JSON.stringify(CLICK)};
  let cards = document.querySelectorAll(CARD);
  if (!cards.length) return { err: 'no cards', sel: CARD, url: location.href, title: document.title };
  const card = cards[0];
  card.scrollIntoView({ block: 'center' });
  const cl = CLICK ? card.querySelector(CLICK) : card;
  (cl || card).click();
  await sleep(3000);
  const out = { title: document.title, url: location.href, cards: cards.length, cands: {} };
  const sels = ['.job-detail-box', '.job-detail-section', '.job-detail', '.job-detail-panel', '.job-detail-card',
    '.job-desc', '.job-description', '[class*="detail-content"]', '[class*="job-detail"]', '[class*="desc"]'];
  for (const s of sels) {
    const el = document.querySelector(s);
    if (!el) continue;
    const t = (el.innerText || '').replace(/\\s+/g, ' ').trim();
    if (!t) continue;
    out.cands[s] = { len: t.length, head: t.slice(0, 220), cls: String(el.className).slice(0, 70) };
  }
  return out;
})()`;

(async () => {
  let created;
  try {
    created = JSON.parse(await req('PUT', '/json/new?' + encodeURIComponent(URL_)));
  } catch (e) {
    created = JSON.parse(await req('GET', '/json/new?' + encodeURIComponent(URL_)));
  }
  const c = new CDP(created.webSocketDebuggerUrl);
  await c.connect();
  try {
    await new Promise((r) => setTimeout(r, 9000));
    const r = await c.send('Runtime.evaluate', { expression: EXPR, returnByValue: true, awaitPromise: true, userGesture: true });
    if (r.exceptionDetails) throw new Error('JS: ' + ((r.exceptionDetails.exception && r.exceptionDetails.exception.description) || r.exceptionDetails.text));
    console.log(JSON.stringify(r.result && r.result.value, null, 1));
  } finally {
    c.close();
    await req('GET', '/json/close/' + created.id).catch(() => {});
  }
})().catch((e) => { console.error('ERR ' + e.message); process.exit(2); });
