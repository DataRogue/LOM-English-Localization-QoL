# Build LOM_UI_EN.dll from source with the Roslyn compiler that ships with Visual Studio (2022 or later, any edition, or the
# Build Tools), against the .NET Framework 4.7.1+ reference assemblies and the game's and BepInEx's own DLLs.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Game <game folder>] [-OutDir <folder>] [-Csc <csc.exe>]
#                                                     [-BepInEx5 [-BepInEx5Dir <folder with BepInEx.dll 5.4>]]
#
# -Game defaults to $env:LOM_GAME, then the default Steam folder. The game folder must hold BepInEx 6 (be.692) in BepInEx\core.
# -BepInEx5 builds LOM_UI_EN.BepInEx5.dll instead, the same source for BepInEx 5 (defines BIE5, see Loader.cs), against the
# BepInEx.dll 5.4 in -BepInEx5Dir (default: $env:LOM_BEPINEX5, then the game's BepInEx\core, where Lash's English Patch keeps
# a 5.4.21 one). The DLL records the version it was built against, so a reproducible build needs the same one (5.4.21 for
# the released builds). Each BepInEx loads only the DLL built for it, so both can sit in the plugin folder.
# The build is deterministic (-deterministic, source paths mapped): the same source, compiler and references give the same
# bytes, so anyone can rebuild a release DLL and compare it with the shipped one (tools/release.py does).
# -CoreDir: the BepInEx 6 core folder to reference (default <Game>\BepInEx\core; for building while that folder holds another
# BepInEx, e.g. during tools/ingame/release_test.py l5_setup). Reference paths do not enter the output.
param([string]$Game = "", [string]$OutDir = "", [string]$Csc = "", [switch]$BepInEx5, [string]$BepInEx5Dir = "", [string]$CoreDir = "")
$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Game -eq "") { $Game = $env:LOM_GAME }
if (-not $Game) { $Game = "C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal" }
if ($OutDir -eq "") { $OutDir = "$src\bin" }

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
if ($Csc -eq "") { $Csc = Find-Csc }

$fwRoot = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework"
$fw = @("v4.7.1", "v4.7.2", "v4.8", "v4.8.1") | ForEach-Object { "$fwRoot\$_" } | Where-Object { Test-Path "$_\mscorlib.dll" } | Select-Object -First 1
if (-not $fw) { throw "no .NET Framework 4.7.1+ reference assemblies under $fwRoot (install the 4.7.1 or 4.8 targeting pack)" }
$m = "$Game\Mortal_Data\Managed"; $core = if ($CoreDir) { $CoreDir } else { "$Game\BepInEx\core" }
if ($BepInEx5) {
    # -BepInEx5Dir, then $env:LOM_BEPINEX5 (for tools/release.py), then the game's core folder
    if ($BepInEx5Dir -eq "") { $BepInEx5Dir = $env:LOM_BEPINEX5 }
    if (-not $BepInEx5Dir) { $BepInEx5Dir = $core }
    $bie = @("$BepInEx5Dir\BepInEx.dll")
    $name = "LOM_UI_EN.BepInEx5"; $define = @("-define:BIE5")
} else {
    $bie = @("$core\BepInEx.Core.dll", "$core\BepInEx.Unity.Mono.dll")
    $name = "LOM_UI_EN"; $define = @()
}
foreach ($p in @("$m\Mortal.Core.dll") + $bie) { if (-not (Test-Path $p)) { throw "not found: $p (is -Game the game folder, with BepInEx installed?)" } }
if ($BepInEx5) {
    $v = [System.Reflection.AssemblyName]::GetAssemblyName($bie[0]).Version
    if ($v.Major -ne 5) { throw "$($bie[0]) is BepInEx $v, not 5.x" }
}

$refs = @("$fw\mscorlib.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\Facades\netstandard.dll",
  "$m\UnityEngine.dll", "$m\UnityEngine.CoreModule.dll", "$m\UnityEngine.UI.dll", "$m\UnityEngine.UIModule.dll", "$m\UnityEngine.TextRenderingModule.dll",
  "$m\UnityEngine.InputLegacyModule.dll", "$m\UnityEngine.AudioModule.dll","$m\UnityEngine.ImageConversionModule.dll", "$m\UnityEngine.ScreenCaptureModule.dll",
  "$m\Unity.TextMeshPro.dll", "$m\LeanLocalization.dll", "$m\LeanLocalization.TMP.dll", "$m\Fungus.dll", "$m\Mortal.Core.dll", "$m\Mortal.Combat.dll", "$m\Mortal.Story.dll", "$m\Mortal.Free.dll", "$m\Mortal.Battle.dll", "$m\Unity.InputSystem.dll", "$m\DOTween.dll", "$m\OBB.Framework.dll", "$m\Unity.Addressables.dll", "$m\Unity.ResourceManager.dll",
  $bie, "$core\0Harmony.dll", "$core\Newtonsoft.Json.dll") | ForEach-Object { $_ } | ForEach-Object { "-r:$_" }
$files = Get-ChildItem "$src\*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
& $Csc -nologo -noconfig -nostdlib+ -langversion:latest -target:library -optimize+ -debug:portable -warn:1 -deterministic `
  "-pathmap:$src=/LOM_UI_EN/,$OutDir=/LOM_UI_EN/bin/" "-out:$OutDir\$name.dll" $define $refs $files
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
Write-Host "built $OutDir\$name.dll with $Csc"
