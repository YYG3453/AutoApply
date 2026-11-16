#!/usr/bin/env node
// 前程无忧(we.51job.com) 自动投递：关键词搜索 → 按卡片 jobArea 过滤 19 城 → 逐卡「投递」→ 关“投递成功”弹窗
// usage: node q51job.js --minutes 30 [--keywords 实施工程师] [--max 120] [--pace 9] [--dry]
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;
const ROOT = __dirname;
const STATE_FILE = path.join(ROOT, 'state-q51.jsonl');
const LOG_FILE = path.join(ROOT, 'run-q51.log');

function log(...a) {
  const line = '[' + new Date().toLocaleString('zh-CN', { hour12: false }) + '] ' + a.join(' ');
  console.log(line);
  try { fs.appendFileSync(LOG_FILE, line + '\n'); } catch {}
}

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
  send(method, params = {}, timeoutMs = 25000) {
    const id = ++this.id;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
      setTimeout(() => { if (this.pending.has(id)) { this.pending.delete(id); reject(new Error('timeout ' + method)); } }, timeoutMs);
    });
  }
  close() { try { this.ws.close(); } catch {} }
}

async function openPage() {
  const list = await httpJSON('/json/list');
  const pages = list.filter((t) => t.type === 'page');
  const t = pages.find((p) => /51job/.test(p.url));
  if (!t) throw new Error('没有 51job 标签页，请先在该 Chrome 打开 we.51job.com');
  const c = new CDP(t.webSocketDebuggerUrl);
  await c.connect();
  return c;
}

async function ev(c, expr) {
  const r = await c.send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true, userGesture: true });
  if (r.exceptionDetails) throw new Error('JS: ' + ((r.exceptionDetails.exception && r.exceptionDetails.exception.description) || r.exceptionDetails.text));
  return r.result ? r.result.value : undefined;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function arg(name, def) {
  const i = process.argv.indexOf('--' + name);
  if (i < 0) return def;
  const v = process.argv[i + 1];
  return v && !v.startsWith('--') ? v : true;
}

const BOSS_CITIES = JSON.parse(fs.readFileSync(path.join(ROOT, 'cities.json'), 'utf8'));
const CITY_NAMES = BOSS_CITIES.map((x) => x.name);

const SCAN = `(() => {
  const out = [];
  document.querySelectorAll('.joblist-item').forEach((card, i) => {
    let m = {};
    const holder = card.querySelector('[sensorsdata]') || card;
    try { m = JSON.parse((holder.getAttribute('sensorsdata') || '{}').replace(/&quot;/g, '"')); } catch (e) {}
    const txt = (card.innerText || '').replace(/\\s+/g, ' ');
    const b = card.querySelector('button.btn.apply');
    let area = m.jobArea || '';
    if (!area) { const am = txt.match(/([\\u4e00-\\u9fa5]{2,6})[·\\-]([\\u4e00-\\u9fa5]{2,14})/); if (am) area = am[0]; }
    out.push({
      i,
      id: m.jobId || '',
      title: m.jobTitle || (txt.split(' ')[0] || ''),
      area,
      salary: m.jobSalary || '',
      btn: b ? b.textContent.trim() : '',
    });
  });
  const body = document.body.innerText.replace(/\\s+/g, ' ');
  return { jobs: out, toast: (body.match(/(?:安全验证|请完成验证|验证码|投递上限|操作过于频繁|操作频繁|已达上限|今日已投)[^ ]{0,30}/) || [''])[0] };
})()`;

const CLOSE_DLG = `(() => {
  const ds = [...document.querySelectorAll('.el-dialog, [class*=dialog]')].filter((d) => d.offsetParent !== null && /投递成功|扫一扫/.test(d.innerText || ''));
  ds.forEach((d) => { const x = d.querySelector('.el-dialog__headerbtn, .close, [class*=close]'); if (x) x.click(); });
  const ov = document.querySelector('.el-dialog__wrapper .el-overlay, .el-overlay');
  return ds.length;
})()`;

