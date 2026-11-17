param(
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'shots\final')
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
function Add-RadialGlow($g, [float]$cx, [float]$cy, [float]$rad, $color, [int]$steps) {
    for ($i = $steps; $i -ge 1; $i--) {
        $t = $i / $steps
        $rr = $rad * $t
        $a = [int]($color.A * (1 - $t) * (1 - $t))
        if ($a -le 1) { continue }
        $br = New-Object System.Drawing.SolidBrush((C $a $color.R $color.G $color.B))
        $g.FillEllipse($br, [float]($cx - $rr), [float]($cy - $rr), [float]($rr * 2), [float]($rr * 2))
        $br.Dispose()
    }
}

# ---------- X4 最终版：夜航（近黑底 + 余烬橙机身 + 背后暖光） ----------
function Art-Final([int]$s) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $p1 = C 255 30 36 68
    $p2 = C 255 18 20 34
    $p3 = C 255 9 10 16
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $p1 $p2 $p3
    $ox = $r.X; $oy = $r.Y
    # 顶部一丝冷光，让黑面不塌
    $topStrip = New-Object System.Drawing.RectangleF([float]$r.X, [float]$r.Y, [float]$r.Width, [float]($r.Height * 0.30))
    $hSteps = [int][Math]::Max(2, [Math]::Round($topStrip.Height))
    for ($i = 0; $i -lt $hSteps; $i++) {
        $t = $i / ($hSteps - 1)
        $a = [int](26 * (1 - $t) * (1 - $t))
        if ($a -le 0) { continue }
        $br = New-Object System.Drawing.SolidBrush((C $a 150 190 255))
        $g.FillRectangle($br, [float]$topStrip.X, [float]($topStrip.Y + $topStrip.Height * $t), [float]$topStrip.Width, [float]($topStrip.Height / $hSteps + 1))
        $br.Dispose()
    }
    # 飞机背后的暖光（小尺寸不加，避免糊）
    if ($s -ge 48) {
        $gcx = [float]($ox + $side * 0.50)
        $gcy = [float]($oy + $side * 0.50)
        Add-RadialGlow $g $gcx $gcy ([float]($side * 0.52)) (C 92 255 138 64) 26
    }
    # 机身
    $nose  = Pt ($ox + $side * 0.885) ($oy + $side * 0.145)
    $tailU = Pt ($ox + $side * 0.105) ($oy + $side * 0.430)
    $fold  = Pt ($ox + $side * 0.475) ($oy + $side * 0.635)
    $tailD = Pt ($ox + $side * 0.255) ($oy + $side * 0.895)
    $plane = New-Object System.Drawing.Drawing2D.GraphicsPath
    $plane.AddPolygon(@($nose, $tailU, $fold, $tailD))
    $w1 = C 255 255 214 108
    $w2 = C 255 255 146 66
    $w3 = C 255 244 74 88
    $st2 = $g.Save()
    $g.SetClip($plane, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $w1 $w2 $w3
    $g.Restore($st2)
    # 折线：两条细暗线，把两个面分开
    $tlw = [float]([Math]::Max(0.6, $side * 0.0085))
    $cEdge = [System.Drawing.Color]::FromArgb(70, 120, 30, 10)
    $edge = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]$cEdge, [float]$tlw)
    $g.DrawLine($edge, $nose, $fold)
    $g.DrawLine($edge, $fold, $tailD)
    $edge.Dispose()
    # 上翼上沿一道窄高光，做出纸面反光
    $cHL = [System.Drawing.Color]::FromArgb(80, 255, 240, 210)
    $wHL = [float]([Math]::Max(0.6, $side * 0.007))
    $hl = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]$cHL, [float]$wHL)
    $g.DrawLine($hl, $nose, $tailU)
    $hl.Dispose()
    # 速度线（三条，逐级变短）
    $sx0 = [float]($ox + $side * 0.075)
    $lineCol = [System.Drawing.Color]::FromArgb(210, 255, 168, 96)
    foreach ($seg in @(@(0.395, 0.320, 0.140), @(0.492, 0.452, 0.105), @(0.585, 0.560, 0.070))) {
        $lw = [float]([Math]::Max(1.0, $side * (0.020 + $seg[2])))
        $pen = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]$lineCol, [float]$lw)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $x1 = $sx0 + $side * 0.0
        $y1 = $oy + $side * $seg[0]
        $x2 = $sx0 + $side * ($seg[2] + 0.115)
        $y2 = $oy + $side * $seg[1]
        $g.DrawLine($pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
        $pen.Dispose()
    }
    $g.Restore($stc)
    # 外框：深色描边 + 内侧冷白亮边
    $cOut = [System.Drawing.Color]::FromArgb(120, 58, 72, 112)
    $wOut = [float]([Math]::Max(0.9, $side * 0.010))
    $pOut = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]$cOut, [float]$wOut)
    $g.DrawPath($pOut, $plate); $pOut.Dispose()
    $in2 = [float]($side * 0.017)
    $ir = New-Object System.Drawing.RectangleF([float]($r.X + $in2), [float]($r.Y + $in2), [float]($r.Width - $in2 * 2), [float]($r.Height - $in2 * 2))
    $iPath = RoundRectPath $ir ([float]($side * 0.212))
    $cIn = [System.Drawing.Color]::FromArgb(60, 190, 215, 255)
    $wIn = [float]([Math]::Max(0.7, $side * 0.006))
    $pIn = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]$cIn, [float]$wIn)
    $g.DrawPath($pIn, $iPath); $pIn.Dispose(); $iPath.Dispose()
    $plane.Dispose(); $plate.Dispose(); $g.Dispose()
    return $bmp
}

