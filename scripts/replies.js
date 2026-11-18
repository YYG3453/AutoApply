#!/usr/bin/env node
/*
 * replies.js —— 只读采集「对方有没有回我 / 有没有约面试」
 *
 * 为什么单独一个脚本：投递脚本只关心“投出去”，而一个岗位值不值得跟，
 * 取决于对方回没回。这里只读会话，一个字的回复都不发。
 *
 * 用法：
 *   node --experimental-websocket replies.js                       # BOSS + 智联，每站最多 30 个会话
 *   node --experimental-websocket replies.js --sites boss --max 10
 *   node --experimental-websocket replies.js --dry                 # 只打印不落盘
 *
 * 输出：scripts/state-replies.jsonl（每个会话一行）
 */
'use strict';

const fs = require('fs');
const path = require('path');
const http = require('http');

const ROOT = __dirname;
const OUT = path.join(ROOT, 'state-replies.jsonl');

const argv = process.argv.slice(2);
function arg(name, def) {
  const i = argv.indexOf('--' + name);
  if (i < 0) return def;
  const v = argv[i + 1];
  return v && v.indexOf('--') !== 0 ? v : true;
}
const PORT = parseInt(arg('port', '9333'), 10) || 9333;
const MAX = parseInt(arg('max', '30'), 10) || 30;
const DRY = argv.indexOf('--dry') >= 0;
const DEBUG = argv.indexOf('--debug') >= 0;
const SITES = String(arg('sites', 'boss,zp')).split(',').map((s) => s.trim()).filter(Boolean);

function log(msg) {
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  console.log('[' + d.getFullYear() + '/' + p(d.getMonth() + 1) + '/' + p(d.getDate()) + ' ' +
    p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()) + '] ' + msg);
}

// ---------------- CDP 客户端（与 chat.js 同款，已验证可用） ----------------
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
        let m; try { m = JSON.parse(ev.data); } catch (e) { return; }
        if (m.id && this.pending.has(m.id)) {
          const p = this.pending.get(m.id);
          this.pending.delete(m.id);
          if (m.error) p.reject(new Error(m.error.message || 'cdp error')); else p.resolve(m.result);
        }
      };
    });
  }
  send(method, params, timeoutMs) {
    const id = ++this.id;
    params = params || {};
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve: resolve, reject: reject });
      this.ws.send(JSON.stringify({ id: id, method: method, params: params }));
      setTimeout(() => {
        if (this.pending.has(id)) { this.pending.delete(id); reject(new Error('timeout ' + method)); }
      }, timeoutMs || 20000);
    });
  }
  close() { try { this.ws.close(); } catch (e) {} }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// 打开/复用某个网站的标签页，返回连上它的 CDP
async function pageFor(match, url) {
  let list = await httpJSON('/json/list');
  let t = (list || []).find((x) => x.type === 'page' && x.url.indexOf(match) >= 0);
  if (!t && url) {
    const ver = await httpJSON('/json/version');
    const b = new CDP(ver.webSocketDebuggerUrl);
    await b.connect();
    try { await b.send('Target.createTarget', { url: url }); } finally { b.close(); }
    log('已打开标签页 ' + url + '，等它加载…');
    await sleep(8000);
    list = await httpJSON('/json/list');
    t = (list || []).find((x) => x.type === 'page' && x.url.indexOf(match) >= 0);
  }
  if (!t) return null;
  const c = new CDP(t.webSocketDebuggerUrl);
  await c.connect();
  return c;
}

async function ev(c, expr, timeoutMs) {
  const r = await c.send('Runtime.evaluate', {
    expression: expr, returnByValue: true, awaitPromise: true,
  }, timeoutMs || 20000);
  if (r && r.exceptionDetails) throw new Error(r.exceptionDetails.text || 'js error');
  return r && r.result ? r.result.value : undefined;
}

// ---------------- 页面内的小工具 ----------------
const HELPERS = `
  var txt=function(el){ return el ? (el.textContent||'').replace(/\\s+/g,' ').trim() : ''; };
  var fire=function(el){ if(!el) return; ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(function(t){
      el.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true,view:window})); }); };
`;

