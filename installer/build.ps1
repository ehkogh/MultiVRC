param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\MultiVRC.App\MultiVRC.App.csproj'
$out = Join-Path $PSScriptRoot 'out'
$publish = Join-Path $out 'publish'

if (-not $Version) { $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if (-not $Version) { throw "No <Version> in $project; pass -Version." }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }

Write-Host "MultiVRC $Version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish --nologo -v q
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Get-ChildItem $publish -Filter *.pdb -Recurse | Remove-Item

& $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$($Version.Split('-')[0])" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'MultiVRC.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }

$zip = Join-Path $out 'MultiVRC-win-x64.zip'
Compress-Archive -Path "$publish\*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  MultiVRC-win-x64.zip" | Out-File -Encoding ascii -NoNewline "$zip.sha256"

Get-ChildItem $out -File | ForEach-Object { '{0,-36} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