foreach ($sz in @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512)) {
    $b = Art-Final $sz
    $b.Save((Join-Path $OutDir ("icon-{0}.png" -f $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
}

# 预览板：明底 / 暗底 + 各尺寸实拍
$sheet = New-Bmp 1240 720
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'ClearTypeGridFit'
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 247 244 238))), 0, 0, 620, 720)
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 8 9 13))), 620, 0, 620, 720)
$big = [System.Drawing.Image]::FromFile((Join-Path $OutDir 'icon-512.png'))
$g.DrawImage($big, 60, 40, 360, 360)
$g.DrawImage($big, 680, 40, 360, 360)
$big.Dispose()
$fN = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$fS = New-Object System.Drawing.Font('Segoe UI', 9)
$bInk = New-Object System.Drawing.SolidBrush((C 255 26 29 35))
$bWht = New-Object System.Drawing.SolidBrush((C 255 232 236 240))
$g.DrawString('AutoApply · Night Ember', $fN, $bInk, 60, 410)
$g.DrawString('near-black plate, ember plane, warm backlight', $fS, $bInk, 60, 436)
$g.DrawString('AutoApply · Night Ember', $fN, $bWht, 680, 410)
$g.DrawString('near-black plate, ember plane, warm backlight', $fS, $bWht, 680, 436)
$y = 480
foreach ($base in @(60, 680)) {
    $x = $base
    foreach ($sz in @(96, 64, 48, 32, 24, 16)) {
        $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir ("icon-{0}.png" -f $sz)))
        $g.DrawImage($img, $x, ($y + (96 - $sz)), $sz, $sz)
        $img.Dispose()
        $x += $sz + 14
    }
}
$g.DrawString('96 / 64 / 48 / 32 / 24 / 16 px — drawn per size, not downscaled', $fS, $bInk, 60, 610)
$g.DrawString('96 / 64 / 48 / 32 / 24 / 16 px — drawn per size, not downscaled', $fS, $bWht, 680, 610)
$g.Dispose()
$sheet.Save((Join-Path $OutDir 'preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
"final art -> $OutDir"
