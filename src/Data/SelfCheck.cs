using System;
using System.Collections.Generic;
using System.IO;

namespace AutoApply
{
    internal class CheckItem
    {
        public bool Ok;
        public string Title = "";
        public string Detail = "";
    }

    /// <summary>
    /// 数据自检：交叉核对几个互相独立的来源，发现"数字对不上"就直接说出来。
    /// 存在的理由很实在——之前我把空的会话字段当成"约面 3 个"报给用户，
    /// 如果当时有一层交叉校验，这个错误在界面上就会被标红。
    /// </summary>
    internal static class SelfCheck
    {
        public static List<CheckItem> Run(RecordStore st, AppCfg cfg)
        {
            var list = new List<CheckItem>();
            if (st == null) return list;

            // 1) 今日投递数 vs runs.jsonl 里今天的合计
            int runSent = 0;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (st.Runs != null)
                for (int i = 0; i < st.Runs.Count; i++)
                {
                    var r = st.Runs[i];
                    if (r.Ts.Date == DateTime.Today) runSent += r.Sent;
                }
            bool ok1 = st.Today == 0 || runSent == 0 || Math.Abs(st.Today - runSent) <= Math.Max(5, st.Today / 5);
            list.Add(new CheckItem
            {
                Ok = ok1,
                Title = "今日投递与运行记录是否一致",
                Detail = "明细记录 " + st.Today + " 条 · 运行日志合计 " + runSent + " 条"
                    + (ok1 ? "" : "（差额过大，可能有批次没写进 runs.jsonl，或历史导入混进了今天）")
            });

            // 2) 会话统计自洽：说"提到面试"，就要真有一行是那个状态
            int interRows = 0;
            if (st.Replies != null) for (int i = 0; i < st.Replies.Count; i++) if (st.Replies[i].Interview) interRows++;
            bool ok2 = st.InterviewCount == interRows;
            list.Add(new CheckItem
            {
                Ok = ok2,
                Title = "提到面试的会话数与明细一致",
                Detail = "统计 " + st.InterviewCount + " 条 · 明细 " + interRows + " 条"
                    + (ok2 ? "" : "（对不上，说明判定和落盘用了两套口径）")
            });

            // 3) 被过滤记录是否都有原因
            int noReason = 0;
            if (st.Skips != null)
                for (int i = 0; i < st.Skips.Count; i++) if ((st.Skips[i].Reason ?? "").Trim().Length == 0) noReason++;
            list.Add(new CheckItem
            {
                Ok = noReason == 0,
                Title = "被过滤的每条都有原因",
                Detail = noReason == 0 ? "全部有原因" : (noReason + " 条没有原因，筛选逻辑可能漏写字段")
            });

            // 4) 今天投出去的记录里，城市是否都解析出来了
            int noCity = 0;
            if (st.All != null)
                for (int i = 0; i < st.All.Count; i++)
                {
                    var j = st.All[i];
                    if (j.Ts.ToString("yyyy-MM-dd") != today) continue;
                    if ((j.City ?? "").Trim().Length == 0) noCity++;
                }
            list.Add(new CheckItem
            {
                Ok = noCity == 0,
                Title = "今日记录的城市字段是否完整",
                Detail = noCity == 0 ? "城市都已解析" : (noCity + " 条城市为空，抓取选择器可能变了")
            });

            // 5) 坏行
            list.Add(new CheckItem
            {
                Ok = st.BadLines == 0,
                Title = "记录文件能否完整解析",
                Detail = st.BadLines == 0 ? "没有无法解析的行" : (st.BadLines + " 行解析失败（已跳过，不影响其它记录）")
            });

            // 6) 启用的站今天一条都没投出去
            if (cfg != null)
            {
                var zero = new List<string>();
                for (int i = 0; i < cfg.Sites.Count; i++)
                {
                    var s = cfg.Sites[i];
                    if (!s.Enabled) continue;
                    if (st.CountToday(s.Key) == 0) zero.Add(s.Name);
                }
                list.Add(new CheckItem
                {
                    Ok = zero.Count == 0,
                    Title = "启用的站今天都有产出",
                    Detail = zero.Count == 0 ? "四站今天都有投放" : ("今天 0 条：" + string.Join("、", zero.ToArray()) + "（去「平台健康」看标签页与登录）")
                });
            }
            return list;
        }

        /// <summary>只返回不通过的项目，供托盘通知使用。</summary>
        public static List<CheckItem> Failures(List<CheckItem> all)
        {
            var bad = new List<CheckItem>();
            if (all == null) return bad;
            for (int i = 0; i < all.Count; i++) if (!all[i].Ok) bad.Add(all[i]);
            return bad;
        }
    }
}
