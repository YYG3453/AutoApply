Add-Type -AssemblyName System.Drawing
function C([int]$a, [int]$r, [int]$g, [int]$b) { return [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
$c1 = C 70 120 30 10
$c2 = C 80 255 240 210
Write-Output ("c1 null? " + ($null -eq $c1) + " -> " + $c1)
Write-Output ("c2 null? " + ($null -eq $c2) + " -> " + $c2)
function Test-Pen([int]$s) {
    $cHL = C 80 255 240 210
    Write-Output ("infunc cHL null? " + ($null -eq $cHL) + " -> " + $cHL)
    return $cHL
}
$r = Test-Pen 512
Write-Output ("ret " + $r)
