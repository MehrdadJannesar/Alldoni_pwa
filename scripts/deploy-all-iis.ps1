#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"
trap {
    $_ | Out-File -FilePath "C:\tmp\alldoni-iis-deploy-error.log" -Force
    exit 1
}
Import-Module WebAdministration

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$applications = @(
    @{
        Name = "Alldoni"
        Port = 5050
        Source = Join-Path $repositoryRoot "Alldoni\publish"
    },
    @{
        Name = "Commandoni"
        Port = 5094
        Source = Join-Path $repositoryRoot "Commandoni\publish"
    },
    @{
        Name = "Linkdoni"
        Port = 5165
        Source = Join-Path $repositoryRoot "Linkdoni\publish"
    },
    @{
        Name = "Filedoni"
        Port = 5276
        Source = Join-Path $repositoryRoot "Filedoni\publish"
    }
)
$productionSettingsByName = @{}

foreach ($application in $applications) {
    $name = $application.Name
    $pool = "${name}Pool"
    $destination = "C:\inetpub\$name"
    $productionSettingsPath = Join-Path $destination "appsettings.Production.json"
    $productionSettingsByName[$name] = if (Test-Path -LiteralPath $productionSettingsPath) {
        [System.IO.File]::ReadAllBytes($productionSettingsPath)
    } else {
        $null
    }

    if (-not (Test-Path -LiteralPath (Join-Path $application.Source "web.config"))) {
        throw "Publish output is missing for $name at $($application.Source)."
    }

    if ((Test-Path "IIS:\Sites\$name") -and (Get-WebsiteState -Name $name).Value -ne "Stopped") {
        Stop-Website -Name $name
    }
    if ((Test-Path "IIS:\AppPools\$pool") -and (Get-WebAppPoolState -Name $pool).Value -ne "Stopped") {
        Stop-WebAppPool -Name $pool
    }
}

Start-Sleep -Seconds 2

foreach ($application in $applications) {
    $name = $application.Name
    $pool = "${name}Pool"
    $destination = "C:\inetpub\$name"
    $productionSettingsPath = Join-Path $destination "appsettings.Production.json"
    $productionSettings = $productionSettingsByName[$name]

    Get-CimInstance Win32_Process -Filter "Name = 'w3wp.exe'" |
        Where-Object { $_.CommandLine -like "*-ap `"$pool`"*"} |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path (Join-Path $application.Source "*") -Destination $destination -Recurse -Force
    if ($null -ne $productionSettings) {
        [System.IO.File]::WriteAllBytes($productionSettingsPath, $productionSettings)
    }
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot "Commandoni\wwwroot\favicon.ico") `
        -Destination (Join-Path $destination "wwwroot\favicon.ico") `
        -Force

    if (-not (Test-Path "IIS:\AppPools\$pool")) {
        New-WebAppPool -Name $pool | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$pool" -Name startMode -Value "AlwaysRunning"

    if (Test-Path "IIS:\Sites\$name") {
        Remove-Website -Name $name
    }
    New-Website `
        -Name $name `
        -PhysicalPath $destination `
        -Port $application.Port `
        -ApplicationPool $pool | Out-Null

    Set-ItemProperty "IIS:\Sites\$name" -Name applicationDefaults.preloadEnabled -Value $true
    & icacls $destination /grant "IIS AppPool\${pool}:(OI)(CI)M" /T /C | Out-Null

    Start-WebAppPool -Name $pool
    Start-Website -Name $name
    Write-Host "$name is available at http://localhost:$($application.Port)/"
}
