#!/usr/bin/env node
// BOSS直聘 自动投递（点“立即沟通”发招呼语）
// usage: node boss.js --minutes 10 [--cities 101010100,101020100] [--keywords 实施工程师,交付实施] [--max 200] [--dry]
// 需要 Chrome 以 --remote-debugging-port=9333 运行，且已登录 BOSS。
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;
const ROOT = __dirname;
const STATE_FILE = path.join(ROOT, 'state.jsonl');
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
const LOG_FILE = path.join(ROOT, 'run.log');

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

async function openPage() {
  const list = await httpJSON('/json/list');
  const pages = list.filter((t) => t.type === 'page' && /zhipin/.test(t.url));
  const t = pages.find((x) => /web\/geek\/job/.test(x.url)) || pages.find((x) => !/geek\/chat/.test(x.url)) || list.filter((x) => x.type === 'page')[0];
  if (!t) throw new Error('no page');
  const c = new CDP(t.webSocketDebuggerUrl);
  await c.connect();
  return c;
}

async function ev(c, expr) {
  const r = await c.send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true, userGesture: true });
  if (r.exceptionDetails) throw new Error('JS: ' + (r.exceptionDetails.exception && r.exceptionDetails.exception.description || r.exceptionDetails.text));
  return r.result ? r.result.value : undefined;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function arg(name, def) {
  const i = process.argv.indexOf('--' + name);
  if (i < 0) return def;
  const v = process.argv[i + 1];
  return v && !v.startsWith('--') ? v : true;
}

const CITIES = JSON.parse(fs.readFileSync(path.join(ROOT, 'cities.json'), 'utf8'));

