# v{VERSION} 发布说明模板

> 用法：复制到 `tools\release-notes.md` 后填好；`tools\publish_release.py` 发布时会在末尾
> 自动追加「核对安装包（SHA256）」一段（带 `<!-- sha256:begin -->` 标记、重复发布幂等替换），
> 这一段**不用手写**，也不要把标记删掉。

## 本版改动

- {一句话说清用户能感知的变化}
- {修掉的 bug：什么现象 → 现在怎样}

## 下载

- `AutoApply-{VERSION}-win-x64.zip`（约 {SIZE} MB，解压即用，不用安装）

## 升级方式

1. 关闭正在运行的 AutoApply（托盘图标 → 退出）。
2. 解压新包，覆盖旧目录里的 `AutoApply.exe` 和 `scripts\*.js`。
3. `data\` 里的投递记录、`%LOCALAPPDATA%\AutoApply\browser-profile` 里的登录态都不会被覆盖，**不用重新登录**。

## 已知的使用前提

- Windows 10 1903+ / Windows 11，64 位。
- 需要本机有 Chrome 或 Edge（程序用独立配置目录起一个受控浏览器，不影响你日常用的浏览器）。
- 四个平台需各自扫码登录一次（登录态只保存在本机，不上传）。
