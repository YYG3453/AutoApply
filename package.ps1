# Build the release folder + zip. ASCII only: Windows PowerShell 5.1 mangles non-ASCII without BOM.
param([string]$Version = "1.0.9")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

Write-Host "== build exe"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1")
if (-not (Test-Path (Join-Path $root "AutoApply.exe"))) { throw "build failed" }

$name = "AutoApply-$Version-win-x64"
$dist = Join-Path $root ("dist\" + $name)
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

Write-Host "== copy app files"
Copy-Item (Join-Path $root "AutoApply.exe") $dist
Copy-Item (Join-Path $root "README.md") $dist
Copy-Item (Join-Path $root "LICENSE") $dist
Copy-Item (Join-Path $root "config.json") $dist
if (Test-Path (Join-Path $root "docs")) { Copy-Item (Join-Path $root "docs") $dist -Recurse }

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
  "replies.js", "probe-health.js", "rules.json", "cities.json", "cities-zp.json")
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

Write-Host "== zip"
$zip = Join-Path $root ("dist\" + $name + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($dist, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$exeSize = [math]::Round((Get-Item (Join-Path $dist "AutoApply.exe")).Length / 1KB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ("OK  " + $name + ".zip  " + $zipMb + " MB  (exe " + $exeSize + " KB)")
Write-Host ("path: " + $zip)
