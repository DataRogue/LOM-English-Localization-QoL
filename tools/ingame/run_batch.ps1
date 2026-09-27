param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Commands, [int]$TimeoutSec = 120)
# Writes a harness batch (commands separated by ';;' or newlines) to cmd.txt, waits for out/<Name>.done, prints the log lines it produced.
# 0.5+: the harness lives in BepInEx\cache\LOM_UI_EN\harness (0.4 used the plugin folder)
$h = "C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal\BepInEx\cache\LOM_UI_EN\harness"
if (-not (Test-Path $h)) { $h = "C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal\BepInEx\plugins\LOM_UI_EN\harness" }
$log = Join-Path $h "out\log.txt"
$done = Join-Path $h "out\$Name.done"
if (Test-Path $done) { [System.IO.File]::Delete($done) }
$before = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
$lines = @("batch $Name") + ($Commands -split ";;|`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
[System.IO.File]::WriteAllText((Join-Path $h "cmd.txt"), ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
$t = 0
while (-not (Test-Path $done) -and $t -lt $TimeoutSec) {
	if (-not (Get-Process Mortal -ErrorAction SilentlyContinue)) { Write-Output "GAME NOT RUNNING"; break }
	Start-Sleep -Milliseconds 500; $t += 0.5
}
if (-not (Test-Path $done)) { Write-Output "TIMEOUT after $TimeoutSec s" }
$all = Get-Content $log
$all[$before..($all.Count - 1)] | ForEach-Object { if ($_.Length -gt 400) { $_.Substring(0, 400) + " ..." } else { $_ } }
