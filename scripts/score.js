// score.js — 岗位匹配打分（四个投递脚本共用）
// 规则来自同目录 rules.json（由 AutoApply 的「匹配规则」页面编辑，随时可改，改完下一轮生效）
// 用法：
//   require('./score.js').evaluate({ title, company, kw }) -> { pass, score, reason, notes }
//   命令行自检：node score.js --title "实施工程师" --company "某某科技" --kw 实施工程师

const fs = require('fs');
const path = require('path');

const ROOT = process.env.AUTOAPPLY_DATA || __dirname;   // 数据目录（多副本共享），脚本仍在自己目录
const RULES_FILE = path.join(ROOT, 'rules.json');

const DEFAULTS = {
  schemaVersion: 1,
  threshold: 55,
  titleMust: true,
  titleInclude: ['实施', '交付', '技术支持', '售后', '运维'],
  titleExclude: ['客服', '销售', '电话销售', '文员', '实习', '兼职', '普工', '司机', '仓管', '保洁', '保安', '收银'],
  companyBlacklist: ['派遣', '外包', '劳务', '人力资源', '人力服务', '众包', '猎头'],
  skillWords: ['实施', '交付', '部署', '运维', '售后', '技术支持', 'MES', 'ERP', 'SaaS', 'WMS', 'CRM', '数据库', 'Linux', '驻场'],
  baseScore: 40,
  includeBonus: 25,
  skillBonus: 4,
  skillBonusCap: 16,
  kwBonus: 8,
  // 正文（JD）规则：只有点了卡片、读到详情正文的站才用得上（BOSS / 智联）
  jdExclude: ['电话销售', '外呼', '电销', '电话邀约', '招生', '课程顾问', '保险代理人', '无底薪', '纯提成'],
  jdInclude: ['MySQL', 'SQL', 'Linux', '数据库', '部署', '驻场', '出差', '售后', '培训', '验收', '实施', '排查', '日志', '服务器'],
  jdBonus: 3,
  jdBonusCap: 12,
};

let cache = null;
let cacheAt = 0;

function loadRules() {
  const now = Date.now();
  if (cache && now - cacheAt < 3000) return cache; // 3 秒缓存，避免每张卡片都读盘
  let r = null;
  try {
    r = JSON.parse(fs.readFileSync(RULES_FILE, 'utf8'));
  } catch (e) {
    r = null;
  }
  // 兼容两种键名：rules.json 可能是小驼峰（手写/Node 写）或 PascalCase（C# 界面写）
  const src = {};
  if (r) for (const k of Object.keys(r)) src[k.charAt(0).toLowerCase() + k.slice(1)] = r[k];
  const out = {};
  for (const k of Object.keys(DEFAULTS)) {
    const v = src[k] !== undefined ? src[k] : DEFAULTS[k];
    out[k] = v;
  }
  // 列表字段做一下清洗：去空、去重、保持顺序
  for (const k of ['titleInclude', 'titleExclude', 'companyBlacklist', 'skillWords', 'jdExclude', 'jdInclude']) {
    const arr = Array.isArray(out[k]) ? out[k] : String(out[k] || '').split(/[,，\s]+/);
    const seen = {};
    out[k] = arr.map((s) => String(s).trim()).filter((s) => {
      if (!s || seen[s]) return false;
      seen[s] = 1;
      return true;
    });
  }
  out.threshold = Number(out.threshold) || DEFAULTS.threshold;
  out.baseScore = Number(out.baseScore);
  out.includeBonus = Number(out.includeBonus);
  out.skillBonus = Number(out.skillBonus);
  out.skillBonusCap = Number(out.skillBonusCap);
  out.kwBonus = Number(out.kwBonus);
  out.jdBonus = Number(out.jdBonus);
  out.jdBonusCap = Number(out.jdBonusCap);
  cache = out;
  cacheAt = now;
  return out;
}

function hit(text, words) {
  const t = String(text || '');
  const found = [];
  for (const w of words) {
    if (w && t.indexOf(w) >= 0) found.push(w);
  }
  return found;
}

function evaluate(job) {
  const r = loadRules();
  const title = String((job && job.title) || '').trim();
  const company = String((job && job.company) || '').trim();
  const kw = String((job && job.kw) || '').trim();
  const notes = [];

  // 1) 公司黑名单：派遣/外包这类直接排除
  const badCompany = hit(company, r.companyBlacklist);
  if (badCompany.length) {
    return { pass: false, score: 0, reason: '公司黑名单：' + badCompany[0], notes: ['公司=' + company] };
  }

  // 2) 标题拿不到（个别站点卡片不给标题）：不拦，只记录
  if (!title) {
    return { pass: true, score: r.baseScore, reason: '无标题信息，放行（未评分）', notes: ['公司=' + company] };
  }

  // 3) 标题排除词
  const badTitle = hit(title, r.titleExclude);
  if (badTitle.length) {
    return { pass: false, score: 0, reason: '标题含排除词：' + badTitle[0], notes: [] };
  }

  // 4) 标题必含词
  const inc = hit(title, r.titleInclude);
  let score = r.baseScore;
  if (inc.length) {
    score += r.includeBonus;
    notes.push('标题命中 ' + inc.join('/'));
  } else if (r.titleMust) {
    return { pass: false, score: score, reason: '标题不含指定词', notes: [] };
  }

  // 5) 技能词加权
  const sk = hit(title, r.skillWords);
  if (sk.length) {
    const add = Math.min(r.skillBonusCap, sk.length * r.skillBonus);
    score += add;
    notes.push('技能词 ' + sk.join('/') + ' +' + add);
  }

  // 6) 正文（JD）闸门：标题里带"实施"、正文其实是电话销售/招生/招商的，在这里拦下
  const jd = String((job && job.jd) || '').replace(/\s+/g, ' ').trim();
  if (jd.length > 0) {
    const badJd = hit(jd, r.jdExclude);
    if (badJd.length) {
      return { pass: false, score: 0, reason: '正文含排除词：' + badJd[0], notes: ['正文=' + jd.slice(0, 50)] };
    }
    const goodJd = hit(jd, r.jdInclude);
    if (goodJd.length) {
      const add = Math.min(r.jdBonusCap, goodJd.length * r.jdBonus);
      score += add;
      notes.push('正文命中 ' + goodJd.join('/') + ' +' + add);
    }
  }

  // 7) 搜索关键词出现在标题里（说明就是为它搜出来的）
  if (kw && title.indexOf(kw) >= 0) {
    score += r.kwBonus;
    notes.push('与关键词一致 +' + r.kwBonus);
  }

  if (score > 100) score = 100;
  const pass = score >= r.threshold;
  return {
    pass,
    score,
    reason: pass ? '通过' : '分数 ' + score + ' < 阈值 ' + r.threshold,
    notes,
  };
}

module.exports = { evaluate, loadRules, DEFAULTS };

if (require.main === module) {
  const a = (n, d) => {
    const i = process.argv.indexOf('--' + n);
    return i < 0 ? d : process.argv[i + 1];
  };
  const res = evaluate({ title: a('title', ''), company: a('company', ''), kw: a('kw', '') });
  if (process.argv.indexOf('--json') >= 0) {
    process.stdout.write(JSON.stringify(res));
  } else {
    console.log(JSON.stringify(res, null, 2));
  }
}