// ---------------- BOSS ----------------
const BOSS_MATCH = 'zhipin.com/web/geek/chat';
const BOSS_URL = 'https://www.zhipin.com/web/geek/chat';

function bossListJS() {
  return 'JSON.stringify((function(){' + HELPERS + `
    var items=[].slice.call(document.querySelectorAll('.friend-content-warp'));
    return items.map(function(it,i){
      var badge=it.querySelector('.badge');
      return { i:i,
        name: txt(it.querySelector('.name-text')),
        last: txt(it.querySelector('.last-msg-text')),
        time: txt(it.querySelector('.time')),
        unread: badge ? (parseInt(txt(badge),10)||1) : 0 };
    });
  })())`;
}

function bossOpenJS(i) {
  return '(function(){' + HELPERS + `
    var items=[].slice.call(document.querySelectorAll('.friend-content-warp'));
    var it=items[${i}]; if(!it) return 'no-item';
    fire(it.querySelector('.friend-content')||it); return 'ok';
  })()`;
}

function bossThreadJS() {
  return 'JSON.stringify((function(){' + HELPERS + `
    var head=document.querySelector('.chat-conversation .chat-header')||document.querySelector('.chat-header');
    var ul=document.querySelector('.chat-conversation ul.im-list')||document.querySelector('ul.im-list');
    if(!ul) return {ok:false};
    var rows=[].slice.call(ul.querySelectorAll('li.message-item')).map(function(li){
      return { mine: li.className.indexOf('item-friend')<0,
               sys: li.className.indexOf('item-system')>=0,
               text: txt(li.querySelector('.text-content')||li) };
    }).filter(function(r){ return r.text; });
    var theirs=rows.filter(function(r){ return !r.mine && !r.sys; });
    return { ok:true,
      job: txt(head&&head.querySelector('.job-name')),
      company: txt(head&&head.querySelector('.company-name')),
      theLast: theirs.length ? theirs[theirs.length-1].text : '',
      head: (document.querySelector('.chat-conversation')||{innerText:''}).innerText || '',
      mineCount: rows.filter(function(r){ return r.mine; }).length,
      theirsCount: theirs.length };
  })())`;
}

// ---------------- 智联 ----------------
const ZP_MATCH = 'i.zhaopin.com/im';
const ZP_URL = 'https://i.zhaopin.com/im';

function zpListJS() {
  return 'JSON.stringify((function(){' + HELPERS + `
    var items=[].slice.call(document.querySelectorAll('.im-session-item'));
    return items.map(function(it,i){
      return { i:i,
        name: txt(it.querySelector('.im-session-item__name')),
        company: txt(it.querySelector('.im-session-item__company-name')),
        job: txt(it.querySelector('.im-session-item__job')),
        last: txt(it.querySelector('.im-session-item__preview-text')),
        time: txt(it.querySelector('.im-session-item__time')) };
    });
  })())`;
}

function zpOpenJS(i) {
  return '(function(){' + HELPERS + `
    var items=[].slice.call(document.querySelectorAll('.im-session-item'));
    var it=items[${i}]; if(!it) return 'no-item';
    fire(it.querySelector('.im-session-item__body')||it); return 'ok';
  })()`;
}

function zpThreadJS(myTexts) {
  return 'JSON.stringify((function(){' + HELPERS + `
    var mine=${JSON.stringify(myTexts || [])};
    var isMine=function(t){ var s=(t||'').slice(0,18); if(!s) return false;
      if ((t||'').indexOf('您好，我有洗浴管理软件') >= 0) return true;
      if ((t||'').indexOf('我有洗浴管理软件实施售后经验') >= 0) return true;
      return mine.some(function(x){ return x && (x.slice(0,18)===s || x.indexOf(s)>=0); }); };
    var all=[].slice.call(document.querySelectorAll('.im-main-panel__timeline-inner .im-message'))
      .filter(function(m){ return !m.parentElement.closest('.im-message'); });
    var rows=all.map(function(m){ return { text: txt(m),
      tip: m.className.indexOf('im-message--tip')>=0,
      custom: m.className.indexOf('im-message--custom')>=0 }; }).filter(function(r){ return r.text; });
    var plain=rows.filter(function(r){ return !r.tip && !r.custom; });
    var theirs=plain.filter(function(r){ return !isMine(r.text); });
    return { ok:true,
      theLast: theirs.length ? theirs[theirs.length-1].text : '',
      theirsCount: theirs.length, total: plain.length };
  })())`;
}

