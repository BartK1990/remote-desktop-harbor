# Remote Desktop Harbor - file transfer helper (SPEC FR-63, ADR-0041).
#
# Runs inside the Remote Desktop session on the server and copies files between the server and the PC's drives shared
# into the session (\\tsclient\<drive>), so only the Remote Desktop port is needed. Each file is copied in chunks to
# <name>.harbor-part; after every flushed chunk its position and SHA-256 are saved in a journal on the server
# (%LOCALAPPDATA%\RemoteDesktopHarbor\transfers). When Remote Desktop drops, the session and this helper keep running,
# wait for the connection to come back and continue where they stopped; running the start command again resumes too.
# Progress is written to status.json next to the plan, where Remote Desktop Harbor shows it.
# Works with Windows PowerShell 5.1 and PowerShell 7. Holds and logs no password.
param(
    [Parameter(Mandatory = $true)][string]$Plan,
    [switch]$NoPause,
    # Tests only: throw a simulated connection drop after this many chunks (once).
    [int]$TestFailAfterChunks = 0,
    # Tests only: stop the whole helper after this many chunks, as if the session had ended.
    [int]$TestExitAfterChunks = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$PlanFolder = Split-Path -Parent $Plan
$StatusPath = Join-Path $PlanFolder 'status.json'
$CancelPath = Join-Path $PlanFolder 'cancel'
$script:ChunksWritten = 0
$script:LastStatus = $null

function Write-JsonFile([string]$Path, $Value) {
    $temp = "$Path.tmp"
    [IO.File]::WriteAllText($temp, ($Value | ConvertTo-Json -Depth 8))
    if (-not [IO.File]::Exists($Path)) { [IO.File]::Move($temp, $Path); return }
    # Atomic on NTFS; a shared PC drive may not support it, then replace it in two steps.
    try { [IO.File]::Replace($temp, $Path, $null) }
    catch { [IO.File]::Delete($Path); [IO.File]::Move($temp, $Path) }
}

function Get-Hash([byte[]]$Buffer, [int]$Count) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Buffer, 0, $Count)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-Inner($ErrorRecord) {
    $e = $ErrorRecord.Exception
    while ($null -ne $e.InnerException) { $e = $e.InnerException }
    return $e
}

# \\server\share of a UNC path, $null for local paths.
function Get-ShareRoot([string]$Path) {
    if ($Path -notmatch '^[\\/]{2}([^\\/]+)[\\/]+([^\\/]+)') { return $null }
    return "\\$($Matches[1])\$($Matches[2])"
}

# True when every \\tsclient drive the item uses answers, i.e. Remote Desktop is connected.
function Test-ClientReachable($Item) {
    foreach ($p in @($Item.source, $Item.destination)) {
        $root = Get-ShareRoot $p
        if ($null -ne $root -and -not (Test-Path -LiteralPath $root)) { return $false }
    }
    return $true
}

function Test-Cancelled {
    try { return [IO.File]::Exists($CancelPath) } catch { return $false }
}

function Save-Status([string]$State, [string]$Message) {
    $done = @($Journal.items | Where-Object { $_.state -eq 'done' -or $_.state -eq 'skipped' }).Count
    $skipped = @($Journal.items | Where-Object { $_.state -eq 'skipped' }).Count
    $bytesDone = 0; $bytesTotal = 0; $current = $null
    foreach ($i in $Journal.items) {
        if ($i.length -ge 0) { $bytesTotal += $i.length }
        if ($i.state -eq 'done') { $bytesDone += $i.length } else { $bytesDone += $i.bytesDone }
        if ($null -eq $current -and $i.state -eq 'pending') { $current = Split-Path -Leaf $i.destination }
    }
    $status = [ordered]@{
        state = $State; filesDone = $done; filesTotal = @($Journal.items).Count; filesSkipped = $skipped; bytesDone = $bytesDone; bytesTotal = $bytesTotal
        currentFile = $current; error = $Message; updatedUtc = [DateTime]::UtcNow.ToString('o')
    }
    $script:LastStatus = $status
    try { Write-JsonFile $StatusPath $status } catch { } # the PC may be disconnected right now: best effort
    $percent = if ($bytesTotal -gt 0) { [int](100 * $bytesDone / $bytesTotal) } else { 0 }
    $text = if ($Message) { $Message } else { "$done of $(@($Journal.items).Count) files" }
    Write-Progress -Activity 'Remote Desktop Harbor file transfer' -Status "$State - $text" -PercentComplete ([Math]::Min(100, $percent))
}

