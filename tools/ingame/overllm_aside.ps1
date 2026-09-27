param([Parameter(Mandatory = $true)][ValidateSet('out', 'back')][string]$Mode)
# Moves the OverLlm English patch out of the game (for a "not installed" test run) and back, verifying every file's hash.
$g = 'C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal'
$aside = Join-Path $g 'BepInEx\_lom_overllm_aside_test'
$hashFile = Join-Path $PSScriptRoot 'overllm_aside_hashes.txt'
$items = @('Mods\English', 'BepInEx\Translation', 'BepInEx\plugins\FunctionalPlugin_Binarizer.dll', 'BepInEx\plugins\XUnity.AutoTranslator', 'BepInEx\plugins\XUnity.ResourceRedirector', 'BepInEx\plugins\LOM_UI_Plugin_KR.dll')
if (Get-Process Mortal -ErrorAction SilentlyContinue) { 'GAME RUNNING - refused'; exit 1 }
function HashAll([string]$root) {
    $lines = @()
    foreach ($i in $items) {
        $p = Join-Path $root $i
        if (Test-Path $p -PathType Leaf) { $lines += ('{0}  {1}' -f (Get-FileHash $p).Hash, $i) }
        elseif (Test-Path $p) { Get-ChildItem $p -Recurse -File | Sort-Object FullName | ForEach-Object { $lines += ('{0}  {1}' -f (Get-FileHash $_.FullName).Hash, $_.FullName.Substring($root.Length + 1)) } }
    }
    return $lines
}
if ($Mode -eq 'out') {
    if (Test-Path $aside) { 'aside folder exists already - refused'; exit 1 }
    $h = HashAll $g
    [System.IO.File]::WriteAllLines($hashFile, $h)
    New-Item -ItemType Directory -Force $aside | Out-Null
    foreach ($i in $items) {
        $src = Join-Path $g $i
        if (-not (Test-Path $src)) { "missing: $i"; continue }
        $dst = Join-Path $aside $i
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        Move-Item $src $dst
    }
    $h2 = HashAll $aside
    if ((Compare-Object $h $h2) -eq $null) { "moved aside: $($h.Count) files, hashes match" } else { 'HASH MISMATCH after move'; exit 2 }
} else {
    $h = [System.IO.File]::ReadAllLines($hashFile)
    foreach ($i in $items) {
        $src = Join-Path $aside $i
        if (-not (Test-Path $src)) { "missing in aside: $i"; continue }
        $dst = Join-Path $g $i
        if (Test-Path $dst) { "target exists, refused: $i"; exit 3 }
        Move-Item $src $dst
    }
    $h2 = HashAll $g
    if ((Compare-Object $h $h2) -eq $null) {
        "moved back: $($h.Count) files, hashes match the originals"
        $left = Get-ChildItem $aside -Recurse -File
        if (-not $left) { Remove-Item $aside -Recurse -Force -Confirm:$false; 'aside folder removed (empty)' } else { "aside folder still holds $($left.Count) files" }
    } else { 'HASH MISMATCH after moving back'; exit 2 }
}
