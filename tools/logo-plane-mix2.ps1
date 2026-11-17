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
    FillV2 $g $top (C 96 255 255 255) (C 0 255 255 255)
    $botH = [float]($r.Height * 0.30)
    $bot = New-Object System.Drawing.RectangleF([float]$r.X, [float]($r.Bottom - $botH), [float]$r.Width, $botH)
    FillV2 $g $bot (C 0 0 0 0) (C 58 0 0 0)
}
function Add-Finish($g, $r, [float]$side, [int]$dR, [int]$dG, [int]$dB) {
    $path = RoundRectPath $r ([float]($side * 0.235))
    $p1 = New-Object System.Drawing.Pen((C 96 $dR $dG $dB), [float]([Math]::Max(0.9, $side * 0.010)))
    $g.DrawPath($p1, $path); $p1.Dispose()
    $in2 = [float]($side * 0.018)
    $ir = New-Object System.Drawing.RectangleF([float]($r.X + $in2), [float]($r.Y + $in2), [float]($r.Width - $in2 * 2), [float]($r.Height - $in2 * 2))
    $ipath = RoundRectPath $ir ([float]($side * 0.212))
    $p2 = New-Object System.Drawing.Pen((C 76 255 255 255), [float]([Math]::Max(0.7, $side * 0.006)))
    $g.DrawPath($p2, $ipath); $p2.Dispose(); $ipath.Dispose(); $path.Dispose()
}

# ---- 真正的"混色"：冷底 + 暖机身（机身本身是暖色渐变）----
$vs = @(
    @{ Id = 'X1'; Name = 'Amber Flight    blue plate, amber-coral plane'
       P = @(@(255, 62, 108, 255), @(255, 28, 62, 224), @(255, 26, 18, 132)); Rim = @(14, 14, 96)
       W = @(@(255, 255, 206, 92), @(255, 255, 138, 78), @(255, 246, 74, 96)); Line = @(255, 255, 255, 255) },
    @{ Id = 'X2'; Name = 'Coral Pilot    teal plate, gold-rose plane'
       P = @(@(255, 34, 206, 194), @(255, 12, 152, 156), @(255, 6, 96, 128)); Rim = @(0, 62, 58)
       W = @(@(255, 255, 214, 96), @(255, 255, 148, 108), @(255, 250, 112, 128)); Line = @(255, 255, 255, 255) },
    @{ Id = 'X3'; Name = 'Dusk Duotone    sunset to indigo, white plane'
       P = @(@(255, 255, 146, 84), @(255, 226, 56, 142), @(255, 56, 40, 198)); Rim = @(48, 16, 88)
       W = @(@(255, 255, 255, 255), @(255, 255, 255, 255), @(255, 255, 255, 255)); Line = @(255, 255, 255, 255) },
    @{ Id = 'X4'; Name = 'Night Ember    near-black, ember plane'
       P = @(@(255, 30, 34, 66), @(255, 24, 22, 48), @(255, 16, 14, 30)); Rim = @(0, 0, 0)
       W = @(@(255, 255, 194, 64), @(255, 255, 132, 72), @(255, 240, 68, 92)); Line = @(255, 255, 176, 96) }
)

function Art-X([int]$s, $v) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $p1 = C $v.P[0][0] $v.P[0][1] $v.P[0][2] $v.P[0][3]
    $p2 = C $v.P[1][0] $v.P[1][1] $v.P[1][2] $v.P[1][3]
    $p3 = C $v.P[2][0] $v.P[2][1] $v.P[2][2] $v.P[2][3]
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $p1 $p2 $p3
    Add-Glass $g $r $side
    $ox = $r.X; $oy = $r.Y
    $lc = C $v.Line[0] $v.Line[1] $v.Line[2] $v.Line[3]
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
    $plane = New-Object System.Drawing.Drawing2D.GraphicsPath
    $plane.AddPolygon(@($nose, $tailU, $fold, $tailD))
    $w1 = C $v.W[0][0] $v.W[0][1] $v.W[0][2] $v.W[0][3]
    $w2 = C $v.W[1][0] $v.W[1][1] $v.W[1][2] $v.W[1][3]
    $w3 = C $v.W[2][0] $v.W[2][1] $v.W[2][2] $v.W[2][3]
    $st2 = $g.Save()
    $g.SetClip($plane, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $w1 $w2 $w3
    $g.Restore($st2)
    # 折线阴影，区分两个面
    $fEdge = New-Object System.Drawing.Pen((C 46 60 0 20), [float]([Math]::Max(0.6, $side * 0.008)))
    $g.DrawLine($fEdge, $nose, $fold)
    $g.DrawLine($fEdge, $fold, $tailD)
    $fEdge.Dispose()
    $plane.Dispose()
    $g.Restore($stc)
    Add-Finish $g $r $side $v.Rim[0] $v.Rim[1] $v.Rim[2]
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

$masters = @{}
foreach ($v in $vs) {
    $m = Art-X 512 $v
    $masters[$v.Id] = $m
    $m.Save((Join-Path $OutDir ("{0}-512.png" -f $v.Id)), [System.Drawing.Imaging.ImageFormat]::Png)
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = Art-X $sz $v
        $b.Save((Join-Path $OutDir ("{0}-{1}.png" -f $v.Id, $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
    }
}

$sheet = New-Bmp 1200 1120
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'ClearTypeGridFit'
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 247 244 238))), 0, 0, 600, 1120)
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 9 11 15))), 600, 0, 600, 1120)
$fName = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
$bInk = New-Object System.Drawing.SolidBrush((C 255 26 29 35))
$bWht = New-Object System.Drawing.SolidBrush((C 255 236 240 240))
$rowY = 20
foreach ($v in $vs) {
    $m = $masters[$v.Id]
    $g.DrawImage($m, 26, $rowY, 152, 152)
    $g.DrawImage($m, 626, $rowY, 152, 152)
    $g.DrawString($v.Name, $fName, $bInk, 190, ($rowY + 62))
    $g.DrawString($v.Name, $fName, $bWht, 790, ($rowY + 62))
    foreach ($base in @(26, 626)) {
        $sx = $base
        foreach ($sz in @(64, 48, 32, 16)) {
            $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir ("{0}-{1}.png" -f $v.Id, $sz)))
            $g.DrawImage($img, $sx, ($rowY + 164), $sz, $sz)
            $img.Dispose()
            $sx += $sz + 10
        }
    }
    $rowY += 275
}
$g.Dispose()
$sheet.Save((Join-Path $OutDir 'sheet-B-mix2.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
foreach ($k in $masters.Keys) { $masters[$k].Dispose() }
"mix2 -> $OutDir"
