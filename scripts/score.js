// score.js — 岗位匹配打分（四个投递脚本共用）
// 规则来自同目录 rules.json（由 AutoApply 的「匹配规则」页面编辑，随时可改，改完下一轮生效）
// 用法：
//   require('./score.js').evaluate({ title, company, kw, city, salary, info, tags, jd }) -> { pass, score, reason, notes }
//   命令行自检：node score.js --title "实施工程师" --company "某某科技" --kw 实施工程师

const fs = require('fs');
const path = require('path');

const ROOT = process.env.AUTOAPPLY_DATA || __dirname;   // 数据目录（多副本共享），脚本仍在自己目录
const RULES_FILE = path.join(ROOT, 'rules.json');

const DEFAULTS = {
  schemaVersion: 2,
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
  // v1.10.0：学历 / 经验 / 福利 / 城市 / 薪资（在卡片全文上判断，命中即跳过）
  eduExclude: ['本科', '硕士', '博士', '研究生'],
  expExclude: ['5-10年', '5年以上', '10年以上'],
  welfareExclude: ['单休', '大小周', '996', '夜班', '倒班'],
  welfareInclude: ['五险一金', '双休', '包吃', '包住', '年终奖', '13薪'],
  welfareBonus: 3,
  welfareBonusCap: 9,
  cityAllow: [],       // 只投这些城市；空 = 不限
  cityDeny: [],        // 命中就跳过
  salaryMinK: 0,       // 职位薪资上限低于它 → 跳过（K/月；0 = 不限）
  salaryMaxK: 0,       // 职位薪资下限高于它 → 跳过（K/月；0 = 不限）
};

const LIST_KEYS = [
  'titleInclude', 'titleExclude', 'companyBlacklist', 'skillWords', 'jdExclude', 'jdInclude',
  'eduExclude', 'expExclude', 'welfareExclude', 'welfareInclude', 'cityAllow', 'cityDeny',
];

let cache = null;
let cacheAt = 0;

