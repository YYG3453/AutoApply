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
function Add-Glass($g, $r, [float]$side) {
    $top = New-Object System.Drawing.RectangleF([float]$r.X, [float]$r.Y, [float]$r.Width, [float]($r.Height * 0.50))
    $ga = C 105 255 255 255
    $gb = C 0 255 255 255
    FillV2 $g $top $ga $gb
    $botH = [float]($r.Height * 0.30)
    $bot = New-Object System.Drawing.RectangleF([float]$r.X, [float]($r.Bottom - $botH), [float]$r.Width, $botH)
    $sa = C 0 0 0 0
    $sb = C 58 0 0 0
    FillV2 $g $bot $sa $sb
}
function Add-Finish($g, $r, [float]$side, [int]$dR, [int]$dG, [int]$dB) {
    $path = RoundRectPath $r ([float]($side * 0.235))
    $w1 = [float]([Math]::Max(0.9, $side * 0.010))
    $p1 = New-Object System.Drawing.Pen((C 92 $dR $dG $dB), $w1)
    $g.DrawPath($p1, $path)
    $p1.Dispose()
    $in2 = [float]($side * 0.018)
    $ir = New-Object System.Drawing.RectangleF([float]($r.X + $in2), [float]($r.Y + $in2), [float]($r.Width - $in2 * 2), [float]($r.Height - $in2 * 2))
    $ipath = RoundRectPath $ir ([float]($side * 0.212))
    $p2 = New-Object System.Drawing.Pen((C 74 255 255 255), [float]([Math]::Max(0.7, $side * 0.006)))
    $g.DrawPath($p2, $ipath)
    $p2.Dispose()
    $ipath.Dispose()
    $path.Dispose()
}

function Art-A([int]$s) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $c1 = C 255 126 74 255
    $c2 = C 255 72 106 255
    $c3 = C 255 0 206 255
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $c1 $c2 $c3
    Add-Glass $g $r $side
    $ox = $r.X; $oy = $r.Y
    $t1 = Pt ($ox + $side * 0.500) ($oy + $side * 0.195)
    $t2 = Pt ($ox + $side * 0.228) ($oy + $side * 0.792)
    $t3 = Pt ($ox + $side * 0.772) ($oy + $side * 0.792)
    $i1 = Pt ($ox + $side * 0.500) ($oy + $side * 0.392)
    $i2 = Pt ($ox + $side * 0.356) ($oy + $side * 0.792)
    $i3 = Pt ($ox + $side * 0.644) ($oy + $side * 0.792)
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddPolygon(@($t1, $t2, $t3))
    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddPolygon(@($i1, $i2, $i3))
    $reg = New-Object System.Drawing.Region($outer)
    $reg.Exclude($inner)
    $barR = New-Object System.Drawing.RectangleF([float]($ox + $side * 0.350), [float]($oy + $side * 0.572), [float]($side * 0.300), [float]($side * 0.072))
    $reg.Union($barR)
    $sh = New-Object System.Drawing.SolidBrush((C 62 40 0 120))
    $tmp = $reg.Clone()
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([float]($side * 0.014), [float]($side * 0.016))
    $tmp.Transform($m)
    $g.FillRegion($sh, $tmp)
    $tmp.Dispose(); $m.Dispose(); $sh.Dispose()
    $ab = New-Object System.Drawing.SolidBrush((C 255 255 255 255))
    $g.FillRegion($ab, $reg)
    $ab.Dispose()
    $reg.Dispose(); $inner.Dispose(); $outer.Dispose()
    $g.Restore($stc)
    Add-Finish $g $r $side 70 0 130
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

function Art-B([int]$s) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $c1 = C 255 255 176 52
    $c2 = C 255 255 96 84
    $c3 = C 255 224 26 118
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
    Add-Finish $g $r $side 130 0 44
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

function Art-C([int]$s) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $c1 = C 255 255 216 88
    $c2 = C 255 255 148 32
    $c3 = C 255 248 72 20
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $c1 $c2 $c3
    Add-Glass $g $r $side
    $ox = $r.X; $oy = $r.Y
    $p1 = Pt ($ox + $side * 0.572) ($oy + $side * 0.140)
    $p2 = Pt ($ox + $side * 0.262) ($oy + $side * 0.556)
    $p3 = Pt ($ox + $side * 0.462) ($oy + $side * 0.556)
    $p4 = Pt ($ox + $side * 0.404) ($oy + $side * 0.860)
    $p5 = Pt ($ox + $side * 0.738) ($oy + $side * 0.428)
    $p6 = Pt ($ox + $side * 0.532) ($oy + $side * 0.428)
    $b = New-Object System.Drawing.Drawing2D.GraphicsPath
    $b.AddPolygon(@($p1, $p2, $p3, $p4, $p5, $p6))
    $bw = New-Object System.Drawing.SolidBrush((C 255 255 255 255))
    $g.FillPath($bw, $b)
    $bw.Dispose(); $b.Dispose()
    $g.Restore($stc)
    Add-Finish $g $r $side 130 44 0
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

