<#
.SYNOPSIS
    Stands up the SSH host the native-terminal spike measures against.

.DESCRIPTION
    A real sshd in a container, so the spike measures a real SSH transport without anyone's
    production estate being involved. Creates the benchmark payloads and a keypair, and prints
    the arguments to hand to NativeTerminalSpike.exe.

    Idempotent: re-running replaces the container.
#>
param(
    [string]$Name = "mrng-spike-sshd",
    [int]$Port = 2222,
    [string]$User = "spike",
    [string]$Password = "spikepass",
    [string]$KeyDir = "$PSScriptRoot\.keys"
)

$ErrorActionPreference = "Stop"

docker rm -f $Name 2>$null | Out-Null

Write-Host "Starting $Name on port $Port..."
docker run -d --name $Name `
    -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC `
    -e PASSWORD_ACCESS=true -e USER_NAME=$User -e USER_PASSWORD=$Password `
    -e SUDO_ACCESS=false `
    -p "${Port}:2222" linuxserver/openssh-server:latest | Out-Null

# Wait for sshd rather than sleeping a guessed amount.
$deadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Milliseconds 500
    $ready = (docker logs $Name 2>&1) -match "sshd is listening"
} until ($ready -or (Get-Date) -gt $deadline)
if (-not $ready) { throw "sshd did not come up within 60s" }

Write-Host "Creating benchmark payloads..."
docker exec $Name sh -c @'
mkdir -p /config/bench
yes 'The quick brown fox jumps over the lazy dog 0123456789 abcdefghijklmnopqrstuvwxyz' | head -n 65000 > /config/bench/ascii.txt
yes 'ελληνικά ρусский 日本語テキスト 中文测试 emoji: ✅★☂ — mixed 多字节 content' | head -n 20000 > /config/bench/utf8.txt
yes 'x=0123456789 abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789 abcdefghijklmnopqrstuvwxyz ABCDEFG' | head -n 40 > /config/bench/screen.txt
chmod -R a+r /config/bench
'@ | Out-Null

# Password auth is what the image advertises, but its sshd_config ships PasswordAuthentication no.
docker exec $Name sh -c "sed -i 's/^PasswordAuthentication no/PasswordAuthentication yes/' /etc/ssh/sshd_config; pkill -HUP sshd" | Out-Null

Write-Host "Installing a keypair..."
New-Item -ItemType Directory -Force -Path $KeyDir | Out-Null
$key = Join-Path $KeyDir "spike_key"
if (Test-Path $key) { Remove-Item "$key*" -Force }
ssh-keygen -t ed25519 -N '""' -f $key -C spike | Out-Null
Get-Content "$key.pub" | docker exec -i $Name sh -c "mkdir -p /config/.ssh && cat > /config/.ssh/authorized_keys && chmod 700 /config/.ssh && chmod 600 /config/.ssh/authorized_keys && chown -R 1000:1000 /config/.ssh"

Write-Host ""
Write-Host "Ready." -ForegroundColor Green
Write-Host "  Benchmark : NativeTerminalSpike.exe --benchmark --port $Port --user $User --key `"$key`" --results results.json"
Write-Host "  Interactive: NativeTerminalSpike.exe --interactive --port $Port --user $User --key `"$key`""
Write-Host "  Password auth instead of --key: --password $Password"
