# 把 AutoApply 目录从「低完整性」改回「中完整性」
#
# 背景：某些沙箱/安全软件会给文件夹的 SACL 打上 Low Mandatory Level 标签。
#       从该文件夹启动的程序（包括在这里构建出来的 AutoApply.exe）会继承标签，
#       以低权限运行 —— 而 Chrome 在低权限下会以 0x80000003 退出，浏览器起不来。
#
# 只需要改三处：
#   1) 文件夹本身：把可继承的标签改成 Medium，以后新建的文件（含每次重新构建的 exe）都自动是 Medium
#   2) 现有的 exe：AutoApply.exe / 包里那几个，已经是 Low 的要单独改
#   3) node 目录：程序会拉起 node.exe 跑脚本，它也不能是 Low
# 其余文件（记录、缓存、登录态数据）保持原样即可：中权限进程可以正常读写低完整性文件。
#
# 用法：由 fix-integrity-label.cmd 以管理员身份调用（改 SACL 必须有管理员权限）。

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'AutoApply.exe'
if (-not (Test-Path $exe)) {
    Write-Host "没找到 AutoApply.exe，请在 AutoApply 目录里的 tools 下运行本脚本。" -ForegroundColor Red
    exit 1
}

Write-Host "目标目录：$root"
Write-Host ""

# 1) 文件夹：可继承标签 -> Medium
& icacls $root /setintegritylevel '(OI)(CI)Medium' | Out-Null
Write-Host ("1/3 文件夹标签  " + $(if ($LASTEXITCODE -eq 0) { "OK" } else { "失败（退出码 $LASTEXITCODE）" }))

# 2) 现有 exe
$exes = @(Get-ChildItem -LiteralPath $root -Recurse -Force -Filter *.exe | Where-Object { -not $_.PSIsContainer })
$okExe = 0
foreach ($f in $exes) {
    & icacls $f.FullName /setintegritylevel Medium | Out-Null
    if ($LASTEXITCODE -eq 0) { $okExe++ }
}
Write-Host ("2/3 exe 标签    {0}/{1} 个改为 Medium" -f $okExe, $exes.Count)

# 3) node 运行时（程序会启动它跑 scripts\*.js）
$node = Join-Path $root 'node'
if (Test-Path $node) {
    & icacls $node /setintegritylevel '(OI)(CI)Medium' /T | Out-Null
    Write-Host ("3/3 node 目录    " + $(if ($LASTEXITCODE -eq 0) { "OK" } else { "失败（退出码 $LASTEXITCODE）" }))
} else {
    Write-Host "3/3 node 目录    不存在，跳过"
}

Write-Host ""
$check = icacls $exe 2>$null | Out-String
if ($check -match 'Low Mandatory Level') {
    Write-Host "仍然带着「低完整性」标签 —— 请把本窗口截图发给作者。" -ForegroundColor Yellow
} else {
    Write-Host "完成：AutoApply.exe 不再是低完整性。双击它就能正常拉起浏览器了。" -ForegroundColor Green
}
Write-Host ""
Write-Host "提示：如果以后再往这个文件夹里放新的 exe 并发现同样问题，重新运行一次本脚本即可。"
