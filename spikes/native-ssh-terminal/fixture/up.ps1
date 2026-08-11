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

. (Join-Path $PSScriptRoot "keys.ps1")

docker rm -f $Name 2>$null | Out-Null

New-Item -ItemType Directory -Force -Path $KeyDir | Out-Null
$key = Join-Path $KeyDir "spike_key"

# The private key is written below with inheritance stripped, so a plain Remove-Item on a leftover
# from an earlier run fails with access denied and takes the whole script with it -- which is what
# "idempotent" above was claiming not to do.
foreach ($stale in @($key, "$key.pub")) {
    Remove-KeyFile -Path $stale
}

# The keypair is made before the server starts, in a throwaway container, so the public half can be
# handed to the image's own PUBLIC_KEY mechanism at run time.
#
# This used to write /config/.ssh/authorized_keys from a here-string after the container was up, and
# silently produced a file named `authorized_keys<CR>` — see the CRLF note below. sshd read the real,
# empty one the init had created, so key auth failed with "Permission denied (publickey)" while `ls`
# appeared to show the key installed.
#
# Handing the key to the image is better than fixing that write, because it removes the ordering
# question entirely: there is no window between the init creating its files and this one writing
# over them. It also drops the dependency on ssh-keygen being on the host's PATH, which it
# frequently is not.
Write-Host "Generating a keypair..."
$keygen = "$Name-keygen"
docker rm -f $keygen 2>$null | Out-Null
docker run -d --name $keygen --entrypoint sleep linuxserver/openssh-server:latest 300 | Out-Null
docker exec $keygen sh -c 'rm -f /tmp/spike_key /tmp/spike_key.pub; ssh-keygen -q -t ed25519 -N "" -f /tmp/spike_key -C spike' | Out-Null
if ($LASTEXITCODE -ne 0) { docker rm -f $keygen | Out-Null; throw "key generation failed" }
docker cp "${keygen}:/tmp/spike_key" $key | Out-Null
docker cp "${keygen}:/tmp/spike_key.pub" "$key.pub" | Out-Null
docker rm -f $keygen | Out-Null
if (-not (Test-Path $key) -or -not (Test-Path "$key.pub")) { throw "could not copy the keypair out of $keygen" }

$publicKey = (Get-Content "$key.pub" -Raw).Trim()

Write-Host "Starting $Name on port $Port..."
docker run -d --name $Name `
    -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC `
    -e PASSWORD_ACCESS=true -e USER_NAME=$User -e USER_PASSWORD=$Password `
    -e SUDO_ACCESS=false -e PUBLIC_KEY="$publicKey" `
    -p "127.0.0.1:${Port}:2222" linuxserver/openssh-server:latest | Out-Null

# Wait for sshd rather than sleeping a guessed amount.
#
# "sshd is listening" is necessary but not sufficient: the image's init is still populating /config
# after that line appears. Writing to /config in the gap silently loses the write -- which cost the
# benchmark payloads and, worse, authorized_keys, so key auth failed with no indication why. Probe
# the directory as well, because it is the thing actually being waited on.
$deadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Milliseconds 500
    $log = docker logs $Name 2>&1
    $listening = $log -match "sshd is listening"

    # The init's own completion marker. Waiting for /config to merely exist is not enough: the init
    # is still rebuilding its contents at that point, so a mkdir lands in a directory that is then
    # replaced and the write is lost with a success exit code.
    $initDone = $log -match "\[ls\.io-init\] done\."

    $ready = $listening -and $initDone
} until ($ready -or (Get-Date) -gt $deadline)
if (-not $ready) { throw "sshd did not come up within 60s" }

Write-Host "Creating benchmark payloads..."

# The carriage returns are stripped because this file has CRLF endings and `sh` does not treat one
# as whitespace: it becomes the last character of the last token on the line. `mkdir -p /config/bench`
# then creates a directory whose name ends in CR, and the redirect on the next line writes into
# `bench` -- which does not exist. The error reads
#
#     sh: can't create /config/bench/ascii.txt
#     : nonexistent directory
#
# where the stray line break is the CR being echoed back, and is the only clue that this is what
# happened. It is also what produced the two apparently identical `authorized_keys` entries that
# `ls` used to show: one real, one named `authorized_keys<CR>`.
$benchPayloads = (@'
mkdir -p /config/bench
yes 'The quick brown fox jumps over the lazy dog 0123456789 abcdefghijklmnopqrstuvwxyz' | head -n 65000 > /config/bench/ascii.txt
yes 'ελληνικά ρусский 日本語テキスト 中文测试 emoji: ✅★☂ — mixed 多字节 content' | head -n 20000 > /config/bench/utf8.txt
yes 'x=0123456789 abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789 abcdefghijklmnopqrstuvwxyz ABCDEFG' | head -n 40 > /config/bench/screen.txt
chmod -R a+r /config/bench
'@) -replace "`r", ""

docker exec $Name sh -c $benchPayloads | Out-Null

# Verified rather than assumed: a lost write here reports success and fails later inside the
# benchmark with a missing file.
$payloads = (docker exec $Name sh -c 'test -s /config/bench/ascii.txt && test -s /config/bench/utf8.txt && test -s /config/bench/screen.txt && echo PAYLOADS' 2>$null) -match "PAYLOADS"
if (-not $payloads) { throw "benchmark payloads were not written to /config/bench" }

# Password auth is what the image advertises, but its sshd_config ships PasswordAuthentication no.
docker exec $Name sh -c "sed -i 's/^PasswordAuthentication no/PasswordAuthentication yes/' /etc/ssh/sshd_config; pkill -HUP sshd" | Out-Null

# docker cp writes the key with inherited ACLs, which grants Authenticated Users. SSH.NET does not
# care, so mRemoteNG works either way, but ssh-add and ssh refuse the file outright. Tightening it
# here is what makes the agent testable: with this line, `ssh-add` accepts the key and an agent
# scenario can be exercised against this host.
icacls $key /inheritance:r /grant:r "$($env:USERNAME):R" | Out-Null

Write-Host ""
Write-Host "Ready." -ForegroundColor Green
Write-Host "  Benchmark : NativeTerminalSpike.exe --benchmark --port $Port --user $User --key `"$key`" --results results.json"
Write-Host "  Interactive: NativeTerminalSpike.exe --interactive --port $Port --user $User --key `"$key`""
Write-Host "  Password auth instead of --key: --password $Password"
