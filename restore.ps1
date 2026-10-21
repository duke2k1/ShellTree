param([string]$NodePath)
$ErrorActionPreference = 'Stop'
$packages = Join-Path $PSScriptRoot '.packages'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $packages,$out | Out-Null
$lock = Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
foreach ($package in $lock) {
    $name = "$($package.id).$($package.version)"
    $zip = Join-Path $packages "$name.zip"
    if (!(Test-Path $zip)) {
        $id = $package.id.ToLowerInvariant()
        $url = "https://api.nuget.org/v3-flatcontainer/$id/$($package.version)/$id.$($package.version).nupkg"
        if ($NodePath) {
            & $NodePath -e 'fetch(process.argv[1]).then(async r=>{if(!r.ok)throw Error(r.status);require("fs").writeFileSync(process.argv[2],Buffer.from(await r.arrayBuffer()))}).catch(e=>{console.error(e);process.exit(1)})' $url $zip
            if ($LASTEXITCODE -ne 0) { throw "Download failed: $name" }
        } else {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
        }
    }
    if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) { throw "Package checksum mismatch: $name" }
    $expanded = Join-Path $packages $name
    Expand-Archive $zip $expanded -Force
    Get-ChildItem (Join-Path $expanded "lib/$($package.framework)") -Filter *.dll | Copy-Item -Destination $out -Force
}
# Redirect dependency versions to the exact assemblies bundled with the app.
$bindings = Get-ChildItem $out -Filter *.dll | ForEach-Object {
    $a = [Reflection.AssemblyName]::GetAssemblyName($_.FullName)
    $token = -join ($a.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') })
    '<dependentAssembly><assemblyIdentity name="' + $a.Name + '" publicKeyToken="' + $token + '" culture="neutral"/><bindingRedirect oldVersion="0.0.0.0-' + $a.Version + '" newVersion="' + $a.Version + '"/></dependentAssembly>'
}
$config = '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">' + ($bindings -join '') + '</assemblyBinding></runtime></configuration>'
$config | Set-Content (Join-Path $out 'ShellTree.exe.config') -Encoding UTF8
$config | Set-Content (Join-Path $out 'ShellTree.Tests.exe.config') -Encoding UTF8