function Art-D([int]$s) {
    $bmp = New-Bmp $s $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pad = [float]($s * 0.04); $side = [float]($s - $pad * 2)
    $r = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $plate = RoundRectPath $r ([float]($side * 0.235))
    $c1 = C 255 126 240 122
    $c2 = C 255 22 198 150
    $c3 = C 255 8 132 196
    $stc = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    FillV3 $g $r $c1 $c2 $c3
    Add-Glass $g $r $side
    $ox = $r.X; $oy = $r.Y
    $card = New-Object System.Drawing.RectangleF([float]($ox + $side * 0.185), [float]($oy + $side * 0.285), [float]($side * 0.630), [float]($side * 0.430))
    $cp = RoundRectPath $card ([float]($side * 0.060))
    $cb1 = New-Object System.Drawing.SolidBrush((C 255 255 255 255))
    $g.FillPath($cb1, $cp); $cb1.Dispose(); $cp.Dispose()
    $lb = New-Object System.Drawing.SolidBrush((C 66 6 110 130))
    $g.FillRectangle($lb, [float]($card.X + $side * 0.070), [float]($card.Y + $side * 0.075), [float]($side * 0.300), [float]($side * 0.042))
    $g.FillRectangle($lb, [float]($card.X + $side * 0.070), [float]($card.Y + $side * 0.165), [float]($side * 0.390), [float]($side * 0.042))
    $g.FillRectangle($lb, [float]($card.X + $side * 0.070), [float]($card.Y + $side * 0.255), [float]($side * 0.235), [float]($side * 0.042))
    $lb.Dispose()
    $bx = [float]($ox + $side * 0.555)
    $by = [float]($oy + $side * 0.545)
    $bw2 = [float]($side * 0.320)
    $badge = New-Object System.Drawing.RectangleF($bx, $by, $bw2, $bw2)
    $bb = New-Object System.Drawing.SolidBrush((C 255 255 255 255))
    $g.FillEllipse($bb, $badge); $bb.Dispose()
    $cxA = $badge.X + $badge.Width / 2
    $cyA = $badge.Y + $badge.Height / 2
    $u = $badge.Width * 0.26
    $a1 = Pt ($cxA) ($cyA - $u * 1.45)
    $a2 = Pt ($cxA + $u * 1.45) ($cyA - $u * 0.10)
    $a3 = Pt ($cxA + $u * 0.48) ($cyA - $u * 0.10)
    $a4 = Pt ($cxA + $u * 0.48) ($cyA + $u * 1.40)
    $a5 = Pt ($cxA - $u * 0.48) ($cyA + $u * 1.40)
    $a6 = Pt ($cxA - $u * 0.48) ($cyA - $u * 0.10)
    $a7 = Pt ($cxA - $u * 1.45) ($cyA - $u * 0.10)
    $arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $arrow.AddPolygon(@($a1, $a2, $a3, $a4, $a5, $a6, $a7))
    $abf = New-Object System.Drawing.SolidBrush((C 255 10 150 132))
    $g.FillPath($abf, $arrow)
    $abf.Dispose(); $arrow.Dispose()
    $g.Restore($stc)
    Add-Finish $g $r $side 0 100 88
    $plate.Dispose(); $g.Dispose()
    return $bmp
}

$cands = @(
    @{ Key = 'A'; Name = 'A. Aurora A   violet-cyan geometric A' },
    @{ Key = 'B'; Name = 'B. Paper Plane   coral-rose, resume sent' },
    @{ Key = 'C'; Name = 'C. Bolt   amber-orange, instant' },
    @{ Key = 'D'; Name = 'D. Letter + Arrow   green-blue, applied' }
)
$masters = @{}
foreach ($c in $cands) {
    switch ($c.Key) {
        'A' { $m = Art-A 512 }
        'B' { $m = Art-B 512 }
        'C' { $m = Art-C 512 }
        'D' { $m = Art-D 512 }
    }
    $masters[$c.Key] = $m
    $m.Save((Join-Path $OutDir ("{0}-512.png" -f $c.Key)), [System.Drawing.Imaging.ImageFormat]::Png)
    foreach ($sz in @(64, 48, 32, 16)) {
        switch ($c.Key) {
            'A' { $b = Art-A $sz }
            'B' { $b = Art-B $sz }
            'C' { $b = Art-C $sz }
            'D' { $b = Art-D $sz }
        }
        $b.Save((Join-Path $OutDir ("{0}-{1}.png" -f $c.Key, $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
    }
}
$sheet = New-Bmp 1180 900
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'ClearTypeGridFit'
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 247 244 238))), 0, 0, 590, 900)
$g.FillRectangle((New-Object System.Drawing.SolidBrush((C 255 9 11 15))), 590, 0, 590, 900)
$fName = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
$bInk = New-Object System.Drawing.SolidBrush((C 255 26 29 35))
$bWht = New-Object System.Drawing.SolidBrush((C 255 236 240 240))
$rowY = 18
foreach ($c in $cands) {
    $m = $masters[$c.Key]
    $g.DrawImage($m, 34, $rowY, 168, 168)
    $g.DrawImage($m, 628, $rowY, 168, 168)
    foreach ($base in @(34, 628)) {
        $sx = $base
        foreach ($sz in @(64, 48, 32, 16)) {
            $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir ("{0}-{1}.png" -f $c.Key, $sz)))
            $g.DrawImage($img, $sx, ($rowY + 176), $sz, $sz)
            $img.Dispose()
            $sx += $sz + 12
        }
    }
    $g.DrawString($c.Name, $fName, $bInk, 214, ($rowY + 60))
    $g.DrawString($c.Name, $fName, $bWht, 808, ($rowY + 60))
    $rowY += 220
}
$g.Dispose()
$sheet.Save((Join-Path $OutDir 'sheet-all.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
foreach ($k in $masters.Keys) { $masters[$k].Dispose() }
"candidates -> $OutDir"