function loadRules() {
  const now = Date.now();
  if (cache && now - cacheAt < 3000) return cache; // 3 秒缓存，避免每张卡片都读盘
  let r = null;
  try {
    r = JSON.parse(fs.readFileSync(RULES_FILE, 'utf8').replace(/^\uFEFF/, ''));
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
  for (const k of LIST_KEYS) {
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
  out.welfareBonus = Number(out.welfareBonus) || DEFAULTS.welfareBonus;
  out.welfareBonusCap = Number(out.welfareBonusCap) || DEFAULTS.welfareBonusCap;
  out.salaryMinK = Number(out.salaryMinK) || 0;
  out.salaryMaxK = Number(out.salaryMaxK) || 0;
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

// 把「8-12K」「8千-1.2万」「15-20万/年」「120-150元/天」「面议」换算成 K/月
// 返回 { lo, hi } 或 null（无法判断 → 放行）
function parseSalaryK(s) {
  const raw0 = String(s || '');
  const raw = raw0.replace(/[\s\u00a0]/g, '');
  if (!raw) return null;
  if (/面议|另议|待定|保密|不限/.test(raw)) return null;
  const t = raw.replace(/元/g, '');
  const unit = (u) => (u === '万' ? 10 : (u === '千' || u === 'K' || u === 'k') ? 1 : NaN);
  let lo, hi, uL = '', uR = '';
  let m = t.match(/(\d+(?:\.\d+)?)(万|千|K|k)?[-~—–至到](\d+(?:\.\d+)?)(万|千|K|k)?/);
  if (m) {
    lo = parseFloat(m[1]); uL = m[2] || '';
    hi = parseFloat(m[3]); uR = m[4] || '';
  } else {
    m = t.match(/(\d+(?:\.\d+)?)(万|千|K|k)?/);
    if (!m) return null;
    lo = hi = parseFloat(m[1]); uL = uR = m[2] || '';
  }
  const u = uR || uL;   // 单位只写一次时按右侧缺省
  const conv = (v, uu) => {
    const k = unit(uu || u);
    return isNaN(k) ? v : v * k;   // 没写单位按 K 处理
  };
  lo = conv(lo, uL); hi = conv(hi, uR);
  if (lo > hi) { const tmp = lo; lo = hi; hi = tmp; }
  const perDay = /日薪/.test(raw) || (/元/.test(raw) && /[天日]/.test(raw));
  const perYear = /年/.test(raw) && !/年终|年假/.test(raw);
  if (perDay) { lo = lo * 21.75 / 1000; hi = hi * 21.75 / 1000; }   // 日薪 × 21.75 天 / 1000
  else if (perYear) { lo = lo / 12; hi = hi / 12; }                  // 年薪 → 月薪
  if (!(hi > 0)) return null;
  return { lo, hi, raw };
}

function evaluate(job) {
  const r = loadRules();
  const title = String((job && job.title) || '').trim();
  const company = String((job && job.company) || '').trim();
  const kw = String((job && job.kw) || '').trim();
  const city = String((job && job.city) || '').trim();
  const salary = String((job && job.salary) || '').trim();
  const cardText = String((job && job.info) || '').replace(/\s+/g, ' ').trim();
  const tags = Array.isArray(job && job.tags) ? job.tags.join(' ') : String((job && job.tags) || '');
  const jd = String((job && job.jd) || '').replace(/\s+/g, ' ').trim();
  const info = [title, cardText, tags, jd].join(' ');
  const notes = [];

  // 1) 公司黑名单：派遣/外包这类直接排除
  const badCompany = hit(company, r.companyBlacklist);
  if (badCompany.length) {
    return { pass: false, score: 0, reason: '公司黑名单：' + badCompany[0], notes: ['公司=' + company] };
  }

  // 2) 城市：只投列表非空时，城市必须命中其一；城市排除词命中即跳过
  if (r.cityAllow.length) {
    const ok = city && r.cityAllow.some((c) => c && (city.indexOf(c) >= 0 || c.indexOf(city) >= 0));
    if (!ok) {
      return { pass: false, score: 0, reason: '城市不在允许列表：' + (city || '未知'), notes: ['城市=' + (city || '?')] };
    }
  }
  if (city) {
    const denyCity = hit(city, r.cityDeny);
    if (denyCity.length) {
      return { pass: false, score: 0, reason: '城市排除：' + denyCity[0], notes: ['城市=' + city] };
    }
  }

  // 3) 薪资：只看卡片上写明的区间，面议/解析不了就放行
  const sal = parseSalaryK(salary);
  if (sal) {
    if (r.salaryMinK > 0 && sal.hi < r.salaryMinK) {
      return { pass: false, score: 0, reason: '薪资上限低于 ' + r.salaryMinK + 'K：' + salary, notes: [] };
    }
    if (r.salaryMaxK > 0 && sal.lo > r.salaryMaxK) {
      return { pass: false, score: 0, reason: '薪资下限高于 ' + r.salaryMaxK + 'K：' + salary, notes: [] };
    }
  }

  // 4) 学历 / 经验 / 福利硬门槛：在卡片全文（标题+卡片文字+标签+正文）上找，命中就跳过
  const badEdu = hit(info, r.eduExclude);
  if (badEdu.length) {
    return { pass: false, score: 0, reason: '学历要求：' + badEdu[0], notes: ['卡片=' + cardText.slice(0, 40)] };
  }
  const badExp = hit(info, r.expExclude);
  if (badExp.length) {
    return { pass: false, score: 0, reason: '经验要求：' + badExp[0], notes: ['卡片=' + cardText.slice(0, 40)] };
  }
  const badWelfare = hit(info, r.welfareExclude);
  if (badWelfare.length) {
    return { pass: false, score: 0, reason: '福利含排除项：' + badWelfare[0], notes: ['卡片=' + cardText.slice(0, 40)] };
  }

  // 5) 标题拿不到（个别站点卡片不给标题）：不拦，只记录
  if (!title) {
    return { pass: true, score: r.baseScore, reason: '无标题信息，放行（未评分）', notes: ['公司=' + company] };
  }

  // 6) 标题排除词
  const badTitle = hit(title, r.titleExclude);
  if (badTitle.length) {
    return { pass: false, score: 0, reason: '标题含排除词：' + badTitle[0], notes: [] };
  }

  // 7) 标题必含词
  const inc = hit(title, r.titleInclude);
  let score = r.baseScore;
  if (inc.length) {
    score += r.includeBonus;
    notes.push('标题命中 ' + inc.join('/'));
  } else if (r.titleMust) {
    return { pass: false, score: score, reason: '标题不含指定词', notes: [] };
  }

  // 8) 技能词加权
  const sk = hit(title, r.skillWords);
  if (sk.length) {
    const add = Math.min(r.skillBonusCap, sk.length * r.skillBonus);
    score += add;
    notes.push('技能词 ' + sk.join('/') + ' +' + add);
  }

  // 9) 福利加分（五险一金 / 双休 / 包吃住 …）
  const wf = hit(info, r.welfareInclude);
  if (wf.length) {
    const add = Math.min(r.welfareBonusCap, wf.length * r.welfareBonus);
    score += add;
    notes.push('福利 ' + wf.join('/') + ' +' + add);
  }

  // 10) 正文（JD）闸门：标题里带"实施"、正文其实是电话销售/招生/招商的，在这里拦下
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

  // 11) 搜索关键词出现在标题里（说明就是为它搜出来的）
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

module.exports = { evaluate, loadRules, parseSalaryK, DEFAULTS };

if (require.main === module) {
  const a = (n, d) => {
    const i = process.argv.indexOf('--' + n);
    return i < 0 ? d : process.argv[i + 1];
  };
  const res = evaluate({
    title: a('title', ''),
    company: a('company', ''),
    kw: a('kw', ''),
    city: a('city', ''),
    salary: a('salary', ''),
    info: a('info', ''),
  });
  if (process.argv.indexOf('--json') >= 0) {
    process.stdout.write(JSON.stringify(res));
  } else if (process.argv.indexOf('--salary') >= 0 && !a('title', '')) {
    console.log(JSON.stringify(parseSalaryK(a('salary', ''))));
  } else {
    console.log(JSON.stringify(res, null, 2));
  }
}
