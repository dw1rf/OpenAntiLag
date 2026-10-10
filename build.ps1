param([string]$OutputDirectory = "dist")
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required (Windows 10/11).' }
$dist = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xml.dll','/r:System.Management.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $dist 'OpenAntiLag.exe')) ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\OpenAntiLag.ico')) ('/resource:' + (Join-Path $PSScriptRoot 'assets\OpenAntiLag.ico') + ',OpenAntiLag.AppIcon') @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
$testSource = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /target:exe /platform:x64 /utf8output /main:OpenAntiLag.Tests ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $dist 'OpenAntiLag.Tests.exe')) ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\OpenAntiLag.ico')) ('/resource:' + (Join-Path $PSScriptRoot 'assets\OpenAntiLag.ico') + ',OpenAntiLag.AppIcon') @references @sources $testSource
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $dist 'OpenAntiLag.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Output ('Built: ' + (Join-Path $dist 'OpenAntiLag.exe'))
