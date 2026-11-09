<#
.SYNOPSIS
  Builds artifacts\Featherweight.exe with the Roslyn C# compiler that ships with Visual Studio / Build Tools.
  Targets .NET Framework 4.8 (built into Windows 10 22H2 and 11), so the exe runs without installing anything.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build.ps1
#>
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$src = Join-Path $repo 'src\Featherweight'
$obj = Join-Path $repo 'obj'
$out = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force $obj, $out | Out-Null

# Find the C# compiler: vswhere (ships with every Visual Studio install, including GitHub's build servers) is instant;
# a folder search is the fallback.
$csc = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
  $csc = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (-not $csc) {
  $csc = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio', 'C:\Program Files (x86)\Microsoft Visual Studio' -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
    Where-Object FullName -match '\\MSBuild\\Current\\Bin\\Roslyn\\csc\.exe$' | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $csc) { throw 'Roslyn csc.exe not found. Install Visual Studio 2022 or "Build Tools for Visual Studio".' }

# Version comes from version.txt (e.g. 0.1.0); CI can override with $env:FW_VERSION (e.g. from a git tag).
$version = if ($env:FW_VERSION) { $env:FW_VERSION.TrimStart('v') } else { (Get-Content (Join-Path $repo 'version.txt') -Raw).Trim() }
$numeric = ($version -split '[-+]')[0]
$commit = try { (git -C $repo rev-parse --short HEAD 2>$null) } catch { $null }
$info = if ($commit) { "$version+$commit" } else { $version }
@"
using System.Reflection;
[assembly: AssemblyTitle("Featherweight")]
[assembly: AssemblyProduct("Featherweight")]
[assembly: AssemblyDescription("Memory, startup and security tool for Windows")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Featherweight contributors. MIT License.")]
[assembly: AssemblyVersion("$numeric.0")]
[assembly: AssemblyFileVersion("$numeric.0")]
[assembly: AssemblyInformationalVersion("$info")]
"@ | Set-Content (Join-Path $obj 'AssemblyInfo.g.cs') -Encoding UTF8

$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Management.dll',
        'System.Web.Extensions.dll', 'System.ServiceProcess.dll', 'Microsoft.CSharp.dll' | ForEach-Object { "/r:$fw\$_" }
$sources = @((Get-ChildItem $src -Filter *.cs).FullName) + (Join-Path $obj 'AssemblyInfo.g.cs')
$opt = if ($Configuration -eq 'Debug') { '/debug+', '/optimize-', '/define:DEBUG' } else { '/optimize+', '/debug:pdbonly' }

& $csc /nologo /target:winexe /platform:x64 /langversion:latest /nowarn:1701,1702 /warnaserror- @opt `
  "/out:$out\Featherweight.exe" "/win32manifest:$src\app.manifest" "/win32icon:$src\app.ico" `
  "/resource:$src\catalog.json,Featherweight.catalog.json" "/resource:$src\knowledge.json,Featherweight.knowledge.json" @refs @sources
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
"Built $out\Featherweight.exe  (version $info)"
