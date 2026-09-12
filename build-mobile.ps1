param([string]$JavaHome = $env:JAVA_HOME)
$ErrorActionPreference = 'Stop'
$previousGradle = $env:GRADLE_USER_HOME
$previousJava = $env:JAVA_HOME
$previousAndroid = $env:ANDROID_USER_HOME
try {
    $env:ANDROID_USER_HOME = Join-Path $PSScriptRoot ".tools/android-user"
    New-Item -ItemType Directory -Path $env:ANDROID_USER_HOME -Force | Out-Null
    $env:GRADLE_USER_HOME = Join-Path $PSScriptRoot '.tools/gradle-home'
    if ($JavaHome) { $env:JAVA_HOME = $JavaHome }
    & (Join-Path $PSScriptRoot 'Mobile app/gradlew.bat') -p (Join-Path $PSScriptRoot 'Mobile app') assembleDebug --console=plain '-Pkotlin.compiler.execution.strategy=in-process'
    exit $LASTEXITCODE
} finally {
    $env:GRADLE_USER_HOME = $previousGradle
    $env:JAVA_HOME = $previousJava
    $env:ANDROID_USER_HOME = $previousAndroid
}
