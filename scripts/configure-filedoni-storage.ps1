#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$destination = "C:\inetpub\Filedoni"
$settingsPath = Join-Path $destination "appsettings.Production.json"

if (-not (Test-Path -LiteralPath $destination)) {
    throw "Filedoni is not deployed at $destination."
}

$endpoint = Read-Host "Arvan endpoint [https://s3.ir-thr-at1.arvanstorage.ir]"
if ([string]::IsNullOrWhiteSpace($endpoint)) {
    $endpoint = "https://s3.ir-thr-at1.arvanstorage.ir"
}

$region = Read-Host "Arvan region [ir-thr-at1]"
if ([string]::IsNullOrWhiteSpace($region)) {
    $region = "ir-thr-at1"
}

$bucket = Read-Host "Bucket name"
$accessKey = Read-Host "Access key"
$secretKeySecure = Read-Host "Secret key" -AsSecureString
$secretKeyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secretKeySecure)

try {
    $secretKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretKeyPointer)
    $settings = @{
        ArvanStorage = @{
            Endpoint = $endpoint
            Region = $region
            BucketName = $bucket
            AccessKey = $accessKey
            SecretKey = $secretKey
            FilesPrefix = "filedoni/files"
            MaxUploadBytes = 104857600
        }
    }

    $settings | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    & icacls $settingsPath /inheritance:r /grant:r "SYSTEM:F" "Administrators:F" "IIS AppPool\FiledoniPool:R" | Out-Null

    Import-Module WebAdministration
    Restart-WebAppPool -Name "FiledoniPool"
    Write-Host "Filedoni production storage settings were saved and the app pool was restarted."
}
finally {
    if ($secretKeyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretKeyPointer)
    }
}
