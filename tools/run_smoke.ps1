$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source='E:\projects\bf2042-scmod\vendor\SurvivalcraftApi\Survivalcraft.Windows\bin\Debug'
$runtime=Join-Path $root 'local/runtime'
New-Item -ItemType Directory -Force -Path $runtime,(Join-Path $runtime 'Mods') | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.dll','.exe','.json','.zip','.js' } | Copy-Item -Destination $runtime
if(Test-Path -LiteralPath (Join-Path $source 'runtimes')) {Copy-Item -LiteralPath (Join-Path $source 'runtimes') -Destination $runtime -Recurse -Force}
dotnet build (Join-Path $root 'Tests/RuntimeSmoke/RuntimeSmoke.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Smoke build failed'}
Copy-Item -LiteralPath (Join-Path $root 'dist/MechDefense-0.1.0.scmod') -Destination (Join-Path $runtime 'Mods') -Force
$staging=Join-Path $root 'local/smoke-package'
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Tests/RuntimeSmoke/bin/Release/net10.0/RuntimeSmoke.dll') -Destination $staging -Force
'{"Name":"MechDefense isolated verification","Version":"1.0.0","ApiVersion":"1.9.3.1","ScVersion":"2.4","PackageName":"local.mechdefense.smoke","Author":"Tests","Description":"Isolated test only","LoadOrder":1200,"Dependencies":{"local.mechdefense.sample":"0.1.0"}}' | Set-Content -LiteralPath (Join-Path $staging 'modinfo.json') -Encoding utf8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $runtime 'Mods/RuntimeSmoke.scmod'
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging,$zip)
$p=Start-Process -FilePath (Join-Path $runtime 'Survivalcraft.exe') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru
$p.Id | Set-Content -LiteralPath (Join-Path $root 'local/smoke-pid.txt')
Write-Output "Isolated game PID $($p.Id), results in $runtime"
