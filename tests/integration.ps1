param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$log = Join-Path $repo 'bin/ssh-test-port.txt'
$err = Join-Path $repo 'bin/ssh-test-errors.txt'
$server = Start-Process -FilePath $Python -ArgumentList ('"' + (Join-Path $PSScriptRoot 'ssh_server.py') + '"') -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $err -PassThru
try {
    $port = $null
    for ($i = 0; $i -lt 100; $i++) {
        if ($server.HasExited) { throw (Get-Content $err -Raw) }
        $port = Get-Content $log -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($port -match '^\d+$') { break }
        Start-Sleep -Milliseconds 100
    }
    if ($port -notmatch '^\d+$') { throw 'SSH fixture did not start.' }
    & (Join-Path $repo 'bin/ShellTree.Tests.exe') $port
    if ($LASTEXITCODE -ne 0) { throw 'SSH integration tests failed.' }
} finally { if (!$server.HasExited) { Stop-Process -Id $server.Id } }
