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
    $p1 = New-Object System.Drawing.Pen((C 96 $dR $dG $dB), [float]([Math]::Max(0.9, $side * 0.010)))
    $g.DrawPath($p1, $path); $p1.Dispose()
    $in2 = [float]($side * 0.018)
    $ir = New-Object System.Drawing.RectangleF([float]($r.X + $in2), [float]($r.Y + $in2), [float]($r.Width - $in2 * 2), [float]($r.Height - $in2 * 2))
    $ipath = RoundRectPath $ir ([float]($side * 0.212))
    $p2 = New-Object System.Drawing.Pen((C 74 255 255 255), [float]([Math]::Max(0.7, $side * 0.006)))
    $g.DrawPath($p2, $ipath); $p2.Dispose(); $ipath.Dispose(); $path.Dispose()
}

# ---- 混色方案：冷底 + 暖点缀 ----
$mixes = @(
    @{ Id = 'M1'; Name = 'Ocean x Amber   blue base, warm plane accents'
       P1 = @(255, 86, 223, 255); P2 = @(255, 42, 122, 255); P3 = @(255, 72, 20, 210); Rim = @(18, 22, 118)
       Wing = @(255, 255, 255, 255); Fold = @(255, 255, 198, 112); Line = @(255, 255, 214, 138) },
    @{ Id = 'M2'; Name = 'Night Flight   deep indigo, champagne gold'
       P1 = @(255, 38, 48, 94); P2 = @(255, 56, 30, 104); P3 = @(255, 92, 20, 84); Rim = @(8, 6, 34)
       Wing = @(255, 255, 255, 255); Fold = @(255, 242, 200, 118); Line = @(255, 236, 190, 110) },
    @{ Id = 'M3'; Name = 'Dusk Gradient   sunset top to ocean bottom'
       P1 = @(255, 255, 138, 76); P2 = @(255, 224, 51, 140); P3 = @(255, 59, 43, 200); Rim = @(54, 18, 92)
       Wing = @(255, 255, 255, 255); Fold = @(255, 255, 232, 236); Line = @(255, 255, 255, 255) },
    @{ Id = 'M4'; Name = 'Lagoon x Coral   teal base, coral accents'
       P1 = @(255, 32, 222, 176); P2 = @(255, 14, 158, 150); P3 = @(255, 8, 106, 140); Rim = @(0, 66, 58)
       Wing = @(255, 255, 255, 255); Fold = @(255, 255, 150, 116); Line = @(255, 255, 198, 176) }
)

function Art-Mix([int]$s, $pal) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $c1 = C $pal.P1[0] $pal.P1[1] $pal.P1[2] $pal.P1[3]
    $c2 = C $pal.P2[0] $pal.P2[1] $pal.P2[2] $pal.P2[3]
    $c3 = C $pal.P3[0] $pal.P3[1] $pal.P3[2] $pal.P3[3]
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $c1 $c2 $c3
    Add-Glass $g $r $side
    $ox = $r.X; $oy = $r.Y
    $lc = C $pal.Line[0] $pal.Line[1] $pal.Line[2] $pal.Line[3]
    $pen = New-Object System.Drawing.Pen($lc, [float]([Math]::Max(1.1, $side * 0.030)))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [float]($ox + $side * 0.085), [float]($oy + $side * 0.400), [float]($ox + $side * 0.265), [float]($oy + $side * 0.345))
    $g.DrawLine($pen, [float]($ox + $side * 0.070), [float]($oy + $side * 0.520), [float]($ox + $side * 0.215), [float]($oy + $side * 0.478))
    $pen.Dispose()
    $nose  = Pt ($ox + $side * 0.880) ($oy + $side * 0.155)
    $tailU = Pt ($ox + $side * 0.120) ($oy + $side * 0.440)
    $fold  = Pt ($ox + $side * 0.460) ($oy + $side * 0.640)
    $tailD = Pt ($ox + $side * 0.270) ($oy + $side * 0.885)
    $wc = C $pal.Wing[0] $pal.Wing[1] $pal.Wing[2] $pal.Wing[3]
    $fc = C $pal.Fold[0] $pal.Fold[1] $pal.Fold[2] $pal.Fold[3]
    $f2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $f2.AddPolygon(@($nose, $tailU, $fold))
    $b2 = New-Object System.Drawing.SolidBrush($wc)
    $g.FillPath($b2, $f2); $b2.Dispose()
    $f1 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $f1.AddPolygon(@($nose, $fold, $tailD))
    $b1 = New-Object System.Drawing.SolidBrush($fc)
    $g.FillPath($b1, $f1); $b1.Dispose()
    $f1.Dispose(); $f2.Dispose()
    $g.Restore($stc)
    Add-Finish $g $r $side $pal.Rim[0] $pal.Rim[1] $pal.Rim[2]
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

$masters = @{}
foreach ($p in $mixes) {
    $m = Art-Mix 512 $p
    $masters[$p.Id] = $m
    $m.Save((Join-Path $OutDir ("{0}-512.png" -f $p.Id)), [System.Drawing.Imaging.ImageFormat]::Png)
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = Art-Mix $sz $p
        $b.Save((Join-Path $OutDir ("{0}-{1}.png" -f $p.Id, $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
    }
}

$sheet = New-Bmp 1180 1120
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'ClearTypeGridFit'
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 247 244 238))), 0, 0, 1180, 1120)
$fName = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
$bInk = New-Object System.Drawing.SolidBrush((C 255 26 29 35))
$rowY = 24
foreach ($p in $mixes) {
    $g.DrawImage($masters[$p.Id], 34, $rowY, 210, 210)
    $g.DrawString($p.Name, $fName, $bInk, 270, ($rowY + 88))
    $sx = 34
    foreach ($sz in @(64, 48, 32, 16)) {
        $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir ("{0}-{1}.png" -f $p.Id, $sz)))
        $g.DrawImage($img, $sx, ($rowY + 222), $sz, $sz)
        $img.Dispose()
        $sx += $sz + 12
    }
    $rowY += 275
}
$g.Dispose()
$sheet.Save((Join-Path $OutDir 'sheet-B-mix.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
foreach ($k in $masters.Keys) { $masters[$k].Dispose() }
"mix palettes -> $OutDir"
