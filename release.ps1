<#
.SYNOPSIS
  Builds a portable Big Phone release and publishes it on GitHub.

.EXAMPLE
  .\release.ps1 -Version 1.0.1            # build, tag v1.0.1, push, create GitHub release
  .\release.ps1 -Version 1.0.1 -NoPublish # only build dist\BigPhone-1.0.1-win-x64.zip

  Before running: add a "## 1.0.1" section to CHANGELOG.md describing the changes.
  Installed copies of Big Phone pick the release up automatically (at startup or 2-5 AM).
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$NoPublish
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }

# 1. Stamp the version into the project
$proj = 'src\BigPhone.csproj'
(Get-Content $proj -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" | Set-Content $proj -NoNewline -Encoding utf8

# 2. Self-contained single-file exe (no .NET install needed on the target PC)
Remove-Item publish, "dist\stage" -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o publish
if ($LASTEXITCODE) { throw "publish failed" }

# 3. Portable zip: exe + marker file so settings stay next to the exe
$name = "BigPhone-$Version-win-x64.zip"
New-Item -ItemType Directory -Force dist\stage | Out-Null
Copy-Item publish\BigPhone.exe dist\stage\
Set-Content dist\stage\BigPhone.portable "Settings and logs are stored in this folder. Delete this file to use %APPDATA%\BigPhone instead."
Copy-Item README.md, CHANGELOG.md dist\stage\
Remove-Item "dist\$name" -ErrorAction SilentlyContinue
Compress-Archive dist\stage\* "dist\$name"
$hash = (Get-FileHash "dist\$name" -Algorithm SHA256).Hash.ToLower()
Set-Content "dist\$name.sha256" "$hash  $name" -NoNewline
Remove-Item dist\stage -Recurse -Force
Write-Host "Built dist\$name ($hash)"
if ($NoPublish) { return }

# 4. Release notes = this version's section of CHANGELOG.md
$log = Get-Content CHANGELOG.md -Raw
$m = [regex]::Match($log, "(?ms)^## \[?$([regex]::Escape($Version))\]?[^\n]*\n(.*?)(?=^## |\z)")
if (-not $m.Success) { throw "Add a '## $Version' section to CHANGELOG.md first" }
$notes = $m.Groups[1].Value.Trim()

# 5. Commit, tag, push, release
git add -A
git commit -m "Release $Version" | Out-Null
git tag "v$Version"
git push
git push origin "v$Version"
gh release create "v$Version" "dist\$name" "dist\$name.sha256" --title "Big Phone $Version" --notes $notes
Write-Host "Released v$Version"
