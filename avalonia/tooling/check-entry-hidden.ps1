# CI only (Windows). A controller may start xshell.exe hidden. When the entry cannot go on (here: Forterm.exe is not next to
# it), the box that says why must still be seen. Starts the entry hidden from a copy of the published folder without
# Forterm.exe, waits for a visible window of that process, closes it, and checks the exit code (2). Nothing connects.
param([Parameter(Mandatory = $true)][string]$Payload)
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class EntryWindows
{
    delegate bool EnumProc(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr window, StringBuilder text, int most);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr window, uint message, IntPtr w, IntPtr l);
    public static IntPtr Visible(int processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            var text = new StringBuilder(256);
            GetWindowTextW(window, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
"@

$copy = Join-Path ([IO.Path]::GetTempPath()) ("forterm-entry-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item $Payload $copy -Recurse
Remove-Item (Join-Path $copy 'Forterm.exe')
try {
    $entry = Start-Process (Join-Path $copy 'xshell.exe') -ArgumentList '-newtab', 'SYNTHETIC', '-url', 'ssh://synthetic:synthetic-only@127.0.0.1:1' -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(20)
    $box = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline -and -not $entry.HasExited) {
        $box = [EntryWindows]::Visible($entry.Id, 'Forterm')
        if ($box -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($box -eq [IntPtr]::Zero) {
        if (-not $entry.HasExited) { $entry.Kill() }
        throw "started hidden, the entry showed no visible box saying why it stopped"
    }
    Write-Host "ok   the box is visible although the entry was started hidden"
    [void][EntryWindows]::PostMessageW($box, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
    if (-not $entry.WaitForExit(20000)) { $entry.Kill(); throw "the entry did not end after its box was closed" }
    if ($entry.ExitCode -ne 2) { throw "expected exit code 2 (Forterm.exe missing), got $($entry.ExitCode)" }
    Write-Host "ok   exit code 2 after the box was closed"
}
finally { Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue }
