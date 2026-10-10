$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'assets\OpenAntiLag.png'))
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@()
try {
    foreach($size in $sizes) {
        $bitmap=New-Object Drawing.Bitmap($size,$size)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source,0,0,$size,$size)
        $memory=New-Object IO.MemoryStream
        $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
        $frames+=,@($memory.ToArray())
        $graphics.Dispose();$bitmap.Dispose();$memory.Dispose()
    }
    $file=[IO.File]::Create((Join-Path $PSScriptRoot 'assets\OpenAntiLag.ico'))
    $writer=New-Object IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
        $offset=6+16*$sizes.Count
        for($i=0;$i -lt $sizes.Count;$i++) {
            $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
            $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
            $offset+=$frames[$i].Length
        }
        foreach($frame in $frames){$writer.Write([byte[]]$frame)}
    }finally{$writer.Dispose();$file.Dispose()}
}finally{$source.Dispose()}