function Save-Journal { Write-JsonFile $JournalPath $Journal }

function Expand-Folder([string]$Folder) { return [Environment]::ExpandEnvironmentVariables($Folder) }

# ---------------------------------------------------------------- plan and journal

$PlanData = [IO.File]::ReadAllText($Plan) | ConvertFrom-Json
$localData = $env:LOCALAPPDATA
if (-not $localData) { $localData = [Environment]::GetFolderPath('LocalApplicationData') }
$JournalFolder = Join-Path (Join-Path $localData 'RemoteDesktopHarbor') 'transfers'
[void][IO.Directory]::CreateDirectory($JournalFolder)
$JournalPath = Join-Path $JournalFolder "$($PlanData.id).json"

# One helper per transfer: a second start (the app starting it again after a reconnect, ADR-0042) leaves the running
# one alone. The lock is released when this process ends, however it ends.
$LockFolder = Join-Path $JournalFolder 'running'
[void][IO.Directory]::CreateDirectory($LockFolder)
$LockPath = Join-Path $LockFolder "$($PlanData.id).lock"
try { $script:Lock = New-Object IO.FileStream $LockPath, 'OpenOrCreate', 'ReadWrite', 'None', 1, 'DeleteOnClose' }
catch {
    Write-Host "The transfer '$($PlanData.label)' is already running in this session."
    if (-not $NoPause) { Start-Sleep -Seconds 5 }
    exit 4
}

function Close-Lock { try { $script:Lock.Dispose(); [IO.File]::Delete($LockPath) } catch { } }

if ([IO.File]::Exists($JournalPath)) {
    $Journal = [IO.File]::ReadAllText($JournalPath) | ConvertFrom-Json
    Write-Host "Resuming the transfer '$($PlanData.label)'."
}
else {
    $items = @($PlanData.items)
    $destinationBase = if ($PlanData.isUpload) { Expand-Folder $PlanData.serverFolder } else { $PlanData.clientFolder }
    if (-not $PlanData.isUpload -and $items.Count -eq 0) {
        # Download: the files are picked here, on the server.
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Multiselect = $true
        $dialog.Title = "Files to copy to your PC ($($PlanData.clientFolder))"
        $start = Expand-Folder $PlanData.serverFolder
        if ([IO.Directory]::Exists($start)) { $dialog.InitialDirectory = $start }
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Write-Host 'Nothing selected.'; Close-Lock; exit 0 }
        $items = @($dialog.FileNames | ForEach-Object { [pscustomobject]@{ source = $_; destination = (Split-Path -Leaf $_) } })
    }

    $Journal = [pscustomobject]@{
        id = $PlanData.id
        items = @($items | ForEach-Object {
                [pscustomobject]@{
                    source = $_.source
                    destination = (Join-Path $destinationBase ($_.destination -replace '[\\/]', [IO.Path]::DirectorySeparatorChar))
                    length = -1; lastWriteTicks = 0; bytesDone = 0; hashes = @(); state = 'pending'; error = $null
                }
            })
    }
    Save-Journal
}

$ChunkSize = [int]$PlanData.chunkSize
if ($ChunkSize -le 0) { $ChunkSize = 1MB }
$Overwrite = [bool]$PlanData.overwrite
# SPEC FR-64: leave files that already exist alone. Plans written before this option have no such property.
$SkipExisting = (-not $Overwrite) -and ($null -ne $PlanData.PSObject.Properties['skipExisting']) -and [bool]$PlanData.skipExisting