function myTexts() {
  const p = path.join(ROOT, 'state-chat.jsonl');
  const out = [];
  if (fs.existsSync(p)) {
    fs.readFileSync(p, 'utf8').split('\n').forEach(function (line) {
      if (!line.trim()) return;
      try { const o = JSON.parse(line); if (o.text) out.push(String(o.text)); } catch (e) {}
    });
  }
  return out.slice(-300);
}

const INTERVIEW_RE = /((约|安排|定|挑|选|方便).{0,8}(面试|面谈|复试|初试))|((面试|面谈|复试|初试).{0,12}(时间|几点|周[一二三四五六日末]|明天|后天|上午|下午|\d{1,2}[:：]\d{2}))|(来我们?公司.{0,6}(面试|聊聊|谈谈))/;
const CANNED_RE = /通过(会|后).{0,8}(安排|通知).{0,4}面试|会安排面试|面试通知|综合评估/;
const REJECT_RE = /不合适|不太合适|不适合|不匹配|不太匹配|不是很匹配|不符合|不能发挥|已招满|招到了|暂时不需要|不需要了|祝好|预祝找到|方向不合适/;
const ASK_RESUME_RE = /附件简历|简历过来|发一份.*简历|发个简历|简历发/;
const PARTTIME_RE = /兼职|外包|派遣/;

// BOSS 会话头部内文本：姓名 / 人才经纪人 / 更多 / 岗位 / 薪资 / 城市 / 查看职位 …
const CITY_RE = /(北京|上海|广州|深圳|成都|杭州|重庆|西安|苏州|武汉|南京|天津|郑州|长沙|东莞|佛山|宁波|青岛|合肥)/;
const SALARY_RE = /(\d+(\.\d+)?\s*[-~]\s*\d+(\.\d+)?\s*[Kk万]|[0-9]+-[0-9]+元)/;
const JOB_RE = /实施|工程|技术|运维|售后|交付|专员|经理|顾问|支持|开发|驻场|主管/;

function parseHead(text) {
  const lines = String(text || '').split('\n').map(function (s) { return s.trim(); }).filter(Boolean);
  let job = '', city = '', salary = '';
  for (let i = 0; i < lines.length && i < 10; i++) {
    const s = lines[i];
    if (!city && s.length <= 12 && CITY_RE.test(s)) { city = (s.match(CITY_RE) || [''])[0]; continue; }
    if (!salary && s.length <= 16 && SALARY_RE.test(s)) { salary = s; continue; }
    if (!job && s !== '查看职位' && s.length >= 4 && JOB_RE.test(s)) job = s;
  }
  return { job: job, city: city, salary: salary };
}

// 一句话总结对方的态度：interview 约面 / askResume 要简历 / replied 普通回复 / rejected 拒绝 / notfit 兼职外包 / none 没回
function statusOf(last, interview) {
  if (!last) return 'none';
  if (interview && !CANNED_RE.test(last)) return 'interview';
  if (ASK_RESUME_RE.test(last)) return 'askResume';
  if (REJECT_RE.test(last)) return 'rejected';
  if (PARTTIME_RE.test(last)) return 'notfit';
  return 'replied';
}

const STATUS_CN = { interview: '提到面试', askResume: '要简历', replied: '已回复', rejected: '不合适', notfit: '兼职/外包', none: '未回复' };

const results = [];
let replied = 0, interview = 0;

function push(o) {
  results.push(o);
  if (o.replied) replied++;
  if (o.interview) interview++;
  log('  ' + o.site + ' | ' + String(o.company || o.name || '?').slice(0, 16) + ' | ' +
    (STATUS_CN[o.status] || o.status) + ' | ' + String(o.theLast || '').slice(0, 30));
}

