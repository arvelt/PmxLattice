param([Parameter(Mandatory=$true)][string]$EditorPath, [string[]]$Models = @())
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sdk = Join-Path $EditorPath 'Lib\PEPlugin\PEPlugin.dll'
$slim = Join-Path $EditorPath 'Lib\SlimDX\x64\SlimDX.dll'
$previousEditor = $env:PMXLATTICE_EDITOR_PATH
$env:PMXLATTICE_EDITOR_PATH = (Resolve-Path -LiteralPath $EditorPath).Path
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path 'dist' -Force | Out-Null
    & $compiler /nologo /target:exe /optimize+ /out:dist\TestRunner.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$sdk" "/reference:$slim" src\PmxDocument.cs src\MaterialRenderer.cs src\LatticeForm.cs src\HostState.cs src\Plugin.cs tests\TestRunner.cs
    if ($LASTEXITCODE -ne 0) { throw '検査用ビルドに失敗しました。' }
    & .\dist\TestRunner.exe @Models
    if ($LASTEXITCODE -ne 0) { throw '検査に失敗しました。' }
} finally { Pop-Location; $env:PMXLATTICE_EDITOR_PATH = $previousEditor }
