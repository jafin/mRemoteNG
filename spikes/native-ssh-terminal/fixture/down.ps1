<#
.SYNOPSIS
    Removes the SSH host the native-terminal spike measures against, and its keys.

.DESCRIPTION
    Pass the same -KeyDir that up.ps1 was given. The default matches up.ps1's, so the usual case
    needs neither.
#>
param(
    [string]$Name = "mrng-spike-sshd",
    [string]$KeyDir = "$PSScriptRoot\.keys"
)

. (Join-Path $PSScriptRoot "keys.ps1")

docker rm -f $Name 2>$null | Out-Null
docker rm -f "$Name-keygen" 2>$null | Out-Null

# up.ps1 writes the private key with ACL inheritance stripped, so a plain Remove-Item fails with
# access denied. This used to be -ErrorAction SilentlyContinue, which swallowed that and printed
# "Removed ... and its keys" over a private key still sitting on disk -- the worst of both, because
# the leftover is also what made the next up.ps1 run fail.
$failures = @()
if (Test-Path -LiteralPath $KeyDir) {
    foreach ($file in Get-ChildItem -LiteralPath $KeyDir -Force -File) {
        # One unremovable file should not hide the rest; they are all reported together below.
        try { Remove-KeyFile -Path $file.FullName } catch { $failures += $_.Exception.Message }
    }

    Remove-Item -LiteralPath $KeyDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Said only if it is true. A private key reported as deleted and left behind is worse than one
# reported as left behind.
$remaining = if (Test-Path -LiteralPath $KeyDir) {
    @(Get-ChildItem -LiteralPath $KeyDir -Force -File -ErrorAction SilentlyContinue)
} else { @() }

if ($remaining.Count -gt 0) {
    Write-Warning "Removed $Name, but $($remaining.Count) key file(s) remain in $KeyDir - delete them by hand:"
    $remaining | ForEach-Object { Write-Warning "  $($_.FullName)" }
    $failures | ForEach-Object { Write-Warning "  $_" }
    exit 1
}

Write-Host "Removed $Name and its keys."