# ---------------------------------------------------------------- copying

function Reset-Item($Item, $Info) {
    $Item.length = $Info.Length
    $Item.lastWriteTicks = $Info.LastWriteTimeUtc.Ticks
    $Item.bytesDone = 0
    $Item.hashes = @()
}

# Steps bytesDone back to what the partial file really holds (length, then the last chunk's hash).
function Restore-Verified($Item, [IO.FileStream]$Partial, [byte[]]$Buffer) {
    $hashes = [Collections.ArrayList]@($Item.hashes)
    while ($hashes.Count -gt 0) {
        $last = $hashes.Count - 1
        $start = [long]$last * $ChunkSize
        $size = [int]($Item.bytesDone - $start)
        if ($size -le 0 -or $size -gt $ChunkSize) { break }
        if ($Item.bytesDone -le $Partial.Length) {
            [void]$Partial.Seek($start, 'Begin')
            $read = 0
            while ($read -lt $size) {
                $n = $Partial.Read($Buffer, $read, $size - $read)
                if ($n -le 0) { break }
                $read += $n
            }
            if ($read -eq $size -and (Get-Hash $Buffer $size) -eq $hashes[$last]) { $Item.hashes = @($hashes); return }
        }
        $hashes.RemoveAt($last)
        $Item.bytesDone = $start
    }
    $Item.hashes = @()
    $Item.bytesDone = 0
}

function Copy-OneFile($Item) {
    $info = New-Object IO.FileInfo $Item.source
    if ($SkipExisting -and [IO.File]::Exists($Item.destination)) {
        $Item.state = 'skipped'
        $Item.error = $null
        Save-Journal
        return 'skipped'
    }
    if (-not $info.Exists) { throw (New-Object IO.FileNotFoundException "$($Item.source) no longer exists.") }
    if ($Item.length -ne $info.Length -or $Item.lastWriteTicks -ne $info.LastWriteTimeUtc.Ticks) { Reset-Item $Item $info }
    if (-not $Overwrite -and [IO.File]::Exists($Item.destination)) {
        throw (New-Object InvalidOperationException "$($Item.destination) already exists.")
    }

    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Item.destination))
    $partialPath = "$($Item.destination).harbor-part"
    $buffer = New-Object byte[] $ChunkSize
    $output = [IO.File]::Open($partialPath, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        Restore-Verified $Item $output $buffer
        $output.SetLength($Item.bytesDone)
        Save-Journal
        $reader = [IO.File]::Open($Item.source, 'Open', 'Read', 'Read')
        try {
            [void]$reader.Seek($Item.bytesDone, 'Begin')
            [void]$output.Seek($Item.bytesDone, 'Begin')
            while ($Item.bytesDone -lt $Item.length) {
                if (Test-Cancelled) { return $false }
                $wanted = [int][Math]::Min($ChunkSize, $Item.length - $Item.bytesDone)
                $read = 0
                while ($read -lt $wanted) {
                    $n = $reader.Read($buffer, $read, $wanted - $read)
                    if ($n -le 0) { throw (New-Object IO.IOException "$($Item.source) ended early.") }
                    $read += $n
                }
                $output.Write($buffer, 0, $wanted)
                $output.Flush($true)
                # Recorded only after the flush: the journal never claims bytes the destination may not have.
                $Item.hashes = @($Item.hashes) + (Get-Hash $buffer $wanted)
                $Item.bytesDone += $wanted
                Save-Journal
                Save-Status 'running' $null
                $script:ChunksWritten++
                if ($TestExitAfterChunks -gt 0 -and $script:ChunksWritten -eq $TestExitAfterChunks) { exit 3 }
                if ($TestFailAfterChunks -gt 0 -and $script:ChunksWritten -eq $TestFailAfterChunks) {
                    throw (New-Object IO.IOException 'Simulated connection drop.')
                }
            }
        }
        finally { $reader.Dispose() }
    }
    finally { $output.Dispose() }

    $after = New-Object IO.FileInfo $Item.source
    if (-not $after.Exists -or $after.Length -ne $Item.length -or $after.LastWriteTimeUtc.Ticks -ne $Item.lastWriteTicks) {
        throw (New-Object IO.IOException "$($Item.source) changed while it was copied; it is copied again.")
    }

    if ([IO.File]::Exists($Item.destination)) { [IO.File]::Delete($Item.destination) }
    [IO.File]::Move($partialPath, $Item.destination)
    $Item.state = 'done'
    $Item.error = $null
    Save-Journal
    return 'copied'
}

