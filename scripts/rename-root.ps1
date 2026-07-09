$ErrorActionPreference = "Stop"

$source = "F:\Commandoni"
$target = "F:\Alldoni"

if (-not (Test-Path -LiteralPath $source)) {
    throw "Source folder does not exist: $source"
}
if (Test-Path -LiteralPath $target) {
    throw "Target folder already exists: $target"
}

Set-Location "F:\"
Rename-Item -LiteralPath $source -NewName "Alldoni"
Write-Host "Repository renamed to $target"
