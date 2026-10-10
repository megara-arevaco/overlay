param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src/GameChatOverlay/GameChatOverlay.csproj'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$packageRoot = Join-Path $repositoryRoot "artifacts/local-$stamp/$RuntimeIdentifier"
$publishDirectory = Join-Path $packageRoot 'publish'
$archivePath = Join-Path $packageRoot "Agripa-$RuntimeIdentifier.zip"
$checksumPath = "$archivePath.sha256"

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet publish $project `
    -c Release -r $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$executable = Join-Path $publishDirectory 'Agripa.exe'
if (-not (Test-Path $executable)) { throw "Publish succeeded but Agripa.exe is missing: $executable" }

@'
Agripa local Windows build

Extract this folder and run Agripa.exe. This build is unsigned and is not an installer.
Microsoft Edge WebView2 Evergreen Runtime and internet access are required for the embedded website.
Settings and the WebView2 browser profile are stored under your Local AppData folder.
The package does not include ChatGPT access, automatic updates, or a compatibility guarantee for games or anti-cheat software.
'@ | Set-Content -Path (Join-Path $publishDirectory 'README-local.txt') -Encoding UTF8

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash
"$hash  $(Split-Path -Leaf $archivePath)" | Set-Content -Path $checksumPath -Encoding ASCII

Write-Host "Local package: $archivePath"
Write-Host "SHA-256:      $checksumPath"
Write-Host 'This package is unsigned. Verify it and run the Windows E2E test before sharing.'
