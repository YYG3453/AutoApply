# Build the release folder + zip. ASCII only: Windows PowerShell 5.1 mangles non-ASCII without BOM.
param([string]$Version = "")
if (-not $Version) {
    $m = Select-String -Path (Join-Path $PSScriptRoot "src\MainForm.cs") -Pattern 'Version = "([0-9.]+)"'
    if ($m) { $Version = $m.Matches[0].Groups[1].Value } else { $Version = "0.0.0" }
}

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

Write-Host "== build exe"
# 编到旁边的临时名：root 里的 AutoApply.exe 可能正被运行中的实例锁着（Windows 不允许覆盖正在执行的映像）
$buildExe = "AutoApply.pkg.exe"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1") -Out $buildExe
if (-not (Test-Path (Join-Path $root $buildExe))) { throw "build failed" }

$name = "AutoApply-$Version-win-x64"
$dist = Join-Path $root ("dist\" + $name)
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

Write-Host "== copy app files"
Copy-Item (Join-Path $root $buildExe) (Join-Path $dist "AutoApply.exe")
Copy-Item (Join-Path $root "README.md") $dist
Copy-Item (Join-Path $root "LICENSE") $dist
# config.json 是每台机器自己的设置（城市/关键词/招呼语），不进包：
# 包里带一份就会在升级时覆盖用户配置，而且会把作者的偏好一起发出去。
# 首次运行由程序自己生成默认配置并走向导。
if (Test-Path (Join-Path $root "config.json")) {
    Write-Host "  config.json found in repo root, ignored (user data must not ship)"
}
if (Test-Path (Join-Path $root "docs")) { Copy-Item (Join-Path $root "docs") $dist -Recurse }

# Helper scripts the README tells users to run (repair integrity label, allow the phone page).
Write-Host "== copy tools"
$toolsDir = Join-Path $root "tools"
if (Test-Path $toolsDir) {
    $td = Join-Path $dist "tools"
    New-Item -ItemType Directory $td | Out-Null
    $helpers = Get-ChildItem $toolsDir -File | Where-Object { $_.Extension -eq ".cmd" -or $_.Name -eq "fix-integrity-label.ps1" }
    foreach ($h in $helpers) { Copy-Item $h.FullName $td }
    Write-Host ("  helpers in package: " + $helpers.Count)
}

Write-Host "== copy node runtime"
$nodeSrc = "D:\APP\javasoft\Node\node.exe"
if (-not (Test-Path $nodeSrc)) { $nodeSrc = (Get-Command node.exe).Source }
New-Item -ItemType Directory (Join-Path $dist "node") | Out-Null
Copy-Item $nodeSrc (Join-Path $dist "node\node.exe")

Write-Host "== copy scripts"
$sd = Join-Path $dist "scripts"
New-Item -ItemType Directory $sd | Out-Null
# Base list + anything the C# side calls by name (RunNodeTool("x.js") / RunTool("evalfile.js","x.js")).
# The auto-scan is the guard that stops us shipping a package whose script is silently missing.
$files = @("boss.js", "zhaopin.js", "yingjiesheng.js", "q51job.js", "cdp.js", "evalfile.js", "make-report.js", "score.js",
  "replies.js", "probe-health.js", "cities.json", "cities-zp.json")
$refs = @()
Get-ChildItem (Join-Path $root "src") -Recurse -Filter *.cs | ForEach-Object {
  $txt = Get-Content $_.FullName -Raw
  foreach ($m in [regex]::Matches($txt, 'RunNodeTool\("([^"]+\.js)"')) { $refs += $m.Groups[1].Value }
}
$refs = $refs | Sort-Object -Unique
foreach ($r in $refs) { if ($files -notcontains $r) { Write-Host ("  auto-add " + $r); $files += $r } }
foreach ($f in $files) {
  $src = Join-Path $root ("scripts\" + $f)
  if (-not (Test-Path $src)) { throw ("script missing from scripts\: " + $f) }
  Copy-Item $src $sd
}
Write-Host ("  scripts in package: " + $files.Count)

Write-Host "== hashes"
# 包内一份：核对解压出来的 AutoApply.exe 有没有被换过（发布页正文会告诉用户怎么用）
$exeHash = (Get-FileHash (Join-Path $dist "AutoApply.exe") -Algorithm SHA256).Hash.ToLower()
"$exeHash  AutoApply.exe" | Set-Content -Path (Join-Path $dist "SHA256SUMS.txt") -Encoding ASCII
Write-Host ("  AutoApply.exe  " + $exeHash)

Write-Host "== zip"
$zip = Join-Path $root ("dist\" + $name + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($dist, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

# 发布目录一份：核对下载到的 zip 本身（publish_release.py 会上传它并把哈希贴进发布页）
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
("$zipHash  " + $name + ".zip") | Set-Content -Path (Join-Path $root "dist\SHA256SUMS.txt") -Encoding ASCII

$exeSize = [math]::Round((Get-Item (Join-Path $dist "AutoApply.exe")).Length / 1KB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ("OK  " + $name + ".zip  " + $zipMb + " MB  (exe " + $exeSize + " KB)")
Write-Host ("sha256  " + $name + ".zip  " + $zipHash)
Write-Host ("path: " + $zip)
