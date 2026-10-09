$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source='E:\projects\bf2042-scmod\vendor\SurvivalcraftApi\Survivalcraft.Windows\bin\Debug'
$runtime=Join-Path $root 'local/runtime-endfield-fixes'
$version=(Get-Content (Join-Path $root 'Mod/modinfo.json') -Raw | ConvertFrom-Json).Version
New-Item -ItemType Directory -Force -Path $runtime,(Join-Path $runtime 'Mods') | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.dll','.exe','.json','.zip','.js' } | Copy-Item -Destination $runtime -Force
if(Test-Path -LiteralPath (Join-Path $source 'runtimes')) {Copy-Item -LiteralPath (Join-Path $source 'runtimes') -Destination $runtime -Recurse -Force}
dotnet build (Join-Path $root 'Tests/EndfieldSmoke/EndfieldSmoke.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Endfield test build failed'}
Copy-Item -LiteralPath (Join-Path $root "dist/MechDefense-$version.scmod") -Destination (Join-Path $runtime 'Mods') -Force
$infoMod=Get-ChildItem -LiteralPath 'D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods' -Filter '*Neorxna-v1.4*' | Select-Object -First 1
if($infoMod){Copy-Item -LiteralPath $infoMod.FullName -Destination (Join-Path $runtime 'Mods/Neorxna.scmod') -Force}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath=Join-Path $runtime 'Mods/EndfieldSmoke.scmod'
if(Test-Path -LiteralPath $zipPath){Move-Item -LiteralPath $zipPath -Destination ($zipPath+'.'+[guid]::NewGuid().ToString()+'.previous')}
$zip=[System.IO.Compression.ZipFile]::Open($zipPath,[System.IO.Compression.ZipArchiveMode]::Create)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $root 'Tests/EndfieldSmoke/bin/Release/net10.0/EndfieldSmoke.dll'),'EndfieldSmoke.dll') | Out-Null
    $entry=$zip.CreateEntry('modinfo.json');$writer=[System.IO.StreamWriter]::new($entry.Open())
    $info=@{Name='Endfield isolated verification';Version='1.0.0';ApiVersion='1.9.3.1';ScVersion='2.4';PackageName='local.endfield.smoke';Author='Tests';Description='Isolated tests only';LoadOrder=1200;Dependencies=@{'local.mechdefense.sample'=$version}}
    try {$writer.Write(($info | ConvertTo-Json -Compress))} finally {$writer.Dispose()}
} finally {$zip.Dispose()}
$result=Join-Path $runtime 'endfield-result.json'
if(Test-Path -LiteralPath $result){Move-Item -LiteralPath $result -Destination ($result+'.'+[guid]::NewGuid().ToString()+'.previous')}
$p=Start-Process -FilePath (Join-Path $runtime 'Survivalcraft.exe') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru
Write-Output "Endfield isolated game PID $($p.Id); result: $result"
