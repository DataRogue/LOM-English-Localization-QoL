# Build LOM_UI_EN.dll from source with the Roslyn compiler that ships with Visual Studio (2022 or later, any edition, or the
# Build Tools), against the .NET Framework 4.7.1+ reference assemblies, the game's DLLs and BepInEx 5.4.23.3's own.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Game <game folder>] [-OutDir <folder>] [-Csc <csc.exe>]
#                                                     [-BepInExCore <folder>]
#
# -Game defaults to $env:LOM_GAME, then the default Steam folder; only its Mortal_Data\Managed is referenced.
# BepInEx: since 1.2.0 the mod runs on BepInEx 5.4.23.3 only, the version Lash's English Patch installs (its installer pins the
# same official zip). The core files are taken from release/vendor/BepInEx_win_x64_5.4.23.3.zip, checked against its SHA-256,
# into a cache folder under %TEMP%; -BepInExCore (or $env:LOM_BEPINEX_CORE) names another folder holding BepInEx.dll and
# 0Harmony.dll 5.4.23.3 instead. Whatever BepInEx the game folder has installed does not matter. The game is 64-bit since its
# October 2026 update (build 25987745), so the pin is the x64 zip, as in that installer; the x86 zip's core files are the same
# bytes (only winhttp.dll differs), so either gives the same DLL. Newtonsoft.Json is the copy the plugin ships
# (release/vendor/unity-newtonsoft/Runtime-AOT, else the installed plugin folder's). The 1.0.x and 1.1.x branches keep the
# BepInEx 6 (be.692) build.
# The build is deterministic (-deterministic, source paths mapped): the same source, compiler and references give the same
# bytes, so anyone can rebuild a release DLL and compare it with the shipped one (tools/release.py does).
param([string]$Game = "", [string]$OutDir = "", [string]$Csc = "", [string]$BepInExCore = "")
$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$loc = Split-Path -Parent (Split-Path -Parent $src)
if ($Game -eq "") { $Game = $env:LOM_GAME }
if (-not $Game) { $Game = "C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal" }
if ($OutDir -eq "") { $OutDir = "$src\bin" }

$BieVersion = "5.4.23.3"
$BieZip = "$loc\release\vendor\BepInEx_win_x64_$BieVersion.zip"
$BieSha = "41a089e5b1b1f0713b331346baf6677b1184c69eabebf51101097954e854c749"
$BieUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BieVersion/BepInEx_win_x64_$BieVersion.zip"

function Find-Csc {
    if ($env:LOM_CSC) { return $env:LOM_CSC }
    # The compiler the released builds were made with first (Visual Studio 2022, 17.x), then the newest one installed.
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        foreach ($range in @("[17.0,18.0)", "")) {
            $vsArgs = @("-products", "*", "-prerelease", "-find", "MSBuild\**\Bin\Roslyn\csc.exe")
            if ($range) { $vsArgs = @("-version", $range) + $vsArgs } else { $vsArgs = @("-latest") + $vsArgs }
            $found = & $vswhere @vsArgs | Select-Object -First 1
            if ($found -and (Test-Path $found)) { return $found }
        }
    }
    $known = Get-ChildItem "C:\Program Files\Microsoft Visual Studio", "C:\Program Files (x86)\Microsoft Visual Studio" -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\Roslyn\\csc\.exe$' } | Sort-Object { $_.VersionInfo.FileVersionRaw } -Descending | Select-Object -First 1
    if ($known) { return $known.FullName }
    throw "csc.exe not found: install Visual Studio 2022+ or its Build Tools (the .NET desktop workload), or pass -Csc"
}

