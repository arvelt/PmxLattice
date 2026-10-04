param([Parameter(Mandatory=$true)][string]$EditorPath)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sdk = Join-Path $EditorPath 'Lib\PEPlugin\PEPlugin.dll'
$slim = Join-Path $EditorPath 'Lib\SlimDX\x64\SlimDX.dll'
foreach ($path in @($compiler,$sdk,$slim)) { if (-not (Test-Path -LiteralPath $path)) { throw "必要なファイルがありません: $path" } }
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path 'dist' -Force | Out-Null
    & $compiler /nologo /target:library /optimize+ /out:dist\PmxLattice.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$sdk" "/reference:$slim" src\AssemblyInfo.cs src\PmxDocument.cs src\MaterialRenderer.cs src\LatticeForm.cs src\HostState.cs src\Plugin.cs
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
} finally { Pop-Location }
