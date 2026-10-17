$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory=Join-Path $PSScriptRoot '..\src\QuickMenu\Assets'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
$images=New-Object 'System.Collections.Generic.List[byte[]]'
foreach($size in @(16,32,48,64,128,256)) {
    $bitmap=New-Object System.Drawing.Bitmap $size,$size
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    # Clay rounded square with an ivory eight-ray spark (the app's own mark).
    $brush=New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(217,119,87))
    $inset=[single]([Math]::Max(0.5,$size*0.03)); $edge=[single]($size-2*$inset); $radius=[single]($size*0.24)
    $shape=New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc($inset,$inset,2*$radius,2*$radius,180,90); $shape.AddArc($inset+$edge-2*$radius,$inset,2*$radius,2*$radius,270,90)
    $shape.AddArc($inset+$edge-2*$radius,$inset+$edge-2*$radius,2*$radius,2*$radius,0,90); $shape.AddArc($inset,$inset+$edge-2*$radius,2*$radius,2*$radius,90,90)
    $shape.CloseFigure(); $graphics.FillPath($brush,$shape)
    $ivory=[System.Drawing.Color]::FromArgb(250,249,245); $center=[single]($size/2)
    # Small sizes need heavier strokes to stay legible in the tray.
    $weight=if($size -le 32){1.6}else{1}
    foreach($ray in @(@{Angle=0;Length=0.34;Width=0.062},@{Angle=45;Length=0.26;Width=0.052})) {
        $pen=New-Object System.Drawing.Pen $ivory,([single][Math]::Max(1.5,$size*$ray.Width*$weight))
        $pen.StartCap=[System.Drawing.Drawing2D.LineCap]::Round; $pen.EndCap=[System.Drawing.Drawing2D.LineCap]::Round
        foreach($quarter in 0..3) {
            $angle=($ray.Angle+90*$quarter)*[Math]::PI/180; $from=$size*0.1; $to=$size*$ray.Length
            $graphics.DrawLine($pen,[single]($center+[Math]::Cos($angle)*$from),[single]($center+[Math]::Sin($angle)*$from),[single]($center+[Math]::Cos($angle)*$to),[single]($center+[Math]::Sin($angle)*$to))
        }
        $pen.Dispose()
    }
    $stream=New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose(); $shape.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output=[System.IO.File]::Create((Join-Path $assetDirectory 'quick-menu.ico'))
$writer=New-Object System.IO.BinaryWriter $output
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
$offset=6+16*$images.Count
$sizes=@(16,32,48,64,128,256)
for($index=0;$index -lt $images.Count;$index++) {
    $dimension=if($sizes[$index] -eq 256){0}else{$sizes[$index]}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$index].Length);$writer.Write([uint32]$offset)
    $offset+=$images[$index].Length
}
foreach($image in $images){$writer.Write($image)}
$writer.Dispose();$output.Dispose()
