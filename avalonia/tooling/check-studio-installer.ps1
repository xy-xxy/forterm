# Installs, upgrades and removes Studio on this machine and compares every file. For CI: it uses the real per-user
# places (and puts them back), so do not run it where Studio is in use.
# OldSetup is an earlier version: the same files and one more, old-only.txt.
param([Parameter(Mandatory)][string]$Setup, [Parameter(Mandatory)][string]$OldSetup, [Parameter(Mandatory)][string]$Payload, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path $Setup).Path; $OldSetup = (Resolve-Path $OldSetup).Path; $Payload = (Resolve-Path $Payload).Path
$default = "$env:LOCALAPPDATA\Programs\Forterm Studio"
$uninstaller = 'Uninstall-Forterm-Studio.exe'
$regUninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermStudio'
$regProduct = 'HKCU:\Software\Forterm\Studio'
$menu = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Forterm Studio.lnk"
$desktop = [Environment]::GetFolderPath('Desktop') + '\Forterm Studio.lnk'
function Run([string]$file, [string]$arguments)
{
    $process = Start-Process -FilePath $file -ArgumentList $arguments -PassThru
    $null = $process.Handle # otherwise the exit code is not kept
    if (-not $process.WaitForExit(180000)) { $process.Kill($true); throw "FAILED: $file $arguments did not end" }
    $process.ExitCode
}
function Check([string]$what, $ok) { if (-not $ok) { throw "FAILED: $what" } else { Write-Host "ok  $what" } }
$relative = Get-ChildItem $Payload -Recurse -File | ForEach-Object { $_.FullName.Substring($Payload.Length + 1) }
function Same([string]$dir) { foreach ($f in $relative) { if (-not (Test-Path -LiteralPath "$dir\$f") -or (Get-FileHash -LiteralPath "$dir\$f").Hash -ne (Get-FileHash -LiteralPath "$Payload\$f").Hash) { throw "FAILED: $f differs" } }; $true }
function Gone([string]$dir) { -not ($relative | Where-Object { Test-Path -LiteralPath "$dir\$_" }) }
foreach ($there in $regUninstall, $regProduct, $default, $menu, $desktop) { if (Test-Path -LiteralPath $there) { throw "Studio is in use here: $there" } }
$data = "$env:LOCALAPPDATA\Forterm\Studio"; New-Item -ItemType Directory -Force $data | Out-Null; Set-Content "$data\ci-marker.txt" 'mine'

Check 'silent install' ((Run $Setup '/S') -eq 0)
Check "$($relative.Count) files are the published ones" (Same $default)
Check 'start menu' (Test-Path $menu)
Check 'no desktop shortcut unless asked' (-not (Test-Path $desktop))
$r = Get-ItemProperty $regUninstall
Check 'version in the list of programs' ($r.DisplayVersion -eq $Version -and $r.InstallLocation -eq $default)
Check 'runtime check of the installed program' ((Run "$default\Forterm.exe" '--runtime-check') -eq 0)
Check 'xshell forwards 0' ((Run "$default\xshell.exe" '--diagnose -newtab t -url ssh://t:p@h:22') -eq 0)
Check 'xftp forwards 2' ((Run "$default\xftp.exe" '--diagnose -url ssh://t:p@h:22 -bogus') -eq 2)

Check 'uninstall at once' ((Run "$default\$uninstaller" "/S _?=$default") -eq 0)
Check 'nothing left' ((Gone $default) -and -not (Test-Path $menu) -and -not (Test-Path $regUninstall))
Remove-Item $default -Recurse -Force

# Upgrade in place: what only the version before had goes, what the user put there stays, the desktop shortcut too.
Check 'the version before' ((Run $OldSetup '/S') -eq 0)
Check 'with its own file' (Test-Path "$default\old-only.txt")
Set-Content "$default\mine.txt" 'mine'; New-Item -ItemType Directory -Force "$default\own" | Out-Null; Set-Content "$default\own\x.txt" 'mine'
function Shortcut([string]$at, [string]$to) { $l = (New-Object -ComObject WScript.Shell).CreateShortcut($at); $l.TargetPath = $to; $l.Save() }
Shortcut $desktop "$default\Forterm.exe"; Set-ItemProperty $regProduct -Name DesktopShortcut -Value 1 -Type DWord
Check 'install over it' ((Run $Setup '/S') -eq 0)
Check 'the files of this version' (Same $default)
Check 'what only the version before had is gone' (-not (Test-Path "$default\old-only.txt"))
Check 'own files stay' ((Test-Path "$default\mine.txt") -and (Test-Path "$default\own\x.txt"))
Check 'shortcuts stay' ((Test-Path $desktop) -and (Test-Path $menu))
Check 'the new version in the list' ((Get-ItemProperty $regUninstall).DisplayVersion -eq $Version)

