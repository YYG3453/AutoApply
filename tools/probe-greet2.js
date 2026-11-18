#!/usr/bin/env node
// Probe: open a fresh tab, click first zhaopin card -> apply -> inspect greet modal -> cancel.
const http = require('http');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;
const URL_ = 'https://www.zhaopin.com/jobs/?kw=' + encodeURIComponent('实施工程师');

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
  const cards = document.querySelectorAll('.job-card');
  if (!cards.length) return { err: 'no cards', url: location.href, title: document.title };
  cards[0].scrollIntoView({ block: 'center' });
  cards[0].click();
  await sleep(2500);
  const b = document.querySelector('button.job-detail-summary__apply');
  if (!b) return { err: 'no apply btn', url: location.href };
  const btnText = b.textContent.trim();
  b.click();
  await sleep(3500);
  const d = document.querySelector('.deliver-greeting-modal');
  const out = {
    btnText,
    dlgPresent: !!d,
    dlgText: d ? d.innerText.replace(/\\s+/g, ' ').slice(0, 300) : '',
    editables: [...document.querySelectorAll('textarea, [contenteditable="true"]')]
      .map((t) => ({ tag: t.tagName, cls: String(t.className).slice(0, 50), ph: t.getAttribute('placeholder'), val: String(t.value || t.innerText || '').slice(0, 120) }))
      .slice(0, 6),
    btns: d ? [...d.querySelectorAll('button, a, div[class*="btn"]')].map((x) => ({ t: (x.textContent || '').trim().slice(0, 14), cls: String(x.className).slice(0, 50) })).slice(0, 24) : [],
  };
  if (d) {
    const c = d.querySelector('.deliver-greeting-modal__close') || d.querySelector('.deliver-greeting-modal__btn--sec');
    if (c) c.click();
    out.cancelled = !!c;
  }
  return out;
})()`;

(async () => {
  let created;
  try {
    const body = await req('PUT', '/json/new?' + encodeURIComponent(URL_));
    created = JSON.parse(body);
  } catch (e) {
    const body = await req('GET', '/json/new?' + encodeURIComponent(URL_));
    created = JSON.parse(body);
  }
  console.log('new tab:', created.id, created.url);
  const c = new CDP(created.webSocketDebuggerUrl);
  await c.connect();
  try {
    await new Promise((r) => setTimeout(r, 9000));
    const r = await c.send('Runtime.evaluate', { expression: EXPR, returnByValue: true, awaitPromise: true, userGesture: true });
    if (r.exceptionDetails) {
      const d = r.exceptionDetails;
      throw new Error('JS: ' + ((d.exception && (d.exception.description || d.exception.value)) || d.text));
    }
    console.log(JSON.stringify(r.result && r.result.value, null, 1));
  } finally {
    c.close();
    await req('GET', '/json/close/' + created.id).catch(() => {});
  }
})().catch((e) => { console.error('ERR ' + e.message); process.exit(2); });
