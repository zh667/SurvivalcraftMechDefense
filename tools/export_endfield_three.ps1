$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'local/endfield/tower-library/three-gun-towers'
$cli=Join-Path $root 'local/tools/AnimeStudioVFS/AnimeStudio.CLI/bin/Release/net8.0-windows/AnimeStudio.CLI.exe'
python (Join-Path $PSScriptRoot 'prepare_endfield_three.py')
if($LASTEXITCODE -ne 0){throw 'Source selection failed'}
& $cli (Join-Path $out 'bundles') (Join-Path $out 'converted') --game ArknightsEndfield --export_type Convert --secondary_export_type JSON --secondary_export_path (Join-Path $out 'json') --texture2d_native_payload --export_manifest_jsonl (Join-Path $out 'export-manifest.jsonl') --logger_flags Warning,Error > (Join-Path $out 'export.log') 2>&1
if($LASTEXITCODE -ne 0){throw 'Asset export failed; see export.log'}
& $cli (Join-Path $out 'bundles') (Join-Path $out 'scene-json') --game ArknightsEndfield --types GameObject Transform ParticleSystem ParticleSystemRenderer MeshRenderer MeshFilter AnimatorController MonoBehaviour --export_type JSON --renderer_index_jsonl (Join-Path $out 'renderers.jsonl') --export_manifest_jsonl (Join-Path $out 'scene-manifest.jsonl') --logger_flags Warning,Error > (Join-Path $out 'scene-export.log') 2>&1
if($LASTEXITCODE -ne 0){throw 'Scene export failed; see scene-export.log'}
$config=Get-Content (Join-Path $root 'local/config.json') -Raw | ConvertFrom-Json
$builtin=Join-Path $config.endfield 'Endfield_Data/Resources'
& $cli $builtin (Join-Path $out 'builtin-json') --game ArknightsEndfield --types Mesh --export_type JSON --logger_flags Warning,Error > (Join-Path $out 'builtin-export.log') 2>&1
if($LASTEXITCODE -ne 0){throw 'Builtin mesh export failed'}
Write-Output 'Source exports ready; run convert_endfield_three.py and the audio conversion scripts.'