// 在页面里挂一个 XHR 钩子：把 apply 接口的返回原文存到 window.__q51last，
// 这样投递成功/触碰每日上限都能按接口结果判断，而不是靠弹窗文本猜。
const INSTALL_HOOK = `(() => {
  if (window.__q51hooked) return 'already';
  window.__q51hooked = true;
  window.__q51last = null;
  const X = XMLHttpRequest.prototype;
  const ox = X.open, os = X.send;
  X.open = function (m, u) { this.__u = u; return ox.apply(this, arguments); };
  X.send = function (b) {
    try {
      this.addEventListener('load', function () {
        if (/apply/i.test(this.__u || '')) {
          window.__q51last = { u: String(this.__u).replace(/\\?.*/, ''), s: this.status, t: (this.responseText || '').slice(0, 300) };
        }
      });
    } catch (e) {}
    return os.apply(this, arguments);
  };
  const of = window.fetch;
  if (of) {
    window.fetch = function () {
      const u = arguments[0] && (arguments[0].url || arguments[0]);
      return of.apply(this, arguments).then((r) => {
        try { if (/apply/i.test(String(u))) { r.clone().text().then((t) => { window.__q51last = { u: String(u).replace(/\\?.*/, ''), s: r.status, t: t.slice(0, 300) }; }); } } catch (e) {}
        return r;
      });
    };
  }
  return 'installed';
})()`;

async function loadKeyword(c, kw) {
  const url = 'https://we.51job.com/pc/search?keyword=' + encodeURIComponent(kw);
  await c.send('Page.navigate', { url });
  await sleep(6000);
  await ev(c, INSTALL_HOOK);
  // 滚动加载更多
  for (let i = 0; i < 4; i++) {
    await ev(c, 'window.scrollBy(0, 2200); "ok"');
    await sleep(1300);
  }
  await ev(c, 'window.scrollTo(0, 0); "ok"');
  await sleep(800);
  return await ev(c, SCAN);
}