# A running program is not touched: neither replaced nor removed.
foreach ($program in 'Forterm.exe', 'xshell.exe')
{
    $lock = [IO.File]::Open("$default\$program", 'Open', 'Read', 'Read')
    try
    {
        Check "$program running: the installer gives up with 4" ((Run $Setup '/S') -eq 4)
        Check "$program running: the uninstaller gives up with 4" ((Run "$default\$uninstaller" "/S _?=$default") -eq 4)
        Check "$program running: nothing has changed" ((Same $default) -and (Test-Path $regUninstall))
    }
    finally { $lock.Dispose() }
}
# A file that cannot be replaced is no success.
$lock = [IO.File]::Open("$default\Forterm.Core.dll", 'Open', 'Read', 'Read')
try { $code = Run $Setup '/S'; Check "a locked file is no success ($code)" ($code -ne 0) } finally { $lock.Dispose() }
Check 'the next run puts it right' ((Run $Setup '/S') -eq 0)
Check 'all files again' (Same $default)

# An entry without its program, where nobody is asked.
$alone = Join-Path ([IO.Path]::GetTempPath()) ('forterm-alone-' + [guid]::NewGuid().ToString('N')); Copy-Item $default $alone -Recurse; Remove-Item "$alone\Forterm.exe"
Check 'an entry without Forterm.exe ends with 2' ((Run "$alone\xshell.exe" '--diagnose -url ssh://t:p@h:22') -eq 2)
Remove-Item $alone -Recurse -Force

Check 'uninstall' ((Run "$default\$uninstaller" "/S _?=$default") -eq 0)
Check 'every file is gone' (Gone $default)
Check 'own files are still there' ((Test-Path "$default\mine.txt") -and (Test-Path "$default\own\x.txt"))
Check 'shortcuts are gone' (-not (Test-Path $menu) -and -not (Test-Path $desktop))
Check 'registry is clean' (-not (Test-Path $regUninstall) -and -not (Test-Path $regProduct))
Check 'data stays' (Test-Path "$data\ci-marker.txt")
Remove-Item $default -Recurse -Force

# Another folder, with a blank and Chinese in its name; the next version goes there by itself.
$other = Join-Path ([IO.Path]::GetTempPath()) '其他 位置\Forterm Studio'
Check 'install elsewhere' ((Run $Setup "/S /D=$other") -eq 0)
Check 'files elsewhere' (Same $other)
Check 'xshell from there' ((Run "$other\xshell.exe" '--diagnose -url ssh://t:p@h:22') -eq 0)
Check 'the next one finds it' ((Run $Setup '/S') -eq 0)
Check 'still there, not in the usual place' ((Same $other) -and -not (Test-Path $default))
# A copy that is not the installed one: removing it leaves the installed one alone.
$copy = Join-Path ([IO.Path]::GetTempPath()) ('forterm-copy-' + [guid]::NewGuid().ToString('N')); Copy-Item $other $copy -Recurse
Check 'removing a copy' ((Run "$copy\$uninstaller" "/S _?=$copy") -eq 0)
Check 'the copy is gone' (Gone $copy)
Check 'the installed one is not' ((Same $other) -and (Test-Path $regUninstall) -and (Test-Path $menu) -and (Get-ItemProperty $regProduct).InstallDir -eq $other)
Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
# Files of somebody else's are neither replaced nor run.
foreach ($name in 'README.txt', 'xshell.exe', $uninstaller)
{
    $taken = Join-Path ([IO.Path]::GetTempPath()) ('forterm-taken-' + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $taken | Out-Null; Set-Content "$taken\$name" 'not ours'
    Check "a folder with somebody's $name is refused with 3" ((Run $Setup "/S /D=$taken") -eq 3)
    Check "and left as it was" ((Get-ChildItem $taken).Count -eq 1 -and (Get-Content "$taken\$name") -eq 'not ours' -and (Get-ItemProperty $regProduct).InstallDir -eq $other)
    Remove-Item $taken -Recurse -Force
}
# A shortcut that is not ours stays.
Shortcut $desktop "$env:WINDIR\notepad.exe"
Check 'uninstall elsewhere' ((Run "$other\$uninstaller" "/S _?=$other") -eq 0)
Check 'gone elsewhere' ((Gone $other) -and -not (Test-Path $regUninstall) -and -not (Test-Path $menu))
Check "somebody's desktop shortcut stays" (Test-Path $desktop)
Remove-Item $desktop
Remove-Item "$data\ci-marker.txt"
Write-Host 'installer: all checks passed'
