import ctypes, json, os, sys, time, urllib.request, urllib.error

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = "YYG3453/AutoApply"
VER = (sys.argv[1] if len(sys.argv) > 1 else "1.8.9").lstrip("v")
TAG = "v" + VER
ZIP = os.path.join(HERE, "..", "dist", "AutoApply-%s-win-x64.zip" % VER)
ASSET = "AutoApply-%s-win-x64.zip" % VER


class CREDENTIAL(ctypes.Structure):
    _fields_ = [("Flags", ctypes.c_uint32), ("Type", ctypes.c_uint32),
                ("TargetName", ctypes.c_wchar_p), ("Comment", ctypes.c_wchar_p),
                ("LastWritten", ctypes.c_byte * 8), ("CredentialBlobSize", ctypes.c_uint32),
                ("CredentialBlob", ctypes.c_void_p), ("Persist", ctypes.c_uint32),
                ("AttributeCount", ctypes.c_uint32), ("Attributes", ctypes.c_void_p),
                ("TargetAlias", ctypes.c_wchar_p), ("UserName", ctypes.c_wchar_p)]


def stored_token(target):
    adv = ctypes.WinDLL("advapi32")
    ptr = ctypes.c_void_p()
    if not adv.CredReadW(ctypes.c_wchar_p(target), 1, 0, ctypes.byref(ptr)):
        return None
    try:
        c = ctypes.cast(ptr, ctypes.POINTER(CREDENTIAL)).contents
        if not c.CredentialBlobSize:
            return None
        return ctypes.wstring_at(c.CredentialBlob, c.CredentialBlobSize // 2)
    finally:
        adv.CredFree(ptr)


TOKEN = None
for target in ("git:https://github.com", "git:https://YYG3453@github.com",
               "GitHub for Visual Studio - https://YYG3453@github.com/", "https://github.com/"):
    cand = (stored_token(target) or "").strip()
    if len(cand) < 20:
        continue
    probe = urllib.request.Request("https://api.github.com/user")
    probe.add_header("Authorization", "Bearer " + cand)
    probe.add_header("User-Agent", "AutoApply-release")
    try:
        with urllib.request.urlopen(probe, timeout=30) as r:
            login = json.loads(r.read()).get("login")
            scopes = r.headers.get("x-oauth-scopes", "")
        print("token from %s -> login=%s scopes=[%s]" % (target, login, scopes))
        TOKEN = cand
        break
    except urllib.error.HTTPError as e:
        print("token from %s rejected: HTTP %d" % (target, e.code))
if not TOKEN:
    sys.exit("no usable github token in credential manager")

HDR = {"Authorization": "Bearer " + TOKEN, "Accept": "application/vnd.github+json",
       "User-Agent": "AutoApply-release", "X-GitHub-Api-Version": "2022-11-28"}


def call(url, method="GET", data=None, ctype="application/json", accept_status=(200, 201)):
    req = urllib.request.Request(url, data=data, method=method)
    for k, v in HDR.items():
        req.add_header(k, v)
    if data is not None:
        req.add_header("Content-Type", ctype)
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            body = r.read()
            return r.status, (json.loads(body) if body[:1] in (b"{", b"[") else body)
    except urllib.error.HTTPError as e:
        payload = e.read()
        print("HTTP %d on %s %s -> %s" % (e.code, method, url.split("?")[0], payload[:400]))
        return e.code, None


sc, info = call("https://api.github.com/repos/" + REPO)
print("repo: %s private=%s scopes=%s" % (info.get("full_name") if info else "?", info.get("private") if info else "?", "n/a"))

sc, rel = call("https://api.github.com/repos/%s/releases?per_page=20" % REPO)
rel = rel if isinstance(rel, list) else []
print("existing releases:", len(rel), "->", [r.get("tag_name") for r in rel])

existing = next((r for r in rel if r.get("tag_name") == TAG), None)
if existing:
    print("release exists id=%s draft=%s url=%s" % (existing["id"], existing["draft"], existing["html_url"]))
    rel_id = existing["id"]
else:
    body = open(os.path.join(HERE, "release-notes.md"), encoding="utf-8").read()
    payload = json.dumps({"tag_name": TAG, "name": TAG, "body": body,
                          "draft": False, "prerelease": False}).encode("utf-8")
    sc, created = call("https://api.github.com/repos/%s/releases" % REPO, "POST", payload,
                       "application/json; charset=utf-8")
    if sc != 201:
        sys.exit("create failed")
    print("created id=%s url=%s" % (created["id"], created["html_url"]))
    rel_id = created["id"]

sc, assets = call("https://api.github.com/repos/%s/releases/%s/assets" % (REPO, rel_id))
assets = assets if isinstance(assets, list) else []
for a in assets:
    print("  asset: %s %.1f MB state=%s" % (a["name"], a["size"] / 1048576.0, a["state"]))

same = next((a for a in assets if a["name"] == ASSET), None)
if same is not None:
    # 同一个 tag 重发（改文案/修 bug 但不换版本号）：先删旧资产再传，别留下两个同名包
    print("replacing existing asset id=%s size=%.1f MB" % (same["id"], same["size"] / 1048576.0))
    sc, _ = call("https://api.github.com/repos/%s/releases/assets/%s" % (REPO, same["id"]), "DELETE", accept_status=(204, 200))
    print("delete ->", sc)
if True:
    size = os.path.getsize(ZIP)
    print("uploading %.1f MB ..." % (size / 1048576.0))
    t0 = time.time()
    sc, up = call("https://uploads.github.com/repos/%s/releases/%s/assets?name=%s" % (REPO, rel_id, ASSET),
                  "POST", open(ZIP, "rb").read(), "application/zip")
    if sc not in (200, 201):
        sys.exit("upload failed")
    print("uploaded %s %.1f MB state=%s in %.0fs" % (up["name"], up["size"] / 1048576.0, up["state"], time.time() - t0))

time.sleep(1)
sc, latest = call("https://api.github.com/repos/%s/releases/latest" % REPO)
if latest:
    print("latest now: tag=%s assets=%d url=%s" % (latest["tag_name"], len(latest.get("assets") or []), latest["html_url"]))
    for a in latest.get("assets") or []:
        print("  download: %s (%.1f MB)" % (a["browser_download_url"], a["size"] / 1048576.0))