# BepInEx 5.4.23.3's core folder: -BepInExCore, $env:LOM_BEPINEX_CORE, else extracted from the pinned zip (once per zip hash).
function Get-BepInExCore {
    if ($BepInExCore) { return $BepInExCore }
    if ($env:LOM_BEPINEX_CORE) { return $env:LOM_BEPINEX_CORE }
    if (-not (Test-Path $BieZip)) { throw "missing $BieZip (download it from $BieUrl)" }
    $sha = (Get-FileHash -Algorithm SHA256 $BieZip).Hash.ToLower()
    if ($sha -ne $BieSha) { throw "$BieZip is not the pinned official file (SHA-256 $sha, expected $BieSha)" }
    $dir = Join-Path $env:TEMP ("lom_bepinex_$BieVersion" + "_" + $BieSha.Substring(0, 12))
    if (-not (Test-Path "$dir\BepInEx\core\BepInEx.dll")) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($BieZip, $dir)
    }
    return "$dir\BepInEx\core"
}
if ($Csc -eq "") { $Csc = Find-Csc }
$core = Get-BepInExCore

$fwRoot = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework"
$fw = @("v4.7.1", "v4.7.2", "v4.8", "v4.8.1") | ForEach-Object { "$fwRoot\$_" } | Where-Object { Test-Path "$_\mscorlib.dll" } | Select-Object -First 1
if (-not $fw) { throw "no .NET Framework 4.7.1+ reference assemblies under $fwRoot (install the 4.7.1 or 4.8 targeting pack)" }
$m = "$Game\Mortal_Data\Managed"
# Newtonsoft.Json: Unity's AOT build, the pinned file the plugin ships (docs/DEVELOPMENT.md, third-party files): extracted
# into release/vendor, else the copy in the installed plugin folder (every release ships the same file).
$newtonsoft = "$loc\release\vendor\unity-newtonsoft\Runtime-AOT\Newtonsoft.Json.dll"
if (-not (Test-Path $newtonsoft)) { $newtonsoft = "$Game\BepInEx\plugins\LOM_UI_EN\Newtonsoft.Json.dll" }
if (-not (Test-Path $newtonsoft)) { throw "Newtonsoft.Json.dll not found: extract Runtime/AOT/Newtonsoft.Json.dll of com.unity.nuget.newtonsoft-json 3.2.2 into release\vendor\unity-newtonsoft\Runtime-AOT (docs/DEVELOPMENT.md), or install a release's plugin folder" }
foreach ($p in @("$m\Mortal.Core.dll", "$core\BepInEx.dll", "$core\0Harmony.dll")) { if (-not (Test-Path $p)) { throw "not found: $p (is -Game the game folder?)" } }
$v = [System.Reflection.AssemblyName]::GetAssemblyName("$core\BepInEx.dll").Version
if ("$v" -ne "$BieVersion") { throw "$core\BepInEx.dll is BepInEx $v, not $BieVersion" }

$refs = @("$fw\mscorlib.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\Facades\netstandard.dll",
  "$m\UnityEngine.dll", "$m\UnityEngine.CoreModule.dll", "$m\UnityEngine.UI.dll", "$m\UnityEngine.UIModule.dll", "$m\UnityEngine.TextRenderingModule.dll",
  "$m\UnityEngine.InputLegacyModule.dll", "$m\UnityEngine.AudioModule.dll","$m\UnityEngine.ImageConversionModule.dll", "$m\UnityEngine.ScreenCaptureModule.dll",
  "$m\Unity.TextMeshPro.dll", "$m\LeanLocalization.dll", "$m\LeanLocalization.TMP.dll", "$m\Fungus.dll", "$m\Mortal.Core.dll", "$m\Mortal.Combat.dll", "$m\Mortal.Story.dll", "$m\Mortal.Free.dll", "$m\Mortal.Battle.dll", "$m\Unity.InputSystem.dll", "$m\DOTween.dll", "$m\OBB.Framework.dll", "$m\Unity.Addressables.dll", "$m\Unity.ResourceManager.dll",
  "$core\BepInEx.dll", "$core\0Harmony.dll", $newtonsoft) | ForEach-Object { "-r:$_" }
$files = Get-ChildItem "$src\*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
& $Csc -nologo -noconfig -nostdlib+ -langversion:latest -target:library -optimize+ -debug:portable -warn:1 -deterministic `
  "-pathmap:$src=/LOM_UI_EN/,$OutDir=/LOM_UI_EN/bin/" "-out:$OutDir\LOM_UI_EN.dll" $refs $files
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
Write-Host "built $OutDir\LOM_UI_EN.dll for BepInEx $BieVersion with $Csc"
