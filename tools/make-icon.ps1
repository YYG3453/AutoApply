# Build AutoApply.ico: jade gradient ring (matches the in-app logo / tray icon).
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less files as ANSI.
param(
    [string]$Desktop,
    [string]$Assets = "",
    [string]$PreviewDir = ""
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

if (-not $Desktop) {
    $c1 = Join-Path $env:USERPROFILE 'Desktop'
    $c2 = Join-Path $env:USERPROFILE 'OneDrive\Desktop'
    if (Test-Path $c2) { $Desktop = $c2 } elseif (Test-Path $c1) { $Desktop = $c1 } else { throw "desktop not found" }
}
$root = Split-Path -Parent $PSScriptRoot
if (-not $Assets) { $Assets = Join-Path $root 'assets\app.ico' }

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

function New-Art([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = $s * 0.045
    $side = $s - $pad * 2
    $rect = New-Object System.Drawing.RectangleF([float]$pad, [float]$pad, [float]$side, [float]$side)
    $rad = [float]($side * 0.235)

    $plate = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $plate.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $plate.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $plate.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $plate.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $plate.CloseFigure()

    if ($s -ge 64) {
        for ($i = 3; $i -ge 1; $i--) {
            $dy = [float]($s * 0.010 * $i)
            $grow = [float]($s * 0.004 * $i)
            $rr = New-Object System.Drawing.RectangleF(
                [float]($rect.X - $grow), [float]($rect.Y - $grow + $dy),
                [float]($rect.Width + $grow * 2), [float]($rect.Height + $grow * 2))
            $sb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(14, 0, 0, 0))
            $g.FillEllipse($sb, $rr)
            $sb.Dispose()
        }
    }

    $cTop = [System.Drawing.Color]::FromArgb(255, 0x38, 0xB2, 0x91)
    $cMid = [System.Drawing.Color]::FromArgb(255, 0x1C, 0x8B, 0x71)
    $cBot = [System.Drawing.Color]::FromArgb(255, 0x06, 0x42, 0x38)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cTop, $cBot, 90.0)
    $cb = New-Object System.Drawing.Drawing2D.ColorBlend(3)
    $cb.Colors = @($cTop, $cMid, $cBot)
    $cb.Positions = @(0.0, 0.52, 1.0)
    $br.InterpolationColors = $cb
    $g.FillPath($br, $plate)

    $clip = $g.Save()
    $g.SetClip($plate, [System.Drawing.Drawing2D.CombineMode]::Intersect)

    $shRect = New-Object System.Drawing.RectangleF([float]($rect.X - $side), [float]($rect.Y - $side), [float]($side * 2), [float]($side * 2))
    $sh = New-Object System.Drawing.Drawing2D.LinearGradientBrush($shRect,
        [System.Drawing.Color]::FromArgb(46, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 55.0)
    $g.FillRectangle($sh, $rect)
    $sh.Dispose()

    # top rim light, mimics a polished stone edge
    $rimP = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(72, 255, 255, 255), [float]($side * 0.016))
    $rimP.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rimP.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($rimP, [float]($rect.X + $side * 0.135), [float]($rect.Y + $side * 0.020), [float]($rect.X + $side * 0.865), [float]($rect.Y + $side * 0.020))
    $rimP.Dispose()

    # 几何切角的 A：窄身、锐顶、平底，横杆压在 0.60 附近
    $ox = $rect.X; $oy = $rect.Y
    $outerPts = @(
        (New-Object System.Drawing.PointF([float]($ox + $side * 0.500), [float]($oy + $side * 0.200))),
        (New-Object System.Drawing.PointF([float]($ox + $side * 0.235), [float]($oy + $side * 0.785))),
        (New-Object System.Drawing.PointF([float]($ox + $side * 0.765), [float]($oy + $side * 0.785)))
    )
    $innerPts = @(
        (New-Object System.Drawing.PointF([float]($ox + $side * 0.500), [float]($oy + $side * 0.395))),
        (New-Object System.Drawing.PointF([float]($ox + $side * 0.360), [float]($oy + $side * 0.785))),
        (New-Object System.Drawing.PointF([float]($ox + $side * 0.640), [float]($oy + $side * 0.785)))
    )
    $pOut = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pOut.AddPolygon($outerPts)
    $pIn = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pIn.AddPolygon($innerPts)
    $reg = New-Object System.Drawing.Region($pOut)
    $reg.Exclude($pIn)
    $bar = New-Object System.Drawing.RectangleF([float]($ox + $side * 0.352), [float]($oy + $side * 0.575), [float]($side * 0.296), [float]($side * 0.070))
    $reg.Union($bar)
    $aRect = New-Object System.Drawing.RectangleF([float]$ox, [float]($oy + $side * 0.200), [float]$side, [float]($side * 0.585))
    $ab = New-Object System.Drawing.Drawing2D.LinearGradientBrush($aRect,
        [System.Drawing.Color]::White,
        [System.Drawing.Color]::FromArgb(255, 0xE6, 0xFB, 0xF4), 90.0)
    $g.FillRegion($ab, $reg)
    $ab.Dispose()
    $reg.Dispose()
    $pIn.Dispose()
    $pOut.Dispose()
    $g.Restore($clip)

    $inset = [float]($side * 0.030)
    $hair = New-Object System.Drawing.RectangleF([float]($rect.X + $inset), [float]($rect.Y + $inset), [float]($side - $inset * 2), [float]($side - $inset * 2))
    $hr = [float]($rad * 0.90)
    $hd = $hr * 2
    $hairPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hairPath.AddArc($hair.X, $hair.Y, $hd, $hd, 180, 90)
    $hairPath.AddArc($hair.Right - $hd, $hair.Y, $hd, $hd, 270, 90)
    $hairPath.AddArc($hair.Right - $hd, $hair.Bottom - $hd, $hd, $hd, 0, 90)
    $hairPath.AddArc($hair.X, $hair.Bottom - $hd, $hd, $hd, 90, 90)
    $hairPath.CloseFigure()
    $penHair = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 0xE3, 0xC2, 0x8C), [float]([Math]::Max(0.75, $side * 0.0075)))
    $g.DrawPath($penHair, $hairPath)

    $penHair.Dispose()
    $hairPath.Dispose()
    $br.Dispose()
    $plate.Dispose()
    $g.Dispose()
    return $bmp
}
function Get-BmpBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width
    $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER
    $bw.Write([int]40)          # biSize
    $bw.Write([int]$w)          # biWidth
    $bw.Write([int]($h * 2))    # biHeight (XOR + AND)
    $bw.Write([int16]1)         # biPlanes
    $bw.Write([int16]32)        # biBitCount
    $bw.Write([int]0)           # biCompression = BI_RGB
    $bw.Write([int]($w * $h * 4)) # biSizeImage
    $bw.Write([int]0)           # biXPelsPerMeter
    $bw.Write([int]0)           # biYPelsPerMeter
    $bw.Write([int]0)           # biClrUsed
    $bw.Write([int]0)           # biClrImportant
    # XOR bitmap: BGRA bottom-up
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    # AND mask: 1 bit per pixel, 1 = transparent
    $stride = [int](([Math]::Floor(($w + 31) / 32)) * 4)
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $stride
        for ($x = 0; $x -lt $w; $x++) {
            if ($bmp.GetPixel($x, $y).A -lt 128) {
                $row[[int]([Math]::Floor($x / 8))] = [byte]($row[[int]([Math]::Floor($x / 8))] -bor (0x80 -shr ($x % 8)))
            }
        }
        $bw.Write([byte[]]$row)
    }
    $bw.Flush()
    return $ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$frames = @()
