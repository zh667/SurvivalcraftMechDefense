$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'local/endfield/industry-library'
$cli = Join-Path $root 'local/tools/AnimeStudioVFS/AnimeStudio.CLI/bin/Release/net8.0-windows/AnimeStudio.CLI.exe'
$config = Get-Content (Join-Path $root 'local/config.json') -Raw | ConvertFrom-Json
$streaming = Join-Path $config.endfield 'Endfield_Data/StreamingAssets'
python (Join-Path $PSScriptRoot 'archive_endfield_industry.py') --extract
if ($LASTEXITCODE -ne 0) { throw 'Archive failed' }
& $cli dump --streaming-assets $streaming --output (Join-Path $out 'config') --block-type table --file-regex '^Data/TableCfg/(Factory(Const|.*Table)|ItemTable|I18nTextTable_CN|AudioFactory)\.bytes$' --verify-md5
if ($LASTEXITCODE -ne 0) { throw 'Configuration extraction failed' }
& $cli (Join-Path $out 'raw-bundles') (Join-Path $out 'converted') --game ArknightsEndfield --export_type Convert --secondary_export_type JSON --secondary_export_path (Join-Path $out 'object-json') --texture2d_native_payload --export_manifest_jsonl (Join-Path $out 'export-manifest.jsonl') --logger_flags Warning,Error
if ($LASTEXITCODE -ne 0) { throw 'Asset conversion failed' }
& $cli (Join-Path $out 'raw-bundles') (Join-Path $out 'scene-json') --game ArknightsEndfield --types GameObject Transform ParticleSystem ParticleSystemRenderer MeshRenderer MeshFilter MonoBehaviour --export_type JSON --renderer_index_jsonl (Join-Path $out 'renderers.jsonl') --export_manifest_jsonl (Join-Path $out 'scene-manifest.jsonl') --logger_flags Warning,Error
if ($LASTEXITCODE -ne 0) { throw 'Scene metadata export failed' }
python (Join-Path $PSScriptRoot 'verify_industry_research.py')
if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }

