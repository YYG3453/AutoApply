param(
    [string]$Out = "AutoApply.exe"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# Resolve the output to an absolute path first: csc resolves /out against the process working
# directory, so a bare file name can land somewhere unexpected. Keep this file ASCII-only:
# Windows PowerShell 5.1 reads a BOM-less .ps1 as GBK and would mangle non-ASCII text.
$outPath = if ($Out -match '^[A-Za-z]:\\') { $Out } else { Join-Path $root $Out }
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
    "/out:$outPath",
    "/win32manifest:$(Join-Path $root 'src\app.manifest')",
    "/resource:$(Join-Path $root 'src\Web\phone.html'),AutoApply.phone.html"
) + $refs + $files

$ico = Join-Path $root 'assets\app.ico'
if (Test-Path $ico) { $args = $args[0..6] + @("/win32icon:$ico") + $args[7..($args.Count - 1)] }

Push-Location $root
try {
    & $csc @args 2>&1 | ForEach-Object { $_ }
    if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
    $f = Get-Item $outPath
    "OK  $($f.Name)  $([math]::Round($f.Length/1KB,1)) KB"

    # If the output folder carries a Low mandatory-integrity label, Windows runs the exe at
    # low integrity and Chrome refuses to start (crashpad 0x80000003). Try to clear the label
    # on the exe; if we are not elevated, say exactly how to fix it.
    $ErrorActionPreference = 'SilentlyContinue'
    $acl = (& icacls $f.FullName 2>&1 | Out-String)
    if ($acl -match 'Low Mandatory Level') {
        $null = (& icacls $f.FullName /setintegritylevel Medium 2>&1)
        $acl = (& icacls $f.FullName 2>&1 | Out-String)
        if ($acl -match 'Low Mandatory Level') {
            "WARN  this exe carries a Low integrity label: it starts with restricted rights and cannot launch Chrome."
            "      Run tools\fix-integrity-label.cmd as Administrator once (clears the label for the folder), then rebuild."
        } else {
            "OK    exe integrity label restored to Medium"
        }
    }
    $ErrorActionPreference = 'Stop'
}finally { Pop-Location }
