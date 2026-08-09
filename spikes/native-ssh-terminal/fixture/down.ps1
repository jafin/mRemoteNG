param([string]$Name = "mrng-spike-sshd")
docker rm -f $Name 2>$null | Out-Null
Remove-Item "$PSScriptRoot\.keys" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Removed $Name and its keys."
