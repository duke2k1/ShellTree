param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { throw '.NET Framework 4.8 developer/compiler tools are required.' }
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /out:"$out\ShellTree.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    & $compiler /nologo /target:exe /main:ShellTree.Tests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /out:"$out\ShellTree.Tests.exe" $sources (Join-Path $PSScriptRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & "$out\ShellTree.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Host "Built $out\ShellTree.exe"
