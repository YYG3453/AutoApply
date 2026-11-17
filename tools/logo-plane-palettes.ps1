param(
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'shots\candidates')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

function New-Bmp([int]$w, [int]$h) {
    $b = New-Object System.Drawing.Bitmap($w, $h)
    $g0 = [System.Drawing.Graphics]::FromImage($b)
    $g0.Clear([System.Drawing.Color]::Transparent)
    $g0.Dispose()
    return $b
}
function C([int]$a, [int]$r, [int]$g, [int]$b) { return [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Pt([double]$x, [double]$y) {
    $p = New-Object System.Drawing.PointF([float]$x, [float]$y)
    return $p
}
function Mix($c1, $c2, [double]$t) {
    $a = [int]($c1.A + ($c2.A - $c1.A) * $t)
    $r = [int]($c1.R + ($c2.R - $c1.R) * $t)
    $g = [int]($c1.G + ($c2.G - $c1.G) * $t)
    $b = [int]($c1.B + ($c2.B - $c1.B) * $t)
    return [System.Drawing.Color]::FromArgb($a, $r, $g, $b)
}
function FillV3($g, $rect, $c1, $c2, $c3) {
    $h = [double]$rect.Height
    $n = [int][Math]::Max(2, [Math]::Round($h))
    for ($i = 0; $i -lt $n; $i++) {
        $t = $i / ($n - 1)
        if ($t -lt 0.5) { $cc = Mix $c1 $c2 ($t * 2) } else { $cc = Mix $c2 $c3 (($t - 0.5) * 2) }
        $br = New-Object System.Drawing.SolidBrush($cc)
        $yy = [float]($rect.Y + $h * $i / $n)
        $hh = [float]($h / $n + 1)
        $g.FillRectangle($br, [float]$rect.X, $yy, [float]$rect.Width, $hh)
        $br.Dispose()
    }
}
function FillV2($g, $rect, $c1, $c2) {
    $h = [double]$rect.Height
    $n = [int][Math]::Max(2, [Math]::Round($h))
    for ($i = 0; $i -lt $n; $i++) {
        $t = $i / ($n - 1)
        $cc = Mix $c1 $c2 $t
        $br = New-Object System.Drawing.SolidBrush($cc)
        $yy = [float]($rect.Y + $h * $i / $n)
        $hh = [float]($h / $n + 1)
        $g.FillRectangle($br, [float]$rect.X, $yy, [float]$rect.Width, $hh)
        $br.Dispose()
    }
}
function RoundRectPath($r, [float]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]($rad * 2)
    if ($d -gt $r.Width) { $d = $r.Width }
    if ($d -gt $r.Height) { $d = $r.Height }
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}
function Add-Glass($g, $r, [float]$side) {
    $top = New-Object System.Drawing.RectangleF([float]$r.X, [float]$r.Y, [float]$r.Width, [float]($r.Height * 0.50))
    FillV2 $g $top (C 100 255 255 255) (C 0 255 255 255)
    $botH = [float]($r.Height * 0.30)
    $bot = New-Object System.Drawing.RectangleF([float]$r.X, [float]($r.Bottom - $botH), [float]$r.Width, $botH)
    FillV2 $g $bot (C 0 0 0 0) (C 56 0 0 0)
}
function Add-Finish($g, $r, [float]$side, [int]$dR, [int]$dG, [int]$dB) {
    $path = RoundRectPath $r ([float]($side * 0.235))
    $p1 = New-Object System.Drawing.Pen((C 92 $dR $dG $dB), [float]([Math]::Max(0.9, $side * 0.010)))
    $g.DrawPath($p1, $path); $p1.Dispose()
    $in2 = [float]($side * 0.018)
    $ir = New-Object System.Drawing.RectangleF([float]($r.X + $in2), [float]($r.Y + $in2), [float]($r.Width - $in2 * 2), [float]($r.Height - $in2 * 2))
    $ipath = RoundRectPath $ir ([float]($side * 0.212))
    $p2 = New-Object System.Drawing.Pen((C 74 255 255 255), [float]([Math]::Max(0.7, $side * 0.006)))
    $g.DrawPath($p2, $ipath); $p2.Dispose(); $ipath.Dispose(); $path.Dispose()
}

# ---- 同一架纸飞机，换 6 套配色 ----
$palettes = @(
    @{ Id = 'B1'; Name = 'Coral Rose (selected)'; C1 = @(255, 255, 176, 52);  C2 = @(255, 255, 96, 84);   C3 = @(255, 224, 26, 118);  Rim = @(130, 0, 44) },
    @{ Id = 'B2'; Name = 'Ocean Jet   sky to indigo'; C1 = @(255, 86, 223, 255); C2 = @(255, 42, 122, 255); C3 = @(255, 72, 30, 214);  Rim = @(20, 24, 120) },
    @{ Id = 'B3'; Name = 'Mint Lagoon   mint to deep teal'; C1 = @(255, 138, 255, 214); C2 = @(255, 16, 214, 166); C3 = @(255, 4, 118, 156);  Rim = @(0, 78, 74) },
    @{ Id = 'B4'; Name = 'Neon Orchid   violet to hot pink'; C1 = @(255, 192, 107, 255); C2 = @(255, 255, 78, 158); C3 = @(255, 246, 30, 92);  Rim = @(104, 0, 70) },
    @{ Id = 'B5'; Name = 'Sunset Ember   gold to crimson'; C1 = @(255, 255, 224, 102); C2 = @(255, 255, 138, 58); C3 = @(255, 214, 34, 66);  Rim = @(120, 18, 20) },
    @{ Id = 'B6'; Name = 'Volt Lime   lime to cyan-teal'; C1 = @(255, 214, 255, 74); C2 = @(255, 46, 208, 106); C3 = @(255, 0, 132, 152);  Rim = @(0, 82, 78) }
)

function Art-Plane([int]$s, $pal) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $c1 = C $pal.C1[0] $pal.C1[1] $pal.C1[2] $pal.C1[3]
    $c2 = C $pal.C2[0] $pal.C2[1] $pal.C2[2] $pal.C2[3]
    $c3 = C $pal.C3[0] $pal.C3[1] $pal.C3[2] $pal.C3[3]
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $c1 $c2 $c3
    Add-Glass $g $r $side
    $ox = $r.X; $oy = $r.Y
    $pen = New-Object System.Drawing.Pen((C 140 255 255 255), [float]([Math]::Max(1.1, $side * 0.030)))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [float]($ox + $side * 0.085), [float]($oy + $side * 0.400), [float]($ox + $side * 0.265), [float]($oy + $side * 0.345))
    $g.DrawLine($pen, [float]($ox + $side * 0.070), [float]($oy + $side * 0.520), [float]($ox + $side * 0.215), [float]($oy + $side * 0.478))
    $pen.Dispose()
    $nose  = Pt ($ox + $side * 0.880) ($oy + $side * 0.155)
    $tailU = Pt ($ox + $side * 0.120) ($oy + $side * 0.440)
    $fold  = Pt ($ox + $side * 0.460) ($oy + $side * 0.640)
    $tailD = Pt ($ox + $side * 0.270) ($oy + $side * 0.885)
    $f2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $f2.AddPolygon(@($nose, $tailU, $fold))
    $b2 = New-Object System.Drawing.SolidBrush((C 255 255 255 255))
    $g.FillPath($b2, $f2); $b2.Dispose()
    $f1 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $f1.AddPolygon(@($nose, $fold, $tailD))
    $b1 = New-Object System.Drawing.SolidBrush((C 255 198 244 255))
    $g.FillPath($b1, $f1); $b1.Dispose()
    $f1.Dispose(); $f2.Dispose()
    $g.Restore($stc)
    Add-Finish $g $r $side $pal.Rim[0] $pal.Rim[1] $pal.Rim[2]
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

