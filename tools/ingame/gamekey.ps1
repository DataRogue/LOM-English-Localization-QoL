# Press keys in the game window. With -Focus the game window is brought to the foreground first (the user allowed a
# foreground test run); without it, keys are only sent when the game already is the foreground window.
# Usage: gamekey.ps1 -Vk 0x77 [-Focus] [-Times 1]   (0x77 = F8, 0x1B = Esc, 0x78 = F9, 0x74 = F5)
param([int]$Vk, [switch]$Focus, [int]$Times = 1)
Add-Type -Namespace W -Name U -MemberDefinition @"
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
"@
$game = Get-Process Mortal -ErrorAction SilentlyContinue
if (-not $game) { "NO GAME"; exit 2 }
function FgPid { $fg = [W.U]::GetForegroundWindow(); $p = 0; [void][W.U]::GetWindowThreadProcessId($fg, [ref]$p); return $p }
if ($Focus -and (FgPid) -ne $game.Id) {
	$h = $game.MainWindowHandle
	[void][W.U]::ShowWindow($h, 9)
	$shell = New-Object -ComObject WScript.Shell
	[void]$shell.AppActivate($game.Id)
	Start-Sleep -Milliseconds 300
	[void][W.U]::SetForegroundWindow($h)
	Start-Sleep -Milliseconds 300
}
$procId = FgPid
if ($procId -ne $game.Id) { "NOT FOREGROUND (foreground pid $procId)"; exit 3 }
$scan = [byte][W.U]::MapVirtualKey([uint32]$Vk, 0)
for ($i = 0; $i -lt $Times; $i++) {
	[W.U]::keybd_event([byte]$Vk, $scan, 0, [UIntPtr]::Zero)
	Start-Sleep -Milliseconds 80
	[W.U]::keybd_event([byte]$Vk, $scan, 2, [UIntPtr]::Zero)
	Start-Sleep -Milliseconds 150
}
"sent $Vk x$Times"
