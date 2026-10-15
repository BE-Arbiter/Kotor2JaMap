<#
.SYNOPSIS
  Builds the portable Windows x64 package: artifacts/Kotor2Ja-win-x64.zip (application + command line tool, self-contained).
.PARAMETER SingleFile
  $true (default) publishes single-file executables. Use -SingleFile:$false for a self-contained folder instead.
#>
param([bool]$SingleFile = $true)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publish = Join-Path $root 'publish'
$artifacts = Join-Path $root 'artifacts'
$staging = Join-Path $publish 'package'
Remove-Item $publish, $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging, $artifacts | Out-Null

$single = if ($SingleFile) { 'true' } else { 'false' }
foreach ($p in @(
    @{ Project = 'src/Kotor2Ja.App/Kotor2Ja.App.csproj'; Out = 'app' },
    @{ Project = 'src/Kotor2Ja.Cli/Kotor2Ja.Cli.csproj'; Out = 'cli' })) {
    $out = Join-Path $publish $p.Out
    dotnet publish $p.Project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=$single -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($p.Project)" }
}

# The application folder is the base (it carries the native libraries in non-single-file mode); the CLI is added on top.
Copy-Item (Join-Path $publish 'app/*') $staging -Recurse -Force
Copy-Item (Join-Path $publish 'cli/*') $staging -Recurse -Force
# The development configuration (machine-specific paths) is not shipped: the example file is.
Remove-Item (Join-Path $staging 'kotor2ja.config.json') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $staging '*.pdb') -ErrorAction SilentlyContinue
Copy-Item 'src/Kotor2Ja.Cli/kotor2ja.config.example.json' $staging -Force
Copy-Item 'README.md', 'THIRD-PARTY-NOTICES.md' $staging -Force

$zip = Join-Path $artifacts 'Kotor2Ja-win-x64.zip'
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
Get-ChildItem $staging | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
"{0}: {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB)
