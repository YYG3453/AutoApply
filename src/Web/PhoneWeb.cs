using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AutoApply
{
    /// <summary>
    /// 手机 / 平板看板：同一个 Wi-Fi（或有线局域网）下，用手机浏览器就能看四站状态、
    /// 点「全部重开」「开始一轮」「停止」，不用装 App、不用远程桌面、不用第三方服务。
    ///
    /// 为什么不用 HttpListener：它在 Windows 上绑定非 localhost 前缀需要管理员加 urlacl，
    /// 便携版双击就跑不起来。这里直接开 TcpListener 手写一份极简 HTTP（只认 GET/POST），
    /// 普通用户权限即可，和「单 exe、零安装」的约束一致。
    ///
    /// 安全边界（写进 README，别改松）：
    ///   · 只接受私有网段来源（127/8、10/8、172.16/12、192.168/16），其余一律 403；
    ///   · 首次进页面要输 6 位口令（桌面上显示），连错 5 次封 10 分钟；
    ///   · 登录成功后发一个随机 key 的 cookie，口令只用来换这个 cookie；
    ///   · 明文 HTTP：只给家里/办公室局域网用，绝不做公网映射（README 也这么写）。
    /// </summary>
    internal class PhoneWeb
    {
        private readonly MainForm _app;
        private readonly Engine _eng;
        private AppCfg _cfg;

        private TcpListener _srv;
        private Thread _th;
        private volatile bool _stop;

        private readonly Dictionary<string, SiteHealth> _h = new Dictionary<string, SiteHealth>();
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _fails = new Dictionary<string, int>();
        private readonly Dictionary<string, DateTime> _blocked = new Dictionary<string, DateTime>();
        private int _checking;
        private int _acting;

        // Chrome 快照缓存：手机每 5 秒轮询一次，别每次都打三个 CDP 请求
        private bool _chrome;
        private string _chromeVer = "";
        private int _tabCount;
        private DateTime _snapAt = DateTime.MinValue;

        public int Port = 8737;
        public string Url = "";
        public string Pin = "";
        public string Err = "";

        public bool Running { get { return _srv != null && !_stop; } }

        /// <summary>最近一次动作的结果，手机页上显示一行提示。</summary>
        public volatile string LastAct = "";

        public PhoneWeb(MainForm app, Engine eng, AppCfg cfg)
        {
            _app = app;
            _eng = eng;
            _cfg = cfg;
        }

        // ---------------------------------------------------------------- 启停

        public bool Start(out string msg)
        {
            msg = "";
            Err = "";
            if (_cfg == null) { Err = "没有配置"; msg = Err; return false; }
            if (!_cfg.WebEnabled) { Err = "手机页面已在 config.json 里关掉（WebEnabled=false）"; msg = Err; return false; }

            Port = _cfg.WebPort;
            if (Port < 1024 || Port > 65535) Port = 8737;
            if (string.IsNullOrEmpty(_cfg.WebPin) || _cfg.WebPin.Length != 6) _cfg.WebPin = NewPin();
            if (string.IsNullOrEmpty(_cfg.WebKey) || _cfg.WebKey.Length < 32) _cfg.WebKey = NewKey();
            Pin = _cfg.WebPin;
            SaveQuiet();

            try
            {
                _srv = new TcpListener(IPAddress.Any, Port);
                _srv.Start(32);
            }
            catch (Exception ex)
            {
                Err = "端口 " + Port + " 起不来（可能被别的程序占用）：" + ex.Message;
                msg = Err;
                try { if (_srv != null) _srv.Stop(); } catch { }
                _srv = null;
                return false;
            }

            Url = "http://" + LanIp() + ":" + Port;
            _stop = false;
            _th = new Thread(Loop);
            _th.IsBackground = true;
            _th.Name = "phone-web";
            _th.Start();
            Program.Trace("phone web: " + Url + "  pin=" + Pin);
            return true;
        }

        public void Stop()
        {
            _stop = true;
            try { if (_srv != null) _srv.Stop(); } catch { }
            _srv = null;
        }

        /// <summary>换口令 + 换会话密钥：已经登录过的手机需要重新输入新口令。</summary>
        public string Regenerate()
        {
            _cfg.WebPin = NewPin();
            _cfg.WebKey = NewKey();
            Pin = _cfg.WebPin;
            SaveQuiet();
            _fails.Clear();
            _blocked.Clear();
            return Pin;
        }

        private void SaveQuiet()
        {
            try { _cfg.Save(Paths.CfgPath); }
            catch (Exception ex) { Program.Trace("phone web: 保存口令失败 " + ex.Message); }
        }

        private static string NewPin()
        {
            var r = new Random(Guid.NewGuid().GetHashCode());
            var sb = new StringBuilder(6);
            // 首位不给 0，方便念/输
            sb.Append((char)('1' + r.Next(9)));
            for (int i = 1; i < 6; i++) sb.Append((char)('0' + r.Next(10)));
            return sb.ToString();
        }

        private static string NewKey()
        {
            var b = new byte[16];
            new Random(Guid.NewGuid().GetHashCode()).NextBytes(b);
            var sb = new StringBuilder(32);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>本机的局域网 IPv4：跳过回环、虚拟网卡，优先 Wi-Fi / 有线。</summary>
        public static string LanIp()
        {
            string best = "", bestKind = "";
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    if (desc.IndexOf("vmware") >= 0 || desc.IndexOf("virtualbox") >= 0 ||
                        desc.IndexOf("hyper-v") >= 0 || desc.IndexOf("loopback") >= 0 ||
                        desc.IndexOf("tap") >= 0 || desc.IndexOf("vpn") >= 0) continue;
                    bool wired = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                 ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = a.GetAddressBytes();
                        if (b.Length != 4) continue;
                        bool priv = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        if (!priv) continue;
                        if (best.Length == 0 || (wired && bestKind != "wired"))
                        {
                            best = a.ToString();
                            bestKind = wired ? "wired" : "other";
                        }
                    }
                }
            }
            catch { }
            return best.Length > 0 ? best : "127.0.0.1";
        }

        // ---------------------------------------------------------------- 收连接

        private void Loop()
        {
            while (!_stop)
            {
                Socket s = null;
                try { s = _srv.AcceptSocket(); }
                catch (Exception)
                {
                    if (_stop) break;
                    Thread.Sleep(60);
                    continue;
                }
                var sock = s;
                ThreadPool.QueueUserWorkItem(delegate { HandleSafe(sock); });
            }
        }

        private void HandleSafe(Socket sock)
        {
            try { Handle(sock); }
            catch (Exception ex) { Program.Trace("phone web: " + ex.GetType().Name + " " + ex.Message); }
            finally { try { sock.Close(); } catch { } }
        }

        private void Handle(Socket sock)
        {
            try { sock.ReceiveTimeout = 4000; sock.SendTimeout = 8000; } catch { }

            string ip = "?";
            try { ip = ((IPEndPoint)sock.RemoteEndPoint).Address.ToString(); } catch { }

            using (var ns = new NetworkStream(sock, false))
            {
                // 读头 + 可能已经一起到的正文
                var all = new MemoryStream();
                var tmp = new byte[4096];
                int headEnd = -1;
                while (headEnd < 0 && all.Length < 32768)
                {
                    int n;
                    try { n = ns.Read(tmp, 0, tmp.Length); }
                    catch { break; }
                    if (n <= 0) break;
                    all.Write(tmp, 0, n);
                    headEnd = HeaderEnd(all.ToArray());
                }
                if (headEnd < 0) return;

                byte[] raw = all.ToArray();
                string head = Encoding.UTF8.GetString(raw, 0, headEnd);
                var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) return;
                var first = lines[0].Split(' ');
                if (first.Length < 3)
                {
                    Write(ns, 400, "Bad Request", "text/plain; charset=utf-8", B("bad request"), null);
                    return;
                }
                string method = first[0].ToUpperInvariant();
                string target = first[1];
                if (method != "GET" && method != "POST")
                {
                    Write(ns, 405, "Method Not Allowed", "text/plain; charset=utf-8", B("only GET/POST"), null);
                    return;
                }

                // 只给局域网用：公网 / 其它网段一律拒绝
                if (!IsLan(ip))
                {
                    Write(ns, 403, "Forbidden", "text/plain; charset=utf-8", B("只允许局域网访问"), null);
                    return;
                }

                var hdr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    var ln = lines[i];
                    if (ln.Length == 0) break;
                    int c = ln.IndexOf(':');
                    if (c <= 0) continue;
                    hdr[ln.Substring(0, c).Trim()] = ln.Substring(c + 1).Trim();
                }

                // 正文：先吃掉已读到的，再按 Content-Length 补齐
                int bodyLen = 0;
                string cl;
                if (hdr.TryGetValue("content-length", out cl)) int.TryParse(cl, out bodyLen);
                if (bodyLen < 0) bodyLen = 0;
                if (bodyLen > 65536) bodyLen = 0;      // 本服务不接大请求
                var bodyBytes = new MemoryStream();
                int start = headEnd + 4;
                if (start < raw.Length) bodyBytes.Write(raw, start, raw.Length - start);
                while (bodyLen > 0 && bodyBytes.Length < bodyLen)
                {
                    int n;
                    try { n = ns.Read(tmp, 0, Math.Min(tmp.Length, bodyLen - (int)bodyBytes.Length)); }
                    catch { break; }
                    if (n <= 0) break;
                    bodyBytes.Write(tmp, 0, n);
                }
                string body = bodyBytes.Length > 0 ? Encoding.UTF8.GetString(bodyBytes.ToArray(), 0, (int)Math.Min(bodyBytes.Length, Math.Max(bodyLen, 0) == 0 ? bodyBytes.Length : bodyLen)) : "";

                string path = target, query = "";
                int qi = target.IndexOf('?');
                if (qi >= 0) { path = target.Substring(0, qi); query = target.Substring(qi + 1); }
                var q = ParseForm(query);

                if (path == "/favicon.ico")
                {
                    Write(ns, 204, "No Content", "image/x-icon", new byte[0], null);
                    return;
                }

                if (path == "/login")
                {
                    if (method == "GET") { Write(ns, 200, "OK", "text/html; charset=utf-8", B(LoginHtml("")), null); return; }
                    DoLogin(ns, ip, ParseForm(body));
                    return;
                }

                // 除登录页外都要 cookie
                string key = Cookie(hdr, "aakey");
                if (!SafeEq(key, _cfg.WebKey))
                {
                    if (path == "/" || path.StartsWith("/index"))
                    {
                        Write(ns, 200, "OK", "text/html; charset=utf-8", B(LoginHtml("")), null);
                        return;
                    }
                    WriteJson(ns, 401, "{\"ok\":false,\"msg\":\"请先输入电脑上显示的 6 位口令\"}");
                    return;
                }

                if (path == "/" || path.StartsWith("/index"))
                {
                    Write(ns, 200, "OK", "text/html; charset=utf-8", B(MainHtml()), null);
                    return;
                }
                if (path == "/api/state") { WriteJson(ns, 200, StateJson()); return; }
                if (path == "/api/action")
                {
                    if (method != "POST") { WriteJson(ns, 405, "{\"ok\":false,\"msg\":\"动作要用 POST\"}"); return; }
                    string a = Param(q, body, "a");
                    WriteJson(ns, 200, Act(a));
                    return;
                }
                Write(ns, 404, "Not Found", "text/plain; charset=utf-8", B("not found"), null);
            }
        }

        /// <summary>找头结束位置（\r\n\r\n），找不到返回 -1。</summary>
        private static int HeaderEnd(byte[] b)
        {
            for (int i = 0; i + 3 < b.Length; i++)
                if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
            return -1;
        }

        private void DoLogin(NetworkStream ns, string ip, Dictionary<string, string> form)
        {
            DateTime until;
            if (_blocked.TryGetValue(ip, out until) && until > DateTime.Now)
            {
                Write(ns, 200, "OK", "text/html; charset=utf-8", B(LoginHtml("试错太多次了，10 分钟后再来。")), null);
                return;
            }
            string pin = Param(form, "", "pin").Trim().Replace(" ", "");
            if (!SafeEq(pin, _cfg.WebPin))
            {
                int n;
                _fails.TryGetValue(ip, out n);
                n++;
                _fails[ip] = n;
                if (n >= 5) { _blocked[ip] = DateTime.Now.AddMinutes(10); _fails[ip] = 0; }
                Write(ns, 200, "OK", "text/html; charset=utf-8",
                      B(LoginHtml(n >= 5 ? "连错 5 次，锁 10 分钟。口令在电脑的「平台健康」页底部。"
                                        : "口令不对，看一下电脑屏幕上显示的 6 位数字。")), null);
                return;
            }
            _fails.Remove(ip);
            string cookie = "aakey=" + _cfg.WebKey + "; Path=/; Max-Age=31536000; HttpOnly; SameSite=Lax";
            Program.Trace("phone web: " + ip + " 登录成功");
            Write(ns, 302, "Found", "text/plain; charset=utf-8", new byte[0], "Location: /\r\nSet-Cookie: " + cookie + "\r\n");
        }

        // ---------------------------------------------------------------- 状态

        private void Snap()
        {
            if ((DateTime.Now - _snapAt).TotalSeconds < 2.5) return;
            bool alive = false;
            string ver = "";
            int tabs = 0;
            try
            {
                alive = Health.ChromeAlive();
                if (alive)
                {
                    ver = Health.ChromeVersion();
                    tabs = Health.Tabs().Count;
                }
            }
            catch { }
            _chrome = alive;
            _chromeVer = ver;
            _tabCount = tabs;
            _snapAt = DateTime.Now;
        }

        private string StateJson()
        {
            Snap();
            var d = new Dictionary<string, object>();
            d["ok"] = true;
            d["ver"] = MainForm.Version;
            d["running"] = _eng.Running;
            d["stage"] = _eng.Stage == null ? "" : _eng.Stage;
            d["site"] = _eng.CurrentSiteName == null ? "" : _eng.CurrentSiteName;
            d["sent"] = _eng.TotalSentInRun();
            d["chrome"] = _chrome;
            d["chromeVer"] = _chromeVer;
            d["tabs"] = _tabCount;
            int today = 0, issues = 0;
            try { if (_app.Records != null) today = _app.Records.Today; } catch { }
            var list = new List<object>();
            for (int i = 0; i < _cfg.Sites.Count; i++)
            {
                var s = _cfg.Sites[i];
                SiteHealth h = null;
                lock (_h) _h.TryGetValue(s.Key, out h);
                int st = 0;
                try { if (_app.Records != null) st = _app.Records.CountToday(s.Key); } catch { }
                if (h != null && h.Note != "正常" && h.Note != "没有标签页") issues++;
                var o = new Dictionary<string, object>();
                o["key"] = s.Key;
                o["name"] = s.Name;
                o["enabled"] = s.Enabled;
                o["tabs"] = h == null ? 0 : h.Tabs;
                o["login"] = h == null ? "unknown" : h.Login;
                o["captcha"] = h != null && h.Captcha;
                o["note"] = h == null ? (_checking != 0 ? "检查中…" : "还没检查") : h.Note;
                o["checked"] = h == null ? "" : h.Checked;
                o["today"] = st;
                o["lastRound"] = LastRound(s.Key);
                list.Add(o);
            }
            d["sites"] = list;
            d["issues"] = issues;
            d["today"] = today;
            d["checking"] = _checking != 0;
            d["url"] = Url;
            d["log"] = TailLog(7);
            d["msg"] = LastAct ?? "";
            return new JavaScriptSerializer().Serialize(d);
        }

        private string LastRound(string key)
        {
            try
            {
                if (_app.Records == null || _app.Records.LastRun == null) return "今天还没跑";
                for (int i = 0; i < _app.Records.LastRun.Count; i++)
                {
                    var r = _app.Records.LastRun[i];
                    if (r.SiteKey != key) continue;
                    return r.Sent + " 条 · " + r.StopText;
                }
            }
            catch { }
            return "本轮没跑";
        }

        private string[] TailLog(int n)
        {
            var list = new List<string>();
            try
            {
                string p = Path.Combine(_eng.BaseDir, "autoapply.log");
                if (!File.Exists(p)) return new string[0];
                var fi = new FileInfo(p);
                long from = Math.Max(0, fi.Length - 6144);
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(from, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string content = sr.ReadToEnd();
                        var lines = content.Split('\n');
                        for (int i = Math.Max(0, lines.Length - n - 1); i < lines.Length; i++)
                        {
                            string t = lines[i].TrimEnd('\r');
                            if (t.Trim().Length == 0) continue;
                            list.Add(t.Length > 120 ? t.Substring(0, 120) + "…" : t);
                        }
                    }
                }
            }
            catch { }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- 动作

        private string Act(string a)
        {
            if (a == "check") { StartCheck(); return "{\"ok\":true,\"msg\":\"正在检查四个站点…\"}"; }
            if (a == "reopenall") { StartReopenAll(); return "{\"ok\":true,\"msg\":\"正在重开标签页，约 30 秒\"}"; }
            if (a == "start")
            {
                if (_eng.Running) return "{\"ok\":false,\"msg\":\"已经在跑了\"}";
                try
                {
                    var list = new List<SiteCfg>();
                    foreach (var s in _cfg.Sites) if (s.Enabled) list.Add(s);
                    if (list.Count == 0) return "{\"ok\":false,\"msg\":\"四个站都被停用了\"}";
                    _app.BeginInvoke(new Action(delegate
                    {
                        try { _app.StartRunSites(list, "手机触发", false); }
                        catch (Exception ex) { Program.Trace("phone start throw: " + ex.Message); }
                    }));
                    LastAct = "手机点了「开始一轮」";
                    return "{\"ok\":true,\"msg\":\"已让电脑开始一轮\"}";
                }
                catch (Exception ex) { return "{\"ok\":false,\"msg\":\"发不出去：" + Esc(ex.Message) + "\"}"; }
            }
            if (a == "stop")
            {
                try
                {
                    _app.BeginInvoke(new Action(delegate { try { _app.StopRun(); } catch { } }));
                    LastAct = "手机点了「停止」";
                    return "{\"ok\":true,\"msg\":\"已让电脑停下\"}";
                }
                catch (Exception ex) { return "{\"ok\":false,\"msg\":\"发不出去：" + Esc(ex.Message) + "\"}"; }
            }
            return "{\"ok\":false,\"msg\":\"不认识的动作\"}";
        }

        private void StartCheck()
        {
            if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Health.Port = _cfg.Port;
                    Health.ScriptDir = _eng.ScriptDir;
                    Health.BinDir = _eng.BinDir;
                    _snapAt = DateTime.MinValue;
                    Snap();
                    foreach (var s in _cfg.Sites)
                    {
                        if (!s.Enabled) continue;
                        SiteHealth h;
                        try { h = Health.Probe(s); }
                        catch (Exception ex) { h = new SiteHealth(); h.Key = s.Key; h.Name = s.Name; h.Note = "检查出错：" + ex.Message; }
                        lock (_h) _h[s.Key] = h;
                    }
                    LastAct = "检查完成 " + DateTime.Now.ToString("HH:mm:ss");
                }
                finally { Volatile.Write(ref _checking, 0); }
            });
        }

        private void StartReopenAll()
        {
            if (Interlocked.CompareExchange(ref _acting, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string msg;
                    if (!_eng.EnsureChrome(out msg))
                    {
                        LastAct = "浏览器没起来：" + msg;
                        return;
                    }
                    Health.Port = _cfg.Port;
                    Health.ScriptDir = _eng.ScriptDir;
                    Health.BinDir = _eng.BinDir;
                    for (int i = 0; i < _cfg.Sites.Count; i++)
                    {
                        var s = _cfg.Sites[i];
                        if (!s.Enabled) continue;
                        try { Health.Reopen(s); }
                        catch (Exception ex) { Program.Trace("phone reopen " + s.Key + ": " + ex.Message); }
                    }
                    LastAct = "全部重开完成 " + DateTime.Now.ToString("HH:mm:ss");
                }
                finally { Volatile.Write(ref _acting, 0); }
                StartCheck();
            });
        }

        // ---------------------------------------------------------------- HTTP 细节

        private static byte[] B(string s) { return Encoding.UTF8.GetBytes(s); }

        private static void Write(NetworkStream ns, int code, string reason, string ctype, byte[] body, string extra)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            sb.Append("Content-Type: ").Append(ctype).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("X-Content-Type-Options: nosniff\r\n");
            sb.Append("Referrer-Policy: no-referrer\r\n");
            sb.Append("Connection: close\r\n");
            if (!string.IsNullOrEmpty(extra)) sb.Append(extra);
            sb.Append("\r\n");
            byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
            ns.Write(head, 0, head.Length);
            if (body.Length > 0) ns.Write(body, 0, body.Length);
            ns.Flush();
        }

        private static void WriteJson(NetworkStream ns, int code, string json)
        {
            Write(ns, code, code == 200 ? "OK" : "Unauthorized", "application/json; charset=utf-8", B(json), null);
        }

        private static bool SafeEq(string a, string b)
        {
            if (a == null || b == null) return false;
            int d = a.Length ^ b.Length;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        private static bool IsLan(string ip)
        {
            IPAddress a;
            if (!IPAddress.TryParse(ip, out a)) return false;
            if (IPAddress.IsLoopback(a)) return true;
            byte[] b = a.GetAddressBytes();
            if (b.Length != 4) return false;   // IPv6 暂不放行
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        private static string Cookie(Dictionary<string, string> hdr, string name)
        {
            string c;
            if (!hdr.TryGetValue("cookie", out c) || c == null) return "";
            var parts = c.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return p.Substring(name.Length + 1);
            }
            return "";
        }

        private static string Param(Dictionary<string, string> form, string body, string name)
        {
            if (form != null)
            {
                string v;
                if (form.TryGetValue(name, out v)) return v;
            }
            if (!string.IsNullOrEmpty(body))
            {
                var f = ParseForm(body);
                string v;
                if (f.TryGetValue(name, out v)) return v;
            }
            return "";
        }

        private static Dictionary<string, string> ParseForm(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(s)) return d;
            var parts = s.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                int e = parts[i].IndexOf('=');
                string k = e < 0 ? parts[i] : parts[i].Substring(0, e);
                string v = e < 0 ? "" : parts[i].Substring(e + 1);
                try
                {
                    k = Uri.UnescapeDataString(k.Replace("+", " "));
                    v = Uri.UnescapeDataString(v.Replace("+", " "));
                }
                catch { }
                if (!d.ContainsKey(k)) d[k] = v;
            }
            return d;
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        // ---------------------------------------------------------------- 页面

        private static string MainHtml()
        {
            try
            {
                using (var st = typeof(PhoneWeb).Assembly.GetManifestResourceStream("AutoApply.phone.html"))
                {
                    if (st != null)
                        using (var sr = new StreamReader(st, Encoding.UTF8))
                            return sr.ReadToEnd();
                }
            }
            catch (Exception ex) { Program.Trace("phone web: 读页面模板失败 " + ex.Message); }
            return "<!doctype html><meta charset=utf-8><p>页面模板缺失，请重新构建程序。</p>";
        }

        private static string LoginHtml(string err)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=zh-CN><head><meta charset=utf-8>");
            sb.Append("<meta name=viewport content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>AutoApply</title><style>");
            sb.Append("body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#0d1117;color:#e6edf3;font:16px/1.6 -apple-system,'PingFang SC','Microsoft YaHei',sans-serif}");
            sb.Append("form{width:min(360px,86vw);background:#161b22;border:1px solid #232b36;border-radius:16px;padding:26px 22px}");
            sb.Append("h1{font-size:19px;margin:0 0 6px}p{font-size:13px;color:#9aa7b4;margin:0 0 16px}");
            sb.Append("input{width:100%;box-sizing:border-box;background:#0d1117;border:1px solid #2b3542;border-radius:12px;color:#e6edf3;font-size:26px;letter-spacing:10px;text-align:center;padding:12px;font-family:ui-monospace,Consolas,monospace}");
            sb.Append("button{width:100%;margin-top:14px;padding:14px;border-radius:12px;border:1px solid rgba(52,211,153,.5);background:rgba(52,211,153,.14);color:#34d399;font-size:16px;font-weight:600}");
            sb.Append(".err{color:#f59e0b;font-size:13px;margin-top:12px;min-height:20px}");
            sb.Append("</style></head><body><form method=post action=/login>");
            sb.Append("<h1>AutoApply</h1><p>输入电脑「平台健康」页底部显示的 6 位口令</p>");
            sb.Append("<input name=pin id=pin inputmode=numeric pattern=\"[0-9]*\" maxlength=6 autocomplete=off autofocus>");
            sb.Append("<button type=submit>进入</button>");
            sb.Append("<div class=err>").Append(err == null ? "" : err).Append("</div></form></body></html>");
            return sb.ToString();
        }
    }
}
