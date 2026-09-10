$ErrorActionPreference = 'Stop'
$auditRoot = $PSScriptRoot
$sourceRoot = 'D:\workespace\ws-game-audit-d6fda65-20260911'
$probeRoot = Join-Path $auditRoot 'abi-property'
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-ProbeFile($relative, $content) {
    $target = Join-Path $probeRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    [IO.File]::WriteAllText($target, $content, $utf8)
}
function Invoke-ProbeBuild($project, $log) {
    & dotnet build $project -c Release --nologo *> (Join-Path $probeRoot $log)
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $log" }
}
$libraryProject = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>CompatLib</AssemblyName></PropertyGroup></Project>'
Write-ProbeFile 'old/Compat.csproj' $libraryProject
Write-ProbeFile 'new/Compat.csproj' $libraryProject
Write-ProbeFile 'old/Compat.cs' 'public class Compat { public int Value { get { return 7; } } }'
Write-ProbeFile 'new/Compat.cs' 'public class Compat { public static int Value { get { return 7; } } }'
Write-ProbeFile 'consumer/Consumer.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><Reference Include="CompatLib"><HintPath>../old/bin/Release/net8.0/CompatLib.dll</HintPath></Reference></ItemGroup></Project>'
Write-ProbeFile 'consumer/Program.cs' 'using System; using System.Runtime.CompilerServices; class Program { [MethodImpl(MethodImplOptions.NoInlining)] static int Read() { return new Compat().Value; } static int Main() { try { Console.WriteLine("VALUE=" + Read()); return 0; } catch(Exception ex) { Console.WriteLine("FAIL=" + ex.GetType().Name + ": " + ex.Message); return 9; } } }'
Invoke-ProbeBuild (Join-Path $probeRoot 'old/Compat.csproj') 'old-build.log'
Invoke-ProbeBuild (Join-Path $probeRoot 'new/Compat.csproj') 'new-build.log'
Invoke-ProbeBuild (Join-Path $probeRoot 'consumer/Consumer.csproj') 'consumer-build.log'
$consumerDir = Join-Path $probeRoot 'consumer/bin/Release/net8.0'
& dotnet (Join-Path $consumerDir 'Consumer.dll') *> (Join-Path $probeRoot 'consumer-old.log')
$oldExit = $LASTEXITCODE
Copy-Item -LiteralPath (Join-Path $probeRoot 'new/bin/Release/net8.0/CompatLib.dll') -Destination (Join-Path $consumerDir 'CompatLib.dll') -Force
& dotnet (Join-Path $consumerDir 'Consumer.dll') *> (Join-Path $probeRoot 'consumer-new.log')
$newExit = $LASTEXITCODE
$surfaceDir = Join-Path $probeRoot 'surface-tool'
New-Item -ItemType Directory -Force -Path $surfaceDir | Out-Null
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'toolchain/abi_surface') -File | Where-Object { $_.Extension -in @('.cs', '.csproj') } | Copy-Item -Destination $surfaceDir -Force
Invoke-ProbeBuild (Join-Path $surfaceDir 'AbiSurface.csproj') 'surface-build.log'
$surfaceDll = Join-Path $surfaceDir 'bin/Release/net8.0/AbiSurface.dll'
& dotnet $surfaceDll dump --out (Join-Path $probeRoot 'old-surface.txt') (Join-Path $probeRoot 'old/bin/Release/net8.0/CompatLib.dll') *> (Join-Path $probeRoot 'old-dump.log')
if ($LASTEXITCODE -ne 0) { throw 'Old dump failed' }
& dotnet $surfaceDll dump --out (Join-Path $probeRoot 'new-surface.txt') (Join-Path $probeRoot 'new/bin/Release/net8.0/CompatLib.dll') *> (Join-Path $probeRoot 'new-dump.log')
if ($LASTEXITCODE -ne 0) { throw 'New dump failed' }
& dotnet $surfaceDll compare (Join-Path $probeRoot 'old-surface.txt') (Join-Path $probeRoot 'new-surface.txt') --out (Join-Path $probeRoot 'surface-report.txt') *> (Join-Path $probeRoot 'compare.log')
$compareExit = $LASTEXITCODE
$result = [ordered]@{ frozen_commit='d6fda65cd6b00ede5c5f6f606f724d2ab0f60603'; expected_old_exit=0; actual_old_exit=$oldExit; expected_new_consumer_failure=$true; actual_new_exit=$newExit; expected_surface_break_exit=2; actual_surface_exit=$compareExit; consumer_compiled_once=$true }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $probeRoot 'result.json') -Encoding UTF8
Get-Content (Join-Path $probeRoot 'consumer-old.log')
Get-Content (Join-Path $probeRoot 'consumer-new.log')
Get-Content (Join-Path $probeRoot 'surface-report.txt')
$result | ConvertTo-Json
