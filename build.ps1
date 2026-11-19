param(
    [string]$Out = "AutoApply.exe"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$files = Get-ChildItem (Join-Path $root 'src\*.cs') -Recurse | ForEach-Object { $_.FullName }
$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll',
    '/r:System.Management.dll'
)

$args = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    "/out:$Out",
    "/win32manifest:$(Join-Path $root 'src\app.manifest')"
) + $refs + $files

$ico = Join-Path $root 'assets\app.ico'
if (Test-Path $ico) { $args = $args[0..6] + @("/win32icon:$ico") + $args[7..($args.Count - 1)] }

Push-Location $root
try {
    & $csc @args 2>&1 | ForEach-Object { $_ }
    if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
    $f = Get-Item (Join-Path $root $Out)
    "OK  $($f.Name)  $([math]::Round($f.Length/1KB,1)) KB"
}
finally { Pop-Location }
