$ErrorActionPreference = 'Stop'

$compilerCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw '找不到 .NET Framework 4.x C# 编译器。'
}

$source = Join-Path $PSScriptRoot 'src\LanSwitch.cs'
$outputDirectory = Join-Path $PSScriptRoot 'dist'
$output = Join-Path $outputDirectory 'LanSwitch.exe'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $compiler `
    /nologo `
    /optimize+ `
    /target:winexe `
    "/out:$output" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.ServiceProcess.dll `
    /reference:System.Management.dll `
    /reference:System.Configuration.Install.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出代码：$LASTEXITCODE"
}

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
Get-FileHash -LiteralPath $output -Algorithm SHA256
