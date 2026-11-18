#!/usr/bin/env node
// 应届生求职网(q.yingjiesheng.com) 自动投递：关键词搜索 → 城市筛选 → 逐卡「立即申请」→ 关“投递成功”弹窗
// usage: node yingjiesheng.js --minutes 20 [--keywords 实施工程师] [--max 120] [--pace 8] [--dry]
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = process.env.CDP_PORT ? Number(process.env.CDP_PORT) : 9333;
const ROOT = process.env.AUTOAPPLY_DATA || __dirname;   // 数据目录（多副本共享），脚本仍在自己目录
const STATE_FILE = path.join(ROOT, 'state-yjs.jsonl');
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
const LOG_FILE = path.join(ROOT, 'run-yjs.log');

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
  const t = pages.find((p) => /yingjiesheng/.test(p.url));
  if (!t) throw new Error('没有应届生标签页，请先在该 Chrome 打开 q.yingjiesheng.com');
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
// 应届生筛选栏里直接可见的城市
const VISIBLE_CITIES = ['北京', '上海', '广州', '深圳', '天津', '重庆', '南京', '杭州', '成都', '武汉', '西安'];

const CLOSE_DLG = `(() => {
  [...document.querySelectorAll('.el-dialog__wrapper, .el-message-box__wrapper')].forEach((d) => {
    if (d.style.display !== 'none') {
      const x = d.querySelector('.el-dialog__headerbtn') || d.querySelector('.el-message-box__headerbtn');
      if (x) x.click();
    }
  });
  return 'ok';
})()`;

async function scan(c) {
  return await ev(c, `(() => {
    const cards = [...document.querySelectorAll('.search-list-item.job')];
    return cards.map((c2, i) => {
      const t = (c2.innerText || '').replace(/\\s+/g, ' ').trim();
      const m = t.match(/([\\u4e00-\\u9fa5]{2,6})-([\\u4e00-\\u9fa5]{2,10})/);
      let company = '';
      const cel = c2.querySelector('.company, .job-company, [class*=company], a[href*="/company"]');
      if (cel) company = (cel.innerText || '').replace(/\\s+/g, ' ').trim();
      if (!company) {
        const cm = t.match(/([\\u4e00-\\u9fa5A-Za-z（）()]{2,26}(?:股份有限公司|有限责任公司|有限公司|分公司|集团|事务所|研究院|工作室))/);
        if (cm) company = cm[1];
      }
      return {
        i,
        title: t.split(' ')[0] || '',
        company,
        text: t.slice(0, 120),
        loc: m ? m[0] : '',
        city: m ? m[1] : '',
        applied: !c2.querySelector('.notdelivery'),
      };
    });
  })()`);
}

async function pickCity(c, city) {
  const r = await ev(c, `(() => {
    const els = [...document.querySelectorAll('.area-option-item')].filter((e) => e.offsetParent !== null && (e.textContent || '').trim() === ${JSON.stringify(city)});
    if (!els.length) return 'nocity';
    els[0].click();
    return 'ok';
  })()`);
  return r;
}

