#!/usr/bin/env node
// 把旧版 run.ps1 的 task.log 回填成 runs.jsonl（运行分析页的历史数据）。
// 旧日志每站跑完会有一行： [2026/11/17 14:39:29] === YJS end stop=time sent=50 skipped=2 errs=0 elapsed=12min
// 用法： node tools/backfill-runs.js [taskLogPath] [runsJsonlPath]
'use strict';

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const taskLog = process.argv[2] || 'D:\\APP\\DeepSeek_WorkSpace\\Project\\_automation\\boss-apply\\task.log';
const outFile = process.argv[3] || path.join(ROOT, 'scripts', 'runs.jsonl');

const SITE = {
  BOSS: { key: 'boss', name: 'BOSS直聘' },
  ZP: { key: 'zp', name: '智联招聘' },
  YJS: { key: 'yjs', name: '应届生求职' },
  Q51: { key: 'q51', name: '前程无忧' }
};

function pad(n) { return n < 10 ? '0' + n : '' + n; }

function iso(y, mo, d, h, mi, s) {
  return y + '-' + pad(mo) + '-' + pad(d) + 'T' + pad(h) + ':' + pad(mi) + ':' + pad(s);
}

function main() {
  if (!fs.existsSync(taskLog)) { console.error('task.log 不存在: ' + taskLog); process.exit(2); }
  let raw = fs.readFileSync(taskLog);
  if (raw.length >= 2 && raw[0] === 0xFF && raw[1] === 0xFE) raw = raw.slice(2);
  let text = raw.toString('utf16le');
  if (text.indexOf('=== ') < 0) text = raw.toString('utf8');
  const lines = text.split(/\r?\n/);

  const rows = [];
  const seen = {};
  // 按"任务日"分批次：相邻两行间隔 > 20 分钟就算新一轮
  let lastEnd = null;
  let runKey = null;
  let seq = 0;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const m = /\[(\d{4})\/(\d{2})\/(\d{2}) (\d{2}):(\d{2}):(\d{2})\]\s*===\s*(?:(BOSS|ZP|YJS|Q51)\s+)?end\b(.*)$/.exec(line);
    if (!m) continue;
    const y = +m[1], mo = +m[2], d = +m[3], h = +m[4], mi = +m[5], s = +m[6];
    const site = SITE[m[7] || 'BOSS'];
    const rest = m[8] || '';
    const stop = ((/(?:^|\s)stop=(\S+)/.exec(rest) || [0, 'time'])[1]) || 'time';
    const sent = +(/(?:^|\s)sent=(\d+)/.exec(rest) || [0, 0])[1];
    const elapsed = +(/(?:^|\s)elapsed=([\d.]+)min/.exec(rest) || [0, 0])[1];
    const endTs = new Date(y, mo - 1, d, h, mi, s);
    if (lastEnd === null || (endTs - lastEnd) > 20 * 60 * 1000) {
      const startTs = new Date(endTs.getTime() - Math.max(1, elapsed) * 60 * 1000);
      runKey = iso(startTs.getFullYear(), startTs.getMonth() + 1, startTs.getDate(),
        startTs.getHours(), startTs.getMinutes(), startTs.getSeconds());
      seq = 0;
    }
    lastEnd = endTs;
    const ts = iso(y, mo, d, h, mi, s);
    const dedupe = runKey + '|' + site.key + '|' + ts;
    if (seen[dedupe]) continue;
    seen[dedupe] = 1;
    rows.push({
      run: runKey,
      ts: ts,
      window: '历史回填',
      site: site.key,
      name: site.name,
      mins: Math.round(Math.max(0.1, elapsed) * 10) / 10,
      sent: sent,
      stop: stop,
      seq: seq++
    });
  }

  if (!rows.length) { console.error('没解析到任何 === <SITE> end 行'); process.exit(1); }

  fs.mkdirSync(path.dirname(outFile), { recursive: true });
  let existing = '';
  const have = {};
  if (fs.existsSync(outFile)) {
    existing = fs.readFileSync(outFile, 'utf8');
    existing.split(/\r?\n/).forEach((l) => {
      if (!l.trim()) return;
      try {
        const o = JSON.parse(l);
        have[o.run + '|' + o.site + '|' + o.ts] = 1;
      } catch (e) { /* 坏行忽略 */ }
    });
  }

  let added = 0;
  let buf = '';
  rows.forEach((r) => {
    const k = r.run + '|' + r.site + '|' + r.ts;
    if (have[k]) return;
    have[k] = 1;
    const o = { run: r.run, ts: r.ts, window: r.window, site: r.site, name: r.name, mins: r.mins, sent: r.sent, stop: r.stop };
    if (r.seq !== undefined) o.seq = r.seq;
    buf += JSON.stringify(o) + '\n';
    added++;
  });

  if (added) {
    if (existing.length && !/\n$/.test(existing)) buf = '\n' + buf;
    fs.appendFileSync(outFile, buf, 'utf8');
  }
  console.log('parsed=' + rows.length + ' added=' + added + ' -> ' + outFile);
}

main();