async function applyCard(c, idx) {
  const r = await ev(c, `(() => {
    const card = document.querySelectorAll('.joblist-item')[${idx}];
    if (!card) return 'nocard';
    const b = card.querySelector('button.btn.apply');
    if (!b) return 'nobtn';
    const t = b.textContent.trim();
    if (/已申请|继续沟通/.test(t)) return 'already';
    card.scrollIntoView({ block: 'center' });
    // 51job 的按钮只认真实指针事件序列，b.click() 不会触发申请接口
    const fire = (el, type, Ctor) => {
      const rc = el.getBoundingClientRect();
      el.dispatchEvent(new Ctor(type, { bubbles: true, cancelable: true, clientX: rc.left + rc.width / 2, clientY: rc.top + rc.height / 2, button: 0, buttons: type === 'mousedown' ? 1 : 0 }));
    };
    fire(b, 'pointerdown', PointerEvent); fire(b, 'mousedown', MouseEvent);
    fire(b, 'pointerup', PointerEvent); fire(b, 'mouseup', MouseEvent);
    fire(b, 'click', MouseEvent);
    window.__q51last = null;
    return 'clicked';
  })()`);
  if (r === 'already') return { ok: false, why: 'already' };
  if (r !== 'clicked') return { ok: false, why: r };
  await sleep(3200);
  const after = await ev(c, `(() => {
    const card = document.querySelectorAll('.joblist-item')[${idx}];
    const b = card ? card.querySelector('button.btn.apply') : null;
    const body = document.body.innerText.replace(/\\s+/g, ' ');
    const cap = (body.match(/(?:今日投递太多|休息一下明天再来|今日投递已达|投递次数已用完)[^ ]{0,20}/) || [''])[0];
    const toast = [...document.querySelectorAll('.el-message, .el-notification, [class*=toast], [class*=Toast]')]
      .map((e) => (e.innerText || '').replace(/\\s+/g, ' ').trim()).filter(Boolean).join(' | ').slice(0, 90);
    const dlg = [...document.querySelectorAll('.el-dialog')].filter((d) => d.offsetParent !== null && (d.innerText || '').trim())
      .map((d) => (d.innerText || '').replace(/\\s+/g, ' ').trim()).join(' | ').slice(0, 110);
    const api = window.__q51last || null;
    return { btn: b ? b.textContent.trim() : '', cap, toast, dlg, api };
  })()`);
  const n = await ev(c, CLOSE_DLG);
  // 接口结果最可信：status=1 成功；510008 = 今日投递太多
  const apiTxt = after.api ? String(after.api.t || '') : '';
  if (/510008|今日投递太多/.test(apiTxt) || after.cap) return { ok: false, why: 'limit', tip: (apiTxt.match(/"message":"([^"]+)"/) || [])[1] || after.cap || '今日投递太多' };
  if (after.api && /"status":"?1"?/.test(apiTxt) && /apply-job/.test(after.api.u || '')) return { ok: true, why: 'sent', tip: 'api-ok', closed: n };
  // 只有按钮真的变成"已申请"，或明确出现投递成功提示，才算成功
  if (/已申请|继续沟通/.test(after.btn)) return { ok: true, why: 'sent', tip: after.dlg || after.toast, closed: n };
  if (/投递成功|申请成功/.test(after.dlg) || /投递成功|申请成功/.test(after.toast)) return { ok: true, why: 'sent', tip: after.dlg || after.toast, closed: n };
  return { ok: false, why: 'nosent', tip: (apiTxt.slice(0, 80) || after.dlg || after.toast || '').slice(0, 80) };
}

function seenKeys() {
  const s = new Set();
  try {
    for (const line of fs.readFileSync(STATE_FILE, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      try { const o = JSON.parse(line); if (o.key) s.add(o.key); } catch {}
    }
  } catch {}
  return s;
}
function record(o) { try { fs.appendFileSync(STATE_FILE, JSON.stringify(o) + '\n'); } catch {} }

function cityOfArea(area) {
  if (!area) return '';
  const t = String(area).split(/[·\-\s]/)[0].trim();
  return CITY_NAMES.includes(t) ? t : '';
}

async function main() {
  const minutes = Number(arg('minutes', 30));
  const maxJobs = Number(arg('max', 120));
  const pace = Number(arg('pace', 9));
  const dry = !!arg('dry', false);
  const kwArg = arg('keywords', '实施工程师,实施顾问,软件实施,交付实施,技术支持工程师,实施运维');
  const keywords = String(kwArg).split(',').map((s) => s.trim()).filter(Boolean);

  const deadline = Date.now() + minutes * 60000;
  const seen = seenKeys();
  const c = await openPage();
  const started = Date.now();
  log('=== Q51 start minutes=' + minutes + ' max=' + maxJobs + ' pace=' + pace + ' kw=' + keywords.length + ' seen=' + seen.size + ' dry=' + dry);
  let sent = 0, skipped = 0, errs = 0, stop = '';

  for (let pass = 1; !stop; pass++) {
    log('--- Q51 pass ' + pass + ' sent=' + sent);
    let fresh = 0;
    for (const kw of keywords) {
      if (Date.now() > deadline) { stop = 'time'; break; }
      if (sent >= maxJobs) { stop = 'max'; break; }
      let res;
      try { res = await loadKeyword(c, kw); } catch (e) { log('search fail', kw, e.message); errs++; continue; }
      if (!res) continue;
      if (res.toast) { log('!! STOP toast:', res.toast); stop = 'toast'; break; }
      const jobs = res.jobs || [];
      const inCity = jobs.filter((j) => cityOfArea(j.area));
      log('search ' + kw + ': ' + jobs.length + ' cards, 目标城市 ' + inCity.length);
      for (const j of jobs) {
        if (Date.now() > deadline) { stop = 'time'; break; }
        if (sent >= maxJobs) { stop = 'max'; break; }
        const city = cityOfArea(j.area);
        if (!city) { skipped++; continue; }
        const key = (j.id || (j.title + '|' + j.area)).slice(0, 60);
        if (seen.has(key)) { skipped++; continue; }
        if (/已申请|继续沟通/.test(j.btn)) { seen.add(key); skipped++; continue; }
        if (dry) { log('DRY would apply', j.title, '[' + j.area + ']', j.salary); seen.add(key); continue; }
        let r;
        try { r = await applyCard(c, j.i); } catch (e) { log('apply err', j.title, e.message); errs++; continue; }
        if (r.why === 'limit' || /安全验证|验证码|上限|频繁|投递太多/.test(r.tip || '')) { log('!! STOP:', r.tip || r.why); stop = 'limit'; break; }
        if (r.ok) {
          sent++; fresh++; seen.add(key);
          record({ key, title: j.title, area: j.area, salary: j.salary, kw, ts: new Date().toISOString() });
          log('OK ' + sent + ' ' + j.title + ' [' + j.area + '] ' + j.salary);
        } else if (r.why === 'already') {
          seen.add(key); skipped++;
        } else {
          errs++; log('SKIP', j.title, j.area, r.why, r.tip || '');
        }
        await sleep(pace * 1000 + Math.floor(Math.random() * 2500));
      }
      if (stop) break;
    }
    if (stop) break;
    if (fresh === 0) { log('!!! Q51 这一轮没有新岗位'); stop = 'exhausted'; }
  }

  const el = Math.round((Date.now() - started) / 60000);
  log('=== Q51 end stop=' + stop + ' sent=' + sent + ' skipped=' + skipped + ' errs=' + errs + ' elapsed=' + el + 'min');
  c.close();
}

main().catch((e) => { log('FATAL ' + e.message); process.exit(2); });
