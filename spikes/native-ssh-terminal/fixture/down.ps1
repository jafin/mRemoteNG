param([string]$Name = "mrng-spike-sshd")

docker rm -f $Name 2>$null | Out-Null
docker rm -f "$Name-keygen" 2>$null | Out-Null

# up.ps1 writes the private key with ACL inheritance stripped, so a plain Remove-Item fails with
# access denied. This used to be -ErrorAction SilentlyContinue, which swallowed that and printed
# "Removed ... and its keys" over a private key still sitting on disk -- the worst of both, because
# the leftover is also what made the next up.ps1 run fail.
$keyDir = Join-Path $PSScriptRoot ".keys"
if (Test-Path -LiteralPath $keyDir) {
    foreach ($file in Get-ChildItem -LiteralPath $keyDir -Force -File) {
        try { $file.IsReadOnly = $false } catch { }
        icacls $file.FullName /reset /Q 2>&1 | Out-Null
        icacls $file.FullName /grant "$($env:USERNAME):(F)" /Q 2>&1 | Out-Null
        Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $keyDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Said only if it is true. A private key reported as deleted and left behind is worse than one
# reported as left behind.
$remaining = if (Test-Path -LiteralPath $keyDir) {
    @(Get-ChildItem -LiteralPath $keyDir -Force -File -ErrorAction SilentlyContinue)
} else { @() }

if ($remaining.Count -gt 0) {
    Write-Warning "Removed $Name, but $($remaining.Count) key file(s) remain in $keyDir - delete them by hand:"
    $remaining | ForEach-Object { Write-Warning "  $($_.FullName)" }
    exit 1
}

Write-Host "Removed $Name and its keys."
