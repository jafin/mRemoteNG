<#
.SYNOPSIS
    Deleting the key files the fixture locks down, shared by up.ps1 and down.ps1.

.DESCRIPTION
    up.ps1 strips inheritance from the private key so ssh and ssh-add will accept it. That is also
    what makes the file awkward to delete afterwards, and both scripts have to do it: up.ps1 to be
    re-runnable over its own leftovers, down.ps1 to be able to say the key is gone. Doing it in one
    place is what keeps the two from disagreeing about how.
#>

function Remove-KeyFile {
    <#
    .SYNOPSIS
        Deletes one key file, recovering the access needed to do it.
    .DESCRIPTION
        Throws if the file is still there afterwards, with whatever the recovery attempts said.
        Silence would mean a private key left on disk while the caller reports success.
    #>
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }

    try { (Get-Item -LiteralPath $Path -Force).IsReadOnly = $false } catch { }

    # icacls edits the DACL and cannot touch the owner, so ownership comes first: a file left by
    # another account -- an elevated shell, or a colleague on a shared machine -- refuses the grant
    # as well as the delete, and resetting an ACL you have no right to reset changes nothing.
    #
    # None of these three is worth failing on by itself. Only the delete decides, and it often
    # succeeds without any of them; their output is kept back to explain a failure if one comes.
    $recovery = @()
    foreach ($attempt in @(
        @{ Label = "takeown"; Run = { takeown /F $Path } },
        @{ Label = "icacls /reset"; Run = { icacls $Path /reset /Q } },
        @{ Label = "icacls /grant"; Run = { icacls $Path /grant "$($env:USERNAME):(F)" /Q } }
    )) {
        $output = & $attempt.Run 2>&1
        if ($LASTEXITCODE -ne 0) { $recovery += "$($attempt.Label) failed: $($output -join ' ')" }
    }

    try {
        Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
    }
    catch {
        $detail = if ($recovery) { " ($($recovery -join '; '))" } else { "" }
        throw "could not delete ${Path}${detail}: $($_.Exception.Message)"
    }
}
