param([switch]$Run, [switch]$Test, [ValidateSet("Debug", "Release")][string]$Configuration = "Release")
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$localDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
$previousEnv = @{}
foreach ($name in @("DOTNET_CLI_HOME", "APPDATA", "NUGET_PACKAGES", "DOTNET_CLI_TELEMETRY_OPTOUT")) { $previousEnv[$name] = [Environment]::GetEnvironmentVariable($name, "Process") }
try {
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:APPDATA = Join-Path $taskRoot '.tools/appdata'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
$project = Join-Path $taskRoot 'Desktop app/LocalMouse.Desktop.csproj'
if ($Test) { $project = Join-Path $taskRoot 'Desktop app/tests/ProtocolSmoke.csproj' }
& $dotnet restore $project --configfile (Join-Path $taskRoot 'Desktop app/NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build $project --no-restore --configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Run -or $Test) { & $dotnet run --project $project --no-build --no-restore --configuration $Configuration }
exit $LASTEXITCODE

} finally {
foreach ($name in $previousEnv.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnv[$name], "Process") }
}
