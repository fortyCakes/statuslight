# Publishes StatusLight and registers a Scheduled Task that starts it at logon. Re-run it after changing the code.
#
# The binaries go to %LOCALAPPDATA%\StatusLight so they don't lock the repo's build output, but the working directory
# is the repo's src\StatusLight.Host, so appsettings.json and appsettings.Local.json are read from the repo and still
# reload live.
#
# Use -Uninstall to remove the task and the published files.
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'

$taskName = 'StatusLight'
$installDir = Join-Path $env:LOCALAPPDATA 'StatusLight'
$hostDir = Resolve-Path (Join-Path $PSScriptRoot '..\src\StatusLight.Host')
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

function Stop-StatusLight {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName
    }
    # Also catches a copy started by hand, e.g. with dotnet run.
    Get-Process -Name StatusLight -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Stop-StatusLight

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
    if (Test-Path $installDir) {
        Remove-Item -Recurse -Force $installDir
    }
    Write-Host 'StatusLight startup task removed.'
    return
}

& $dotnet publish (Join-Path $hostDir 'StatusLight.Host.csproj') -c Release -o $installDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$action = New-ScheduledTaskAction -Execute (Join-Path $installDir 'StatusLight.exe') -WorkingDirectory $hostDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description 'Drives the BlinkStick status light for Claude Code sessions.' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

Write-Host "StatusLight installed to $installDir and started. It will start at every logon."
