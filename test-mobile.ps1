param([Parameter(Mandatory = $true)][string]$JavaHome)
$ErrorActionPreference = 'Stop'
$stdlib = Get-ChildItem -Path (Join-Path $PSScriptRoot '.tools/gradle-home/caches/modules-2/files-2.1/org.jetbrains.kotlin/kotlin-stdlib/2.1.20/*/kotlin-stdlib-2.1.20.jar') | Select-Object -First 1
if (!$stdlib) { throw 'Build the Android app first with build-mobile.ps1.' }
$classes = Join-Path $PSScriptRoot 'Mobile app/app/build/tmp/kotlin-classes/debug'
$out = Join-Path $PSScriptRoot '.tools/input-tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$classpath = "$classes;$($stdlib.FullName)"
& (Join-Path $JavaHome 'bin/javac.exe') -cp $classpath -d $out (Join-Path $PSScriptRoot 'Mobile app/tests/InputSmoke.java')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $JavaHome 'bin/java.exe') -cp "$out;$classpath" InputSmoke
exit $LASTEXITCODE
