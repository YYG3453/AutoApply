#!/usr/bin/env node
// 智联招聘 自动投递 v2（全国关键词流 + 按卡上城市过滤 19 城；点卡→右侧详情→立即投递→关“已发送”弹窗）
// usage: node zhaopin.js --minutes 60 [--keywords 实施工程师] [--max 200] [--pace 12] [--dry]
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;
const ROOT = process.env.AUTOAPPLY_DATA || __dirname;   // 数据目录（多副本共享），脚本仍在自己目录
const STATE_FILE = path.join(ROOT, 'state-zp.jsonl');
const SCORE = require(path.join(__dirname, 'score.js'));
const SKIP_FILE = path.join(ROOT, 'state-skip.jsonl');
let filtered = 0;
function skipSeenIds() {
  const s = new Set();
  try {
    for (const line of fs.readFileSync(SKIP_FILE, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      try { const o = JSON.parse(line); if (o.id) s.add(o.id); if (o.key) s.add(o.key); } catch {}
    }
  } catch {}
  return s;
}
function skipRecord(o) { try { fs.appendFileSync(SKIP_FILE, JSON.stringify(o) + '\n'); } catch {} }
const skipSeen = skipSeenIds();
const LOG_FILE = path.join(ROOT, 'run-zp.log');

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
  const t = pages.find((p) => /zhaopin/.test(p.url));
  if (!t) throw new Error('没有 zhaopin 标签页，请先在该 Chrome 打开 zhaopin.com');
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
  document.querySelectorAll('.job-card').forEach((c, i) => {
    const company = ((c.querySelector('.job-card__company-name') || {}).textContent || '').replace(/\\s+/g, ' ').trim()
      || ((c.querySelector('.job-card__company-logo') || {}).alt || '').trim();
    const tags = [...c.querySelectorAll('.job-card__skill-tag')].map((x) => x.textContent.trim());
    out.push({
      i,
      title: ((c.innerText || '').split('\\n').map((s) => s.trim()).filter(Boolean)[0] || ''),
      company,
      salary: ((c.querySelector('.job-card__salary') || {}).textContent || '').trim(),
      loc: ((c.querySelector('.job-card__location') || {}).textContent || '').replace(/\\s+/g, ' ').trim(),
      tags: tags.slice(0, 5),
      applied: /已投递|继续沟通/.test(c.innerText),
    });
  });
  const body = document.body.innerText.replace(/\\s+/g, ' ');
  return {
    jobs: out,
    toast: (body.match(/(?:安全验证|请完成验证|验证码|投递上限|操作过于频繁|操作频繁|已达上限|今日投递)[^ ]{0,30}/) || [''])[0],
    n: out.length,
  };
})()`;

function cityOf(loc) {
  if (!loc) return '';
  const t = loc.split(/[\s·\-]/)[0].trim();
  return CITY_NAMES.includes(t) ? t : '';
}

async function searchKw(c, kw) {
  const url = 'https://www.zhaopin.com/jobs/?kw=' + encodeURIComponent(kw);
  await c.send('Page.navigate', { url });
  await sleep(4500);
  return await ev(c, SCAN);
}

async function applyOne(c, idx, job) {
  await ev(c, `(() => { const d=document.querySelector('.deliver-greeting-modal'); if(d){const b=d.querySelector('.deliver-greeting-modal__close, .deliver-greeting-modal__btn--sec'); if(b) b.click();} return 'ok'; })()`);
  const clickRes = await ev(c, `(() => {
    const cs = document.querySelectorAll('.job-card');
    const card = cs[${idx}]; if (!card) return 'nocard';
    card.scrollIntoView({block:'center'});
    card.click();
    return 'clicked';
  })()`);
  if (clickRes !== 'clicked') return { ok: false, why: 'nocard' };
  await sleep(2400);
  const panel = await ev(c, `(() => {
    const p = document.querySelector('.job-detail-panel');
    const w = document.querySelector('.job-detail-summary__title-text') || document.querySelector('.job-detail-card__title-wrap');
    const full = p ? p.innerText.replace(/\\s+/g, ' ') : '';
    return { title: w ? w.innerText.replace(/\\s+/g, ' ').trim() : '', head: full.slice(0, 160), text: full.slice(0, 900) };
  })()`);
  // 正文闸门：招聘要求/岗位描述里出现"电话销售/外呼/招生"这类，标题再像也不投
  if (panel && panel.text && panel.text.length > 40) {
    const sc = SCORE.evaluate({ title: (job && job.title) || panel.title, company: job && job.company, kw: job && job.kw, jd: panel.text });
    if (!sc.pass) return { ok: false, why: 'jd', score: sc.score, reason: sc.reason, panel: panel.title };
  }
  const st = await ev(c, `(() => {
    const b = document.querySelector('button.job-detail-summary__apply');
    if (!b) return { btn: '' };
    const t = b.textContent.trim();
    if (/继续沟通|已投递/.test(t)) return { btn: t, done: true };
    b.click();
    return { btn: t };
  })()`);
  if (!st || !st.btn) return { ok: false, why: 'nobtn', panel: panel && panel.title };
  if (st.done) return { ok: false, why: 'already', panel: panel && panel.title };
  await sleep(3000);
  const after = await ev(c, `(() => {
    const d = document.querySelector('.deliver-greeting-modal');
    const t = d ? d.innerText.replace(/\\s+/g, ' ') : '';
    const bodyTip = (document.body.innerText.replace(/\\s+/g, ' ').match(/(?:安全验证|请完成验证|验证码|投递上限|操作过于频繁|操作频繁|已达上限)[^ ]{0,30}/) || [''])[0];
    if (d) { const b = d.querySelector('.deliver-greeting-modal__close') || d.querySelector('.deliver-greeting-modal__btn--sec'); if (b) b.click(); }
    return { sent: /已向对方发送简历|发送成功|投递成功/.test(t), dlg: t.slice(0, 80), tip: bodyTip, dlgPresent: !!d };
  })()`);
  const btnTxt = await ev(c, `(() => { const b=document.querySelector('button.job-detail-summary__apply'); return b? b.textContent.trim():''; })()`);
  const info = { panel: panel && panel.title, head: panel && panel.head, btnAfter: btnTxt };
  if (after.sent) return Object.assign({ ok: true, why: 'sent' }, info);
  if (/继续沟通|已投递/.test(btnTxt)) return Object.assign({ ok: true, why: 'sent-direct' }, info);
  if (after.dlgPresent) return Object.assign({ ok: false, why: 'dlg', tip: after.dlg }, info);
  return Object.assign({ ok: false, why: 'nosent', tip: after.tip }, info);
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

async function main() {
  const minutes = Number(arg('minutes', 60));
  const maxJobs = Number(arg('max', 300));
  const pace = Number(arg('pace', 12));
  const dry = !!arg('dry', false);
  const kwArg = arg('keywords', '实施工程师,实施顾问,软件实施,交付实施,售后实施,项目实施,驻场实施,实施交付,ERP实施,技术支持工程师');
  const keywords = String(kwArg).split(',').map((s) => s.trim()).filter(Boolean);

  const deadline = Date.now() + minutes * 60000;
  const seen = seenKeys();
  const c = await openPage();
  const started = Date.now();
  log('=== ZP start minutes=' + minutes + ' max=' + maxJobs + ' pace=' + pace + ' kw=' + keywords.length + ' seen=' + seen.size + ' dry=' + dry);
  let sent = 0, skipped = 0, errs = 0, stop = '';

  for (let pass = 1; !stop; pass++) {
    log('--- ZP pass ' + pass + ' sent=' + sent);
    let fresh = 0;
    for (const kw of keywords) {
      if (Date.now() > deadline) { stop = 'time'; break; }
      if (sent >= maxJobs) { stop = 'max'; break; }
      let res;
      try { res = await searchKw(c, kw); } catch (e) { log('search fail', kw, e.message); errs++; continue; }
      if (!res) continue;
      if (res.toast) { log('!! STOP toast:', res.toast); stop = 'toast'; break; }
      const jobs = res.jobs || [];
      const inCity = jobs.filter((j) => cityOf(j.loc));
      log('search ' + kw + ': ' + jobs.length + ' cards, 目标城市 ' + inCity.length);
      for (const j of jobs) {
        if (Date.now() > deadline) { stop = 'time'; break; }
        if (sent >= maxJobs) { stop = 'max'; break; }
        const city = cityOf(j.loc);
        if (!city) { skipped++; continue; }
        const key = (j.company + '|' + j.salary + '|' + j.loc).slice(0, 140);
        if (seen.has(key)) { skipped++; continue; }
        const sc = SCORE.evaluate({ title: j.title, company: j.company, kw });
        if (!sc.pass) {
          if (!skipSeen.has(key)) { skipSeen.add(key); filtered++; skipRecord({ site: 'zp', key, title: j.title, company: j.company, salary: j.salary, loc: j.loc, city, kw, score: sc.score, reason: sc.reason, ts: new Date().toISOString() }); }
          skipped++; log('FILTER ' + sc.score + ' ' + sc.reason + ' | ' + j.company + ' ' + j.loc + ' ' + (j.title || '')); continue;
        }
        if (j.applied) { seen.add(key); skipped++; continue; }
        if (dry) { log('DRY would apply', j.company, '@', city, j.salary); seen.add(key); continue; }
        let r;
        try { r = await applyOne(c, j.i, j); } catch (e) { log('apply err', j.company, e.message); errs++; continue; }
        if (r.tip && /(?:安全验证|请完成验证|验证码|投递上限|操作过于频繁|操作频繁|已达上限)/.test(r.tip)) { log('!! STOP:', r.tip); stop = 'limit'; break; }
        if (r.why === 'jd') {
          filtered++; seen.add(key); skipped++;
          if (!skipSeen.has(key)) { skipSeen.add(key); skipRecord({ site: 'zp', key, title: j.title, company: j.company, salary: j.salary, loc: j.loc, city, kw, score: r.score, reason: r.reason, ts: new Date().toISOString() }); }
          log('FILTER-JD ' + r.score + ' ' + r.reason + ' | ' + j.company + ' ' + (j.title || ''));
          await sleep(pace * 1000);
          continue;
        }
        if (r.ok) {
          sent++; fresh++; seen.add(key);
          record({ key, title: r.panel || '', company: j.company, salary: j.salary, loc: j.loc, city, kw, score: sc.score, ts: new Date().toISOString() });
          log('OK ' + sent + ' ' + (r.panel || '') + ' @ ' + j.company + ' ' + j.salary + ' [' + j.loc + ']');
        } else if (r.why === 'already') {
          seen.add(key); skipped++;
        } else {
          errs++; log('SKIP', j.company, j.loc, r.why, r.tip || '');
        }
        await sleep(pace * 1000 + Math.floor(Math.random() * 3000));
      }
      if (stop) break;
    }
    if (stop) break;
    if (fresh === 0) { log('!!! ZP 这一轮没有新岗位'); stop = 'exhausted'; }
  }

  const el = Math.round((Date.now() - started) / 60000);
  log('=== ZP end stop=' + stop + ' sent=' + sent + ' skipped=' + skipped + ' errs=' + errs + ' elapsed=' + el + 'min');
  c.close();
}

main().catch((e) => { log('FATAL ' + e.message); process.exit(2); });