foreach ($s in $sizes) {
    $art = New-Art $s
    if ($PreviewDir) {
        if (-not (Test-Path $PreviewDir)) { New-Item -ItemType Directory -Path $PreviewDir | Out-Null }
        $art.Save((Join-Path $PreviewDir ("icon-{0}.png" -f $s)), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    # All frames are 32bpp DIB: System.Drawing.Icon (.NET Framework) cannot decode
    # PNG-compressed frames, and this file doubles as the exe's /win32icon.
    $data = Get-BmpBytes $art
    $frames += [pscustomobject]@{ Size = $s; Data = $data }
    $art.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([int16]0)                    # reserved
$bw.Write([int16]1)                    # type = icon
$bw.Write([int16]$frames.Count)        # count
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = $f.Size
    if ($dim -ge 256) { $dim = 0 }
    $bw.Write([byte]$dim)              # width
    $bw.Write([byte]$dim)              # height
    $bw.Write([byte]0)                 # colors
    $bw.Write([byte]0)                 # reserved
    $bw.Write([int16]1)                # planes
    $bw.Write([int16]32)               # bpp
    $bw.Write([int]$f.Data.Length)
    $bw.Write([int]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f.Data) }
$bw.Flush()
$bytes = $ms.ToArray()

if (-not (Test-Path $Desktop)) { New-Item -ItemType Directory -Path $Desktop | Out-Null }
$desktopIco = Join-Path $Desktop 'AutoApply.ico'
[System.IO.File]::WriteAllBytes($desktopIco, $bytes)

$assetsDir = Split-Path -Parent $Assets
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir | Out-Null }
[System.IO.File]::WriteAllBytes($Assets, $bytes)

# verify it really loads as an icon
$ico = New-Object System.Drawing.Icon($desktopIco)
$bmp = $ico.ToBitmap()
"OK  {0}  {1:N1} KB  frames={2}  loaded={3}x{4}" -f $desktopIco, ($bytes.Length / 1KB), $frames.Count, $ico.Width, $ico.Height
"OK  {0}" -f $Assets
"    sizes = {1}" -f '', ($sizes -join ', ')
$bmp.Dispose()
$ico.Dispose()