function Remove-Partials {
    foreach ($i in $Journal.items) {
        if ($i.state -ne 'done') { try { [IO.File]::Delete("$($i.destination).harbor-part") } catch { } }
    }
}

$GiveUpAfter = [TimeSpan]::FromMinutes(30)
$cancelled = $false
foreach ($item in $Journal.items) {
    if ($item.state -ne 'pending') { continue }
    $delay = 2
    $lastProgress = [DateTime]::UtcNow
    $bytesAtProgress = $item.bytesDone
    while ($true) {
        if (Test-Cancelled) { $cancelled = $true; break }
        try {
            Save-Status 'running' $null
            $result = Copy-OneFile $item
            if ($result -eq 'skipped') { Write-Host "Skipped $($item.destination): it already exists"; break }
            if ($result -eq 'copied') { Write-Host "Copied $($item.destination)"; break }
            $cancelled = $true
            break
        }
        catch [System.Management.Automation.ExitException] { throw } # exit inside a script block is an exception
        catch {
            $err = Get-Inner $_
            if ($item.bytesDone -gt $bytesAtProgress) { $bytesAtProgress = $item.bytesDone; $lastProgress = [DateTime]::UtcNow; $delay = 2 }
            if (-not (Test-ClientReachable $item)) {
                # Remote Desktop is disconnected: the session keeps running; wait for it to come back, however long.
                Save-Status 'waiting' 'Waiting for Remote Desktop to reconnect.'
                $lastProgress = [DateTime]::UtcNow
            }
            elseif ($err -is [IO.FileNotFoundException] -or $err -is [UnauthorizedAccessException] -or $err -is [InvalidOperationException] -or
                    ([DateTime]::UtcNow - $lastProgress) -ge $GiveUpAfter) {
                $item.state = 'failed'
                $item.error = $err.Message
                Save-Journal
                Write-Host "Failed: $($item.source): $($err.Message)" -ForegroundColor Red
                break
            }
            else {
                Save-Status 'waiting' "$($err.Message) Retrying."
            }
            Start-Sleep -Seconds $delay
            $delay = [Math]::Min($delay * 2, 30)
        }
    }
    if ($cancelled) { break }
}

if ($cancelled) {
    Remove-Partials
    try { [IO.File]::Delete($JournalPath) } catch { }
    Save-Status 'cancelled' 'Cancelled.'
    Write-Host 'Transfer cancelled.'
    Close-Lock
    exit 2
}

$failed = @($Journal.items | Where-Object { $_.state -eq 'failed' })
if ($failed.Count -eq 0) {
    [IO.File]::Delete($JournalPath)
    Save-Status 'done' $null
    $skippedCount = @($Journal.items | Where-Object { $_.state -eq 'skipped' }).Count
    $copiedCount = @($Journal.items).Count - $skippedCount
    $skippedText = if ($skippedCount -gt 0) { ", $skippedCount skipped because they already exist" } else { '' }
    Write-Host "Done: $copiedCount file(s) copied$skippedText." -ForegroundColor Green
    $code = 0
}
else {
    Save-Status 'failed' "$($failed.Count) file(s) failed: $($failed[0].error)"
    Write-Host "$($failed.Count) file(s) failed. Fix the problem and run the start command again to retry them." -ForegroundColor Red
    foreach ($f in $failed) { $f.state = 'pending' } # a new run retries them
    Save-Journal
    $code = 1
}

Close-Lock
if (-not $NoPause) { [void](Read-Host 'Press Enter to close') }
exit $code
