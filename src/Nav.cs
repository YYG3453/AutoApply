using System;
using System.Collections.Generic;

namespace AutoApply
{
    /// <summary>一个导航项：页面 key、标题、所属分组、以及"怎么造这个页面"。</summary>
    internal class NavItem
    {
        public string Key;
        public string Title;
        public string Group;
        public Func<MainForm, Page> Create;
    }

    /// <summary>
    /// 导航注册表。加一个新页面 = 在这里加一行，导航栏、路由、--page 参数全都自动生效。
    /// 以前加页面要同时改 Ui.cs 的数组、MainForm 的字段和 ShowPage，容易漏。
    /// </summary>
    internal static class Nav
    {
        public static readonly List<NavItem> Items = new List<NavItem>
        {
            new NavItem { Key = "dash",    Title = "仪表盘",      Group = "概览", Create = delegate(MainForm f) { return new DashboardPage(f); } },
            new NavItem { Key = "records", Title = "投递明细",    Group = "概览", Create = delegate(MainForm f) { return new RecordsPage(f); } },
            new NavItem { Key = "plan",    Title = "投递计划",    Group = "投递", Create = delegate(MainForm f) { return new PlanPage(f); } },
            new NavItem { Key = "sites",   Title = "平台与关键词", Group = "投递", Create = delegate(MainForm f) { return new SitesPage(f); } },
            new NavItem { Key = "rules",   Title = "匹配规则",    Group = "投递", Create = delegate(MainForm f) { return new RulesPage(f); } },
            new NavItem { Key = "greet",   Title = "招呼语版本",  Group = "投递", Create = delegate(MainForm f) { return new GreetPage(f); } },
            new NavItem { Key = "filtered",Title = "被过滤",      Group = "投递", Create = delegate(MainForm f) { return new FilteredPage(f); } },
            new NavItem { Key = "analyze", Title = "运行分析",    Group = "投递", Create = delegate(MainForm f) { return new AnalyzePage(f); } },
            new NavItem { Key = "health",Title = "平台健康",    Group = "系统", Create = delegate(MainForm f) { return new HealthPage(f); } },
            new NavItem { Key = "log",     Title = "运行日志",    Group = "系统", Create = delegate(MainForm f) { return new LogPage(f); } },
            new NavItem { Key = "selfcheck",Title = "数据自检",  Group = "系统", Create = delegate(MainForm f) { return new SelfCheckPage(f); } },
            new NavItem { Key = "about",   Title = "关于",        Group = "系统", Create = delegate(MainForm f) { return new AboutPage(f); } },
        };

        public static string DefaultKey { get { return Items.Count > 0 ? Items[0].Key : "dash"; } }

        public static NavItem ByKey(string key)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Key == key) return Items[i];
            return null;
        }

        public static bool Contains(string key) { return ByKey(key) != null; }
    }
}
