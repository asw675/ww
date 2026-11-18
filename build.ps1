param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'),
    [switch]$Test,
    [string]$ReferenceDirectory,
    [switch]$RebuildSignatures
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The 64-bit .NET Framework C# compiler was not found.'
}

$buildFolder = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object FullName)
$runtime = Join-Path $PSScriptRoot 'lib'
$common = @('/nologo', '/warn:4', '/warnaserror+', '/utf8output', '/codepage:65001', '/optimize+', '/platform:x64',
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll', '/r:System.Web.Extensions.dll',
    ('/r:' + (Join-Path $runtime 'Microsoft.ML.OnnxRuntime.dll')), ('/r:' + (Join-Path $runtime 'netstandard.dll')),
    ('/r:' + (Join-Path $runtime 'System.Memory.dll')), ('/r:' + (Join-Path $runtime 'System.Buffers.dll')),
    ('/r:' + (Join-Path $runtime 'System.Runtime.CompilerServices.Unsafe.dll')))
$signatures = Join-Path $PSScriptRoot 'data\Signatures.tsv'
if ($RebuildSignatures) {
    if (-not $ReferenceDirectory) { throw 'Specify a local reference directory to regenerate signatures.' }
    $generator = Join-Path $buildFolder 'BuildSignatures.exe'
    $generatorSource = Join-Path $PSScriptRoot 'tools\BuildSignatures.cs'
    & $compiler @common '/target:exe' '/main:TideCombat.BuildSignatures' "/out:$generator" @sources $generatorSource
    if ($LASTEXITCODE -ne 0) { throw "Signature generator compilation failed ($LASTEXITCODE)." }
    & $generator $ReferenceDirectory $signatures
    if ($LASTEXITCODE -ne 0) { throw "Signature generation failed ($LASTEXITCODE)." }
}
$resource = "/resource:$signatures,TideCombat.Signatures.tsv"

$application = Join-Path $buildFolder 'TideCombat.exe'
& $compiler @common '/target:winexe' "/out:$application" $resource @sources
if ($LASTEXITCODE -ne 0) { throw "Application compilation failed ($LASTEXITCODE)." }
Copy-Item (Join-Path $runtime 'onnxruntime.dll') $buildFolder -Force
Copy-Item (Join-Path $runtime 'System.Memory.dll') $buildFolder -Force
Copy-Item (Join-Path $runtime 'System.Buffers.dll') $buildFolder -Force
Copy-Item (Join-Path $runtime 'System.Runtime.CompilerServices.Unsafe.dll') $buildFolder -Force
foreach ($asset in @('echo_model', 'ocr', 'minimap'))
{
    $source = Join-Path $PSScriptRoot ('assets\' + $asset)
    $target = Join-Path $buildFolder ('assets\' + $asset)
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item (Join-Path $source '*') $target -Force -Recurse
}
Write-Output $application

if ($Test -or $ReferenceDirectory) {
    $testRunner = Join-Path $buildFolder 'TideCombat.Tests.exe'
    $testFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' -File |
        Sort-Object Name | ForEach-Object FullName)
    & $compiler @common '/target:exe' '/main:TideCombat.SelfTest' "/out:$testRunner" $resource @sources @testFiles
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed ($LASTEXITCODE)." }
    if ($ReferenceDirectory) {
        & $testRunner '--reference' $ReferenceDirectory
    } else {
        & $testRunner
    }
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE)." }
}
