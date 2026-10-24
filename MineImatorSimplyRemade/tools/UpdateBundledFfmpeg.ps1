param(
    [string]$CacheDirectory = [System.IO.Path]::GetTempPath()
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Maintainer-only packaging step. The application never downloads FFmpeg.
$version = '9.0.2'
$expectedHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$version-essentials_build.zip"
$archivePath = Join-Path $CacheDirectory "ffmpeg-$version-essentials_build.zip"
$assetDirectory = Join-Path $PSScriptRoot '..\assets\ffmpeg'
$outputPath = Join-Path $assetDirectory 'win-x64.zip'

if (-not (Test-Path -LiteralPath $CacheDirectory -PathType Container)) {
    throw "Cache directory does not exist: $CacheDirectory"
}
if (-not (Test-Path -LiteralPath $archivePath)) {
    & curl.exe --fail --location --retry 3 --output $archivePath $url
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed.' }
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw "FFmpeg archive checksum mismatch: $archivePath"
}

[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$inputArchive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
$temporaryPath = "$outputPath.tmp"
try {
    $outputStream = [System.IO.File]::Create($temporaryPath)
    $outputArchive = [System.IO.Compression.ZipArchive]::new($outputStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        # Omit ffprobe, ffplay and HTML documentation from the embedded payload.
        foreach ($relativePath in @('bin/ffmpeg.exe', 'LICENSE', 'README.txt')) {
            $entry = $inputArchive.GetEntry("ffmpeg-$version-essentials_build/$relativePath")
            if ($null -eq $entry) { throw "Missing archive entry: $relativePath" }
            $name = [System.IO.Path]::GetFileName($relativePath)
            if ($name -eq 'LICENSE') { $name = 'LICENSE.txt' }
            $target = $outputArchive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $target.LastWriteTime = $entry.LastWriteTime
            $sourceStream = $entry.Open()
            $targetStream = $target.Open()
            try { $sourceStream.CopyTo($targetStream) }
            finally { $targetStream.Dispose(); $sourceStream.Dispose() }
        }
        $noticeEntry = $outputArchive.CreateEntry('SOURCE.txt')
        $noticeEntry.LastWriteTime = $entry.LastWriteTime
        $writer = [System.IO.StreamWriter]::new($noticeEntry.Open())
        try {
            $writer.WriteLine("FFmpeg $version Windows x64 Essentials, built by Gyan Doshi.")
            $writer.WriteLine("Original binary distribution: $url")
            $writer.WriteLine("Original archive SHA-256: $expectedHash")
            $writer.WriteLine('FFmpeg is a separate command-line program, licensed under GPLv3 or later.')
            $writer.WriteLine('See LICENSE.txt and README.txt for license, build configuration and library versions.')
            $writer.WriteLine("FFmpeg source: https://ffmpeg.org/releases/ffmpeg-$version.tar.xz")
            $writer.WriteLine('Build provider and external library information: https://www.gyan.dev/ffmpeg/builds/')
        }
        finally { $writer.Dispose() }
    }
    finally { $outputArchive.Dispose(); $outputStream.Dispose() }
    Move-Item -LiteralPath $temporaryPath -Destination $outputPath -Force
}
finally {
    $inputArchive.Dispose()
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
}

Get-Item -LiteralPath $outputPath
Get-FileHash -LiteralPath $outputPath -Algorithm SHA256