async function doBoss() {
  const c = await pageFor(BOSS_MATCH, BOSS_URL);
  if (!c) { log('!! BOSS 聊天页打不开，跳过'); return; }
  try {
    let list;
    try { list = JSON.parse(await ev(c, bossListJS())); } catch (e) { log('!! BOSS 会话列表读取失败：' + e.message); return; }
    log('BOSS 会话 ' + list.length + ' 个，取前 ' + Math.min(MAX, list.length) + ' 个');
    const n = Math.min(MAX, list.length);
    for (let i = 0; i < n; i++) {
      const it = list[i];
      try {
        await ev(c, bossOpenJS(i));
        await sleep(700);
        const th = JSON.parse(await ev(c, bossThreadJS()));
        if (!th || !th.ok) continue;
        if (DEBUG && i === 0) {
          try {
            const head = await ev(c, "var e=document.querySelector('.chat-conversation'); e ? e.innerText.slice(0,260) : 'none'");
            log('debug head: ' + String(head).replace(/\n/g, ' / '));
            const hrefs = await ev(c, "JSON.stringify([].slice.call(document.querySelectorAll('.chat-conversation a')).map(function(a){return a.getAttribute('href')||'';}).filter(function(h){return h.indexOf('job_detail')>=0;}))");
            log('debug joblinks: ' + hrefs);
          } catch (e2) {}
        }
        const lastTheirs = !!th.theLast;
        const head = parseHead(th.head || '');
        const interview = lastTheirs && INTERVIEW_RE.test(th.theLast);
        push({
          site: 'boss', key: 'boss:' + (it.name || i), name: it.name,
          company: th.company || '', job: th.job || head.job || '',
          city: head.city || '', salary: head.salary || '',
          lastTime: it.time, unread: it.unread, lastBy: lastTheirs ? 'them' : 'me',
          theLast: th.theLast || '',
          replied: lastTheirs || it.unread > 0,
          interview: interview,
          status: statusOf(lastTheirs ? th.theLast : '', interview),
          mineCount: th.mineCount, theirsCount: th.theirsCount,
          ts: new Date().toISOString(),
        });
      } catch (e) { log('  !! 会话 ' + i + ' 读取失败：' + e.message); }
    }
  } finally { c.close(); }
}

async function doZp() {
  const c = await pageFor(ZP_MATCH, ZP_URL);
  if (!c) { log('!! 智联 IM 打不开，跳过'); return; }
  try {
    let list;
    try { list = JSON.parse(await ev(c, zpListJS())); } catch (e) { log('!! 智联 会话列表读取失败：' + e.message); return; }
    const mine = myTexts();
    log('智联 会话 ' + list.length + ' 个，取前 ' + Math.min(MAX, list.length) + ' 个');
    const n = Math.min(MAX, list.length);
    for (let i = 0; i < n; i++) {
      const it = list[i];
      try {
        await ev(c, zpOpenJS(i));
        await sleep(700);
        const th = JSON.parse(await ev(c, zpThreadJS(mine)));
        if (!th || !th.ok) continue;
        const lastText = th.theLast || '';
        const interview = lastText.length > 0 && INTERVIEW_RE.test(lastText);
        push({
          site: 'zp', key: 'zp:' + (it.name || i), name: it.name,
          company: it.company || it.name, job: it.job || '',
          city: it.city || '', salary: it.salary || '',
          lastTime: it.time, unread: 0, lastBy: th.theLast ? 'them' : 'me',
          theLast: lastText, replied: !!th.theLast,
          interview: interview,
          status: statusOf(th.theLast || '', interview),
          mineCount: 0, theirsCount: th.theirsCount,
          ts: new Date().toISOString(),
        });
      } catch (e) { log('  !! 会话 ' + i + ' 读取失败：' + e.message); }
    }
  } finally { c.close(); }
}

(async () => {
  log('=== replies start sites=' + SITES.join(',') + ' max=' + MAX + ' dry=' + DRY);
  try {
    for (const s of SITES) {
      if (s === 'boss') await doBoss();
      else if (s === 'zp') await doZp();
    }
  } catch (e) {
    log('!! 采集异常：' + e.message);
  }
  log('=== replies end 会话 ' + results.length + ' · 有回复 ' + replied + ' · 约面试 ' + interview);
  if (!DRY && results.length) {
    fs.writeFileSync(OUT, results.map((r) => JSON.stringify(r)).join('\n') + '\n', 'utf8');
    log('写入 ' + OUT + '（' + results.length + ' 行）');
  }
  process.exit(0);
})();