$masters = @{}
foreach ($p in $palettes) {
    $m = Art-Plane 512 $p
    $masters[$p.Id] = $m
    $m.Save((Join-Path $OutDir ("{0}-512.png" -f $p.Id)), [System.Drawing.Imaging.ImageFormat]::Png)
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = Art-Plane $sz $p
        $b.Save((Join-Path $OutDir ("{0}-{1}.png" -f $p.Id, $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
    }
}

# 汇总对比图：三列 x 两行，每格「大图 + 小尺寸」，明底
$sheet = New-Bmp 1500 1120
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'ClearTypeGridFit'
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 247 244 238))), 0, 0, 1500, 1120)
$fName = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
$bInk = New-Object System.Drawing.SolidBrush((C 255 26 29 35))
$col = 0; $row = 0
foreach ($p in $palettes) {
    $x = 30 + $col * 490
    $y = 30 + $row * 545
    $g.DrawImage($masters[$p.Id], $x, $y, 220, 220)
    $g.DrawString($p.Name, $fName, $bInk, ($x + 236), ($y + 92))
    $sx = $x
    foreach ($sz in @(64, 48, 32, 16)) {
        $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir ("{0}-{1}.png" -f $p.Id, $sz)))
        $g.DrawImage($img, $sx, ($y + 235), $sz, $sz)
        $img.Dispose()
        $sx += $sz + 12
    }
    $col++
    if ($col -ge 3) { $col = 0; $row++ }
}
$g.Dispose()
$sheet.Save((Join-Path $OutDir 'sheet-B-palettes.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
foreach ($k in $masters.Keys) { $masters[$k].Dispose() }
"palettes -> $OutDir"