function seenIds() {
  const s = new Set();
  try {
    for (const line of fs.readFileSync(STATE_FILE, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      try { const o = JSON.parse(line); if (o.id) s.add(o.id); } catch {}
    }
  } catch {}
  return s;
}
function record(o) { try { fs.appendFileSync(STATE_FILE, JSON.stringify(o) + '\n'); } catch {} }

const SCAN = `(() => {
  const out = [];
  document.querySelectorAll('li.job-card-box').forEach((c) => {
    const a = c.querySelector('a.job-name'); if (!a) return;
    const m = (a.getAttribute('href') || '').match(/job_detail\\/([^.]+)\\.html/);
    const tags = [...c.querySelectorAll('ul.tag-list li')].map((x) => x.textContent.trim());
    out.push({
      id: m ? m[1] : '',
      title: a.textContent.trim(),
      salary: (c.querySelector('.job-salary') || {}).textContent || '',
      company: (c.querySelector('.boss-name') || {}).textContent || '',
      loc: (c.querySelector('.company-location') || {}).textContent || '',
      tags,
    });
  });
  return { jobs: out, toast: (document.body.innerText.match(/[^\\n]*(?:安全验证|请完成验证|验证码|次数已用完|沟通次数|操作过于频繁|操作频繁|已达上限)[^\\n]*/) || [''])[0] };
})()`;

async function searchCity(cityCode, kw, c) {
  const url = 'https://www.zhipin.com/web/geek/job?query=' + encodeURIComponent(kw) + '&city=' + cityCode;
  await c.send('Page.navigate', { url });
  await sleep(4500);
  // 滚到底，触发更多卡片
  for (let i = 0; i < 6; i++) {
    await ev(c, 'window.scrollTo(0, document.body.scrollHeight); "ok"');
    await sleep(1200);
  }
  const res = await ev(c, SCAN);
  return res;
}

const PROBE_AFTER = `(() => {
  const bd = document.querySelector('.chat-block-dialog');
  const block = bd ? bd.innerText.replace(/\\n/g, ' ') : '';
  const d = document.querySelector('.greet-boss-dialog');
  const dlgText = d ? d.innerText.replace(/\\n/g, ' ') : '';
  const tip = (document.body.innerText.match(/[^\\n]*(?:安全验证|请完成验证|验证码|次数已用完|沟通次数|操作过于频繁|操作频繁|已达上限)[^\\n]*/) || [''])[0];
  if (d) { const b = d.querySelector('a.cancel-btn') || d.querySelector('.close'); if (b) b.click(); }
  return { sent: /已向BOSS发送消息/.test(dlgText), dlg: dlgText.slice(0, 90), tip, block };
})()`;

// 关掉“您今天已与N位BOSS沟通，还剩M次”的温馨提示，返回提示原文
const DISMISS_BLOCK = `(() => {
  const bd = document.querySelector('.chat-block-dialog');
  if (!bd) return '';
  const txt = bd.innerText.replace(/\\n/g, ' ');
  const ok = [...bd.querySelectorAll('a,button,div,span')].filter((e) => /^(好|确定|我知道了|继续)$/.test((e.textContent || '').trim())).pop();
  if (ok) ok.click();
  return txt;
})()`;

const LIMIT_RE = /还剩\s*0\s*次|次数已用完|今日沟通次数已达上限|沟通次数已用完|明日再来/;

async function greetOne(c, idx) {
  // 清掉上一次可能残留的“已向BOSS发送消息”弹窗 / 限额提示
  await ev(c, `(() => { const d=document.querySelector('.greet-boss-dialog'); if(d){const b=d.querySelector('a.cancel-btn, .close'); if(b) b.click();} return 'ok'; })()`);
  const leftover = await ev(c, DISMISS_BLOCK);
  if (leftover && LIMIT_RE.test(leftover)) return { ok: false, why: 'limit', dlg: leftover.slice(0, 90) };
  if (leftover) await sleep(900);
  // 点击第 idx 张卡片 → 右侧详情 → 立即沟通
  const r1 = await ev(c, `(() => {
    const cs = document.querySelectorAll('li.job-card-box');
    const card = cs[${idx}]; if (!card) return 'nocard';
    card.querySelector('a.job-name').click();
    return 'clicked';
  })()`);
  if (r1 !== 'clicked') return { ok: false, why: 'nocard' };
  await sleep(1500);
  const st = await ev(c, `(() => {
    const b = document.querySelector('a.op-btn-chat');
    if (!b) return { btn: '' };
    const t = b.textContent.trim();
    if (/继续沟通|沟通过/.test(t)) return { btn: t, done: true };
    b.click();
    return { btn: t };
  })()`);
  if (!st || !st.btn) return { ok: false, why: 'nobtn' };
  if (st.done) return { ok: false, why: 'already' };
  await sleep(2600);
  let after = await ev(c, PROBE_AFTER);
  if (!after.sent && after.block) {
    // 首次遇到“还剩N次”的温馨提示：点“好”关掉，再点一次立即沟通
    const blockTxt = await ev(c, DISMISS_BLOCK);
    if (blockTxt && LIMIT_RE.test(blockTxt)) return { ok: false, why: 'limit', dlg: blockTxt.slice(0, 90) };
    await sleep(1200);
    const again = await ev(c, `(() => { const b = document.querySelector('a.op-btn-chat'); if (!b) return 'nobtn'; const t = b.textContent.trim(); if (/继续沟通|沟通过/.test(t)) return 'done'; b.click(); return 'clicked'; })()`);
    if (again === 'done') return { ok: false, why: 'already' };
    if (again === 'clicked') {
      await sleep(2600);
      after = await ev(c, PROBE_AFTER);
      if (!after.sent && after.block) {
        const t2 = await ev(c, DISMISS_BLOCK);
        if (t2 && LIMIT_RE.test(t2)) return { ok: false, why: 'limit', dlg: t2.slice(0, 90) };
      }
    }
  }
  return { ok: !!after.sent, why: after.sent ? 'sent' : 'nosent', dlg: after.dlg || after.block || '', tip: after.tip };
}

async function main() {
  const minutes = Number(arg('minutes', 60));
  const maxJobs = Number(arg('max', 400));
  // pace: 每投一个之后的基础等待秒数（真实等待 = pace + 0~4s 随机）
  const pace = Number(arg('pace', 11));
  const dry = !!arg('dry', false);
  const kwArg = arg('keywords', '实施工程师,交付实施,售后实施,软件实施,实施顾问,技术支持工程师');
  const keywords = String(kwArg).split(',').map((s) => s.trim()).filter(Boolean);
  const cityArg = arg('cities', '');
  let cities = CITIES;
  if (cityArg && cityArg !== true) {
    const want = String(cityArg).split(',').map((s) => s.trim());
    cities = CITIES.filter((x) => want.includes(x.code) || want.includes(x.name));
  }

  const deadline = Date.now() + minutes * 60000;
  const seen = seenIds();
  const c = await openPage();
  const started = Date.now();
  log('=== start, minutes=' + minutes + ', max=' + maxJobs + ', pace=' + pace + 's, cities=' + cities.length + ', kws=' + keywords.length + ', seen=' + seen.size + ', dry=' + dry);
  let sent = 0, skipped = 0, errs = 0;
  let stop = '';

  for (let pass = 1; !stop; pass++) {
    log('--- pass ' + pass + ' sent=' + sent);
    let fresh = 0;
    let nosentRun = 0;
    for (const kw of keywords) {
      for (const city of cities) {
        if (Date.now() > deadline) { stop = 'time'; break; }
        if (sent >= maxJobs) { stop = 'max'; break; }
        let res;
        try { res = await searchCity(city.code, kw, c); } catch (e) { log('search fail', city.name, kw, e.message); errs++; continue; }
        if (!res) continue;
        if (res.toast && /(?:安全验证|请完成验证|验证码|次数已用完|沟通次数|操作频繁|已达上限)/.test(res.toast)) { log('!! STOP toast:', res.toast); stop = 'toast'; break; }
        const jobs = res.jobs || [];
        log(`search ${city.name}/${kw}: ${jobs.length} cards`);
        for (let i = 0; i < jobs.length; i++) {
          if (Date.now() > deadline) { stop = 'time'; break; }
          if (sent >= maxJobs) { stop = 'max'; break; }
          const j = jobs[i];
          if (!j.id) continue;
          if (seen.has(j.id)) { skipped++; continue; }
          const sc = SCORE.evaluate({ title: j.title, company: j.company, kw });
          if (!sc.pass) {
            if (!skipSeen.has(j.id)) { skipSeen.add(j.id); filtered++; skipRecord({ site: 'boss', id: j.id, title: j.title, company: j.company, salary: j.salary, loc: j.loc, city: city.name, kw, score: sc.score, reason: sc.reason, ts: new Date().toISOString() }); }
            skipped++; log('FILTER ' + sc.score + ' ' + sc.reason + ' | ' + j.title + ' @ ' + j.company + ' ' + city.name); continue;
          }
          if (dry) { log('DRY would greet', j.title, j.company); seen.add(j.id); continue; }
          let r;
          try { r = await greetOne(c, i); } catch (e) { log('greet err', j.title, e.message); errs++; continue; }
          if (r.tip && /(?:安全验证|请完成验证|验证码|次数已用完|沟通次数|操作频繁|已达上限)/.test(r.tip)) { log('!! STOP:', r.tip); stop = 'limit'; break; }
          if (r.why === 'limit') { log('!! 今日BOSS沟通次数已用完:', r.dlg || ''); stop = 'limit'; break; }
          if (r.ok) {
            sent++; fresh++; seen.add(j.id); nosentRun = 0;
            record({ id: j.id, title: j.title, company: j.company, salary: j.salary, loc: j.loc, city: city.name, kw, ts: new Date().toISOString() });
            log(`OK ${sent} ${j.title} @ ${j.company} ${j.salary}`);
          } else if (r.why === 'already') {
            seen.add(j.id); skipped++;
          } else {
            errs++; log('SKIP', j.title, r.why, r.tip || '');
            if (r.why === 'nosent') {
              nosentRun++;
              if (nosentRun >= 6) {
                log('!! 连续 6 次发送失败（多半是今天沟通次数用完了），提前结束 BOSS 阶段');
                stop = 'nosent';
                break;
              }
            } else {
              nosentRun = 0;
            }
          }
          await sleep(pace * 1000 + Math.floor(Math.random() * 4000));
        }
        if (stop) break;
      }
      if (stop) break;
    }
    if (stop) break;
    if (fresh === 0) { log('!!! 这一轮没有任何新岗位（已过滤 ' + skipped + ' 个投过的），可能已覆盖完'); stop = 'exhausted'; }
  }

  const el = Math.round((Date.now() - started) / 60000);
  log(`=== end stop=${stop} sent=${sent} skipped=${skipped} errs=${errs} elapsed=${el}min`);
  c.close();
}

main().catch((e) => { log('FATAL ' + e.message); process.exit(2); });
