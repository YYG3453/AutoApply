// 生成投递报告 report.md（汇总 BOSS / 智联 / 应届生 / 前程无忧 四个站点）
const fs = require('fs');
const path = require('path');

const SOURCES = [
  { file: 'state.jsonl', name: 'BOSS直聘' },
  { file: 'state-zp.jsonl', name: '智联招聘' },
  { file: 'state-yjs.jsonl', name: '应届生' },
  { file: 'state-q51.jsonl', name: '前程无忧' },
];

function readRows(f) {
  try {
    return fs.readFileSync(path.join(__dirname, f), 'utf8').split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
  } catch { return []; }
}

let all = [];
const perSite = {};
for (const s of SOURCES) {
  const rows = readRows(s.file).map((r) => Object.assign({}, r, { site: s.name, city: r.city || (r.area ? String(r.area).split(/[·\-\s]/)[0] : ''), company: r.company || r.loc || '', salary: r.salary || '' }));
  perSite[s.name] = rows.length;
  all = all.concat(rows);
}

const byCity = {};
for (const r of all) if (r.city) byCity[r.city] = (byCity[r.city] || 0) + 1;
const byDay = {};
for (const r of all) { const d = new Date(r.ts).toLocaleDateString('zh-CN'); byDay[d] = (byDay[d] || 0) + 1; }

let md = '# 自动投递记录（BOSS / 智联 / 应届生 / 前程无忧）\n\n';
md += '总投递：**' + all.length + '** 个岗位\n\n';
md += '## 按平台\n\n| 平台 | 数量 |\n|---|---|\n';
for (const [k, v] of Object.entries(perSite)) md += `| ${k} | ${v} |\n`;
md += '\n## 按城市\n\n| 城市 | 数量 |\n|---|---|\n';
for (const [k, v] of Object.entries(byCity).sort((a, b) => b[1] - a[1])) md += `| ${k} | ${v} |\n`;
md += '\n## 按日期\n\n| 日期 | 数量 |\n|---|---|\n';
for (const [k, v] of Object.entries(byDay).sort()) md += `| ${k} | ${v} |\n`;
md += '\n## 明细（最近 120 条）\n\n| 时间 | 平台 | 城市 | 岗位 | 公司/地点 | 薪资 |\n|---|---|---|---|---|---|\n';
for (const r of all.sort((a, b) => new Date(a.ts) - new Date(b.ts)).slice(-120).reverse()) {
  const t = new Date(r.ts).toLocaleString('zh-CN', { hour12: false });
  md += `| ${t} | ${r.site} | ${r.city || ''} | ${(r.title || '').replace(/\|/g, '/')} | ${(r.company || '').replace(/\|/g, '/')} | ${(r.salary || '').replace(/\|/g, '/')} |\n`;
}
fs.writeFileSync(path.join(__dirname, 'report.md'), md);
console.log('report.md written, total=' + all.length);