async function applyCard(c, idx) {
  const r = await ev(c, `(() => {
    const cards = [...document.querySelectorAll('.search-list-item.job')];
    const card = cards[${idx}];
    if (!card) return 'nocard';
    const b = card.querySelector('.notdelivery');
    if (!b) return 'already';
    card.scrollIntoView({ block: 'center' });
    b.click();
    return 'clicked';
  })()`);
  if (r !== 'clicked') return { ok: false, why: r };
  await sleep(2600);
  const after = await ev(c, `(() => {
    const ds = [...document.querySelectorAll('.el-dialog__wrapper, .el-message-box__wrapper')].filter((d) => d.style.display !== 'none');
    const txt = ds.map((d) => (d.innerText || '').replace(/\\s+/g, ' ').trim()).join(' | ').slice(0, 120);
    return { present: ds.length > 0, txt };
  })()`);
  await ev(c, CLOSE_DLG);
  if (/投递成功|已投递|申请成功/.test(after.txt)) return { ok: true, why: 'sent', tip: after.txt };
  if (after.present) return { ok: false, why: 'dlg', tip: after.txt };
  return { ok: true, why: 'sent-nodlg' };
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
  const minutes = Number(arg('minutes', 20));
  const maxJobs = Number(arg('max', 120));
  const pace = Number(arg('pace', 8));
  const dry = !!arg('dry', false);
  const kwArg = arg('keywords', '实施工程师,实施顾问,软件实施,交付实施,技术支持');
  const keywords = String(kwArg).split(',').map((s) => s.trim()).filter(Boolean);
  const cities = CITY_NAMES.filter((n) => VISIBLE_CITIES.includes(n));

  const deadline = Date.now() + minutes * 60000;
  const seen = seenKeys();
  const c = await openPage();
  const started = Date.now();
  log('=== YJS start minutes=' + minutes + ' max=' + maxJobs + ' pace=' + pace + ' kw=' + keywords.length + ' cities=' + cities.length + ' seen=' + seen.size + ' dry=' + dry);
  let sent = 0, skipped = 0, errs = 0, stop = '';

  for (let pass = 1; !stop; pass++) {
    log('--- YJS pass ' + pass + ' sent=' + sent);
    let fresh = 0;
    for (const kw of keywords) {
      if (stop) break;
      const url = 'https://q.yingjiesheng.com/jobs/search/' + encodeURIComponent(kw);
      await c.send('Page.navigate', { url });
      await sleep(5000);
      await ev(c, CLOSE_DLG);
      const blocked = await ev(c, `(() => {
        const t = document.body.innerText.replace(/\\s+/g, ' ');
        return /滑动验证|访问验证|请按住滑块|拖动到最右边/.test(t) ? (t.match(/(?:访问验证|滑动验证)[^ ]{0,20}/) || ['captcha'])[0] : '';
      })()`);
      if (blocked) {
        log('!! YJS 被滑块验证拦住（需要人工拖一次滑块）:', blocked);
        stop = 'captcha';
        break;
      }
      for (const city of cities) {
        if (Date.now() > deadline) { stop = 'time'; break; }
        if (sent >= maxJobs) { stop = 'max'; break; }
        const pr = await pickCity(c, city);
        if (pr !== 'ok') { continue; }
        // 等列表刷新：最多轮询 5 次，出现本城岗位就立刻收
        let jobs = [];
        for (let t = 0; t < 5; t++) {
          await sleep(1500);
          jobs = (await scan(c)) || [];
          if (jobs.some((j) => j.city === city)) break;
        }
        const mine = jobs.filter((j) => j.city === city);
        log('search ' + kw + '/' + city + ': ' + jobs.length + ' cards, 本城 ' + mine.length);
        for (const j of mine) {
          if (Date.now() > deadline) { stop = 'time'; break; }
          if (sent >= maxJobs) { stop = 'max'; break; }
          const key = (j.title + '|' + j.loc).slice(0, 120);
          if (seen.has(key)) { skipped++; continue; }
          const sc = SCORE.evaluate({ title: j.title, company: '', kw });
          if (!sc.pass) {
            if (!skipSeen.has(key)) { skipSeen.add(key); filtered++; skipRecord({ site: 'yjs', key, title: j.title, company: j.company || '', loc: j.loc, kw, score: sc.score, reason: sc.reason, ts: new Date().toISOString() }); }
            skipped++; log('FILTER ' + sc.score + ' ' + sc.reason + ' | ' + j.title + ' ' + j.loc); continue;
          }
          if (j.applied) { seen.add(key); skipped++; continue; }
          if (dry) { log('DRY would apply', j.title, j.loc); seen.add(key); continue; }
          let r;
          try { r = await applyCard(c, j.i); } catch (e) { log('apply err', j.title, e.message); errs++; continue; }
          if (/安全验证|验证码|上限|频繁/.test(r.tip || '')) { log('!! STOP:', r.tip); stop = 'limit'; break; }
          if (r.ok) {
            sent++; fresh++; seen.add(key);
            record({ key, title: j.title, company: j.company || '', loc: j.loc, kw, score: sc.score, ts: new Date().toISOString() });
            log('OK ' + sent + ' ' + j.title + ' [' + j.loc + ']');
          } else {
            errs++; log('SKIP', j.title, r.why, r.tip || '');
          }
          await sleep(pace * 1000 + Math.floor(Math.random() * 2500));
        }
        if (stop) break;
      }
    }
    if (stop) break;
    if (fresh === 0) { log('!!! YJS 这一轮没有新岗位'); stop = 'exhausted'; }
  }

  const el = Math.round((Date.now() - started) / 60000);
  log('=== YJS end stop=' + stop + ' sent=' + sent + ' skipped=' + skipped + ' errs=' + errs + ' elapsed=' + el + 'min');
  c.close();
}

main().catch((e) => { log('FATAL ' + e.message); process.exit(2); });
