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
    [int]$TestExitAfterChunks = 0,
    # Tests only: stop it right after this many files got their final name, before the journal is saved again.
    [int]$TestExitAfterFiles = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$PlanFolder = Split-Path -Parent $Plan
$StatusPath = Join-Path $PlanFolder 'status.json'
$CancelPath = Join-Path $PlanFolder 'cancel'
$script:ChunksWritten = 0
$script:FilesFinished = 0
$script:LastStatus = $null

# SPEC FR-70, ADR-0051: speed. status.json and the cancel file are on the PC, so every look at them is a round trip
# over Remote Desktop; the journal holds every chunk's hash. None of them is touched after every chunk any more.
# What matters is the journal here on the server; the PC only shows progress, so it is told seldom.
$StatusEvery = [TimeSpan]::FromSeconds(10)     # status.json on the PC while copying (any other state at once)
$CancelEvery = [TimeSpan]::FromSeconds(3)      # the cancel file on the PC
$ProgressEvery = [TimeSpan]::FromSeconds(1)    # the console's progress bar, here in the session
$CheckpointEvery = [TimeSpan]::FromSeconds(2)  # flush to disk + journal while a file is copied
$ReadAhead = 4                                  # chunks read at once, each on its own handle, to hide the latency
$script:LastStatusWrite = [DateTime]::MinValue
$script:LastProgress = [DateTime]::MinValue
$script:LastCancelCheck = [DateTime]::MinValue
$script:CancelSeen = $false
$script:Sha = [Security.Cryptography.SHA256]::Create()

function Write-JsonFile([string]$Path, $Value) {
    $temp = "$Path.tmp"
    [IO.File]::WriteAllText($temp, ($Value | ConvertTo-Json -Depth 8))
    if (-not [IO.File]::Exists($Path)) { [IO.File]::Move($temp, $Path); return }
    # Atomic on NTFS; a shared PC drive may not support it, then replace it in two steps.
    try { [IO.File]::Replace($temp, $Path, $null) }
    catch { [IO.File]::Delete($Path); [IO.File]::Move($temp, $Path) }
}

function Get-Hash([byte[]]$Buffer, [int]$Count) {
    return ([BitConverter]::ToString($script:Sha.ComputeHash($Buffer, 0, $Count)) -replace '-', '').ToLowerInvariant()
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

# Looks for the cancel file at most every few seconds (it is on the PC); once seen, it stays cancelled.
function Test-Cancelled([switch]$Now) {
    if ($script:CancelSeen) { return $true }
    $time = [DateTime]::UtcNow
    if (-not $Now -and ($time - $script:LastCancelCheck) -lt $CancelEvery) { return $false }
    $script:LastCancelCheck = $time
    try { $script:CancelSeen = [IO.File]::Exists($CancelPath) } catch { }
    return $script:CancelSeen
}

function Save-Status([string]$State, [string]$Message, [switch]$Force) {
    # While copying, the console's bar at most once a second and status.json on the PC every $StatusEvery; any other
    # state at once.
    $time = [DateTime]::UtcNow
    $copying = $State -eq 'running' -and -not $Force
    $toPc = -not $copying -or ($time - $script:LastStatusWrite) -ge $StatusEvery
    if (-not $toPc -and ($time - $script:LastProgress) -lt $ProgressEvery) { return }
    $script:LastProgress = $time
    if ($toPc) { $script:LastStatusWrite = $time }
    $done = @($Journal.items | Where-Object { $_.state -eq 'done' -or $_.state -eq 'skipped' }).Count
    $skipped = @($Journal.items | Where-Object { $_.state -eq 'skipped' }).Count
    # SPEC FR-70: the total is every file's size from the start (skipped files count for neither part), so the bar is a
    # share of the whole transfer, not of the files reached so far.
    $bytesDone = 0; $bytesTotal = 0; $current = $null; $currentDone = 0; $currentTotal = 0
    foreach ($i in $Journal.items) {
        if ($i.state -eq 'skipped') { continue }
        $length = if ($i.length -ge 0) { $i.length } elseif ($i.size -ge 0) { $i.size } else { 0 }
        $bytesTotal += $length
        if ($i.state -eq 'done') { $bytesDone += $length } else { $bytesDone += [Math]::Min($i.bytesDone, $length) }
        if ($null -eq $current -and $i.state -eq 'pending') { $current = $i.name; $currentDone = $i.bytesDone; $currentTotal = $length }
    }
    $status = [ordered]@{
        state = $State; filesDone = $done; filesTotal = @($Journal.items).Count; filesSkipped = $skipped; bytesDone = $bytesDone; bytesTotal = $bytesTotal
        currentFile = $current; currentFileBytesDone = $currentDone; currentFileBytes = $currentTotal
        error = $Message; updatedUtc = [DateTime]::UtcNow.ToString('o')
        # SPEC FR-70: when the transfer first started and when it was done, for the time elapsed and the time it took.
        startedUtc = (New-Object DateTime ([long]$Journal.startedTicks), ([DateTimeKind]::Utc)).ToString('o')
        finishedUtc = if ($State -eq 'done') { [DateTime]::UtcNow.ToString('o') } else { $null }
    }
    $script:LastStatus = $status
    if ($toPc) { try { Write-JsonFile $StatusPath $status } catch { } } # the PC may be disconnected right now: best effort
    $percent = if ($bytesTotal -gt 0) { [int](100 * $bytesDone / $bytesTotal) } else { 0 }
    $text = if ($Message) { $Message } else { "$done of $(@($Journal.items).Count) files" }
    $operation = if ($current) { $current } else { ' ' }
    Write-Progress -Activity 'Remote Desktop Harbor file transfer' -Status "$State - $text" -CurrentOperation $operation -PercentComplete ([Math]::Min(100, $percent))
}

function Save-Journal { Write-JsonFile $JournalPath $Journal }

# A finished file is one line appended here, not a rewrite of the whole journal (which lists every file): cheap for a
# folder of many small files, and safe right after the file got its final name. Read back when the helper starts.
function Save-Finished($Item) {
    [IO.File]::AppendAllText($FinishedPath, "$($Item.index) $($Item.state)`n")
}

function Expand-Folder([string]$Folder) { return [Environment]::ExpandEnvironmentVariables($Folder) }

# ---------------------------------------------------------------- plan and journal

$PlanData = [IO.File]::ReadAllText($Plan) | ConvertFrom-Json
$localData = $env:LOCALAPPDATA
if (-not $localData) { $localData = [Environment]::GetFolderPath('LocalApplicationData') }
$JournalFolder = Join-Path (Join-Path $localData 'RemoteDesktopHarbor') 'transfers'
[void][IO.Directory]::CreateDirectory($JournalFolder)
$JournalPath = Join-Path $JournalFolder "$($PlanData.id).json"
$FinishedPath = Join-Path $JournalFolder "$($PlanData.id).finished"

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
                    name = ($_.destination -replace '[\\/]', [IO.Path]::DirectorySeparatorChar)
                    length = -1; lastWriteTicks = 0; bytesDone = 0; hashes = @(); state = 'pending'; error = $null
                }
            })
    }
    Save-Journal
}

# SPEC FR-70: every file's size and its path in the transfer, read once up front (a journal from an older helper gets
# them now), for the whole transfer's progress and the name of the file being copied.
$journalChanged = $false
# Ticks, not a date: Windows PowerShell and PowerShell 7 write and read dates in JSON differently.
if ($null -eq $Journal.PSObject.Properties['startedTicks']) {
    $Journal | Add-Member -NotePropertyName startedTicks -NotePropertyValue ([DateTime]::UtcNow.Ticks)
    $journalChanged = $true
}
foreach ($i in $Journal.items) {
    if ($null -eq $i.PSObject.Properties['size']) {
        $size = -1
        try { $file = New-Object IO.FileInfo $i.source; if ($file.Exists) { $size = $file.Length } } catch { }
        $i | Add-Member -NotePropertyName size -NotePropertyValue $size
        $journalChanged = $true
    }
    if ($null -eq $i.PSObject.Properties['name']) {
        $i | Add-Member -NotePropertyName name -NotePropertyValue (Split-Path -Leaf $i.destination)
        $journalChanged = $true
    }
}
$items = @($Journal.items)
for ($n = 0; $n -lt $items.Count; $n++) {
    if ($null -eq $items[$n].PSObject.Properties['index']) { $items[$n] | Add-Member -NotePropertyName index -NotePropertyValue $n; $journalChanged = $true }
}
if ([IO.File]::Exists($FinishedPath)) {
    foreach ($line in [IO.File]::ReadAllLines($FinishedPath)) {
        $parts = $line.Split(' ')
        if ($parts.Count -ne 2) { continue } # a line cut short by a crash
        $n = 0
        if (-not [int]::TryParse($parts[0], [ref]$n) -or $n -lt 0 -or $n -ge $items.Count) { continue }
        if ($parts[1] -eq 'done' -or $parts[1] -eq 'skipped') {
            $items[$n].state = $parts[1]; $items[$n].error = $null
            if ($parts[1] -eq 'done' -and $items[$n].length -ge 0) { $items[$n].bytesDone = $items[$n].length }
            $journalChanged = $true
        }
    }
}
if ($journalChanged) { Save-Journal; if ([IO.File]::Exists($FinishedPath)) { [IO.File]::Delete($FinishedPath) } }

$ChunkSize = [int]$PlanData.chunkSize
if ($ChunkSize -le 0) { $ChunkSize = 4MB }
$Overwrite = [bool]$PlanData.overwrite
# SPEC FR-64: leave files that already exist alone. Plans written before this option have no such property.
$SkipExisting = (-not $Overwrite) -and ($null -ne $PlanData.PSObject.Properties['skipExisting']) -and [bool]$PlanData.skipExisting

# ---------------------------------------------------------------- copying

function Reset-Item($Item, $Info) {
    $Item.length = $Info.Length
    $Item.lastWriteTicks = $Info.LastWriteTimeUtc.Ticks
    $Item.bytesDone = 0
    $Item.hashes = New-Object 'Collections.Generic.List[string]'
}

# The chunk hashes as a list that grows in place: adding to a PowerShell array copies all of it every time.
function Get-HashList($Item) {
    $list = New-Object 'Collections.Generic.List[string]'
    foreach ($h in @($Item.hashes)) { if ($null -ne $h) { $list.Add([string]$h) } }
    return , $list
}

# Steps bytesDone back to what the partial file really holds (length, then the last chunk's hash).
function Restore-Verified($Item, [IO.FileStream]$Partial, [byte[]]$Buffer) {
    $hashes = Get-HashList $Item
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
            if ($read -eq $size -and (Get-Hash $Buffer $size) -eq $hashes[$last]) { $Item.hashes = $hashes; return }
        }
        $hashes.RemoveAt($last)
        $Item.bytesDone = $start
    }
    $Item.hashes = New-Object 'Collections.Generic.List[string]'
    $Item.bytesDone = 0
}

# Flushes what was written to disk, then records it in the journal: the journal never claims bytes the destination
# may not have. Every couple of seconds while a file is copied (ADR-0051), or at once with -Force.
function Save-Checkpoint([IO.FileStream]$Output, [switch]$Force) {
    $time = [DateTime]::UtcNow
    if (-not $Force -and ($time - $script:LastCheckpoint) -lt $CheckpointEvery) { return }
    $Output.Flush($true)
    Save-Journal
    $script:LastCheckpoint = $time
}

# Starts reading one chunk on its own handle; several run at once so the PC's shared drive is never idle.
function Start-ChunkRead([IO.FileStream]$Stream, [long]$Offset, [int]$Size) {
    $buffer = New-Object byte[] $Size
    [void]$Stream.Seek($Offset, 'Begin')
    return [pscustomobject]@{ Stream = $Stream; Size = $Size; Buffer = $buffer; Task = $Stream.ReadAsync($buffer, 0, $Size) }
}

function Complete-ChunkRead($Read, [string]$Source) {
    $total = $Read.Task.GetAwaiter().GetResult()
    if ($total -le 0 -and $Read.Size -gt 0) { throw (New-Object IO.IOException "$Source ended early.") }
    while ($total -lt $Read.Size) {
        $n = $Read.Stream.Read($Read.Buffer, $total, $Read.Size - $total)
        if ($n -le 0) { throw (New-Object IO.IOException "$Source ended early.") }
        $total += $n
    }
}

function Copy-OneFile($Item) {
    $info = New-Object IO.FileInfo $Item.source
    if ($SkipExisting -and [IO.File]::Exists($Item.destination)) {
        $Item.state = 'skipped'
        $Item.error = $null
        Save-Finished $Item
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
    $readers = New-Object 'Collections.Generic.List[IO.FileStream]'
    $pending = New-Object 'Collections.Generic.Queue[object]'
    try {
        Restore-Verified $Item $output $buffer
        $output.SetLength($Item.bytesDone)
        [void]$output.Seek($Item.bytesDone, 'Begin')
        $script:LastCheckpoint = [DateTime]::UtcNow
        $hashes = $Item.hashes
        $chunksLeft = [long][Math]::Ceiling(($Item.length - $Item.bytesDone) / $ChunkSize)
        $depth = [int][Math]::Max(1, [Math]::Min($ReadAhead, $chunksLeft))
        for ($r = 0; $r -lt $depth; $r++) { $readers.Add([IO.File]::Open($Item.source, 'Open', 'Read', 'Read')) }

        $nextRead = $Item.bytesDone
        $slot = 0
        while ($Item.bytesDone -lt $Item.length) {
            # Keep $depth reads in flight; chunk k uses handle k mod $depth, whose previous read is done by then.
            while ($pending.Count -lt $depth -and $nextRead -lt $Item.length) {
                $size = [int][Math]::Min($ChunkSize, $Item.length - $nextRead)
                $pending.Enqueue((Start-ChunkRead $readers[$slot % $depth] $nextRead $size))
                $nextRead += $size
                $slot++
            }
            if (Test-Cancelled) { return $false }
            $chunk = $pending.Dequeue()
            Complete-ChunkRead $chunk $Item.source
            $output.Write($chunk.Buffer, 0, $chunk.Size)
            $hashes.Add((Get-Hash $chunk.Buffer $chunk.Size))
            $Item.bytesDone += $chunk.Size
            Save-Checkpoint $output
            Save-Status 'running' $null
            $script:ChunksWritten++
            if ($TestExitAfterChunks -gt 0 -and $script:ChunksWritten -eq $TestExitAfterChunks) {
                Save-Checkpoint $output -Force; Save-Status 'running' $null -Force; exit 3
            }
            if ($TestFailAfterChunks -gt 0 -and $script:ChunksWritten -eq $TestFailAfterChunks) {
                throw (New-Object IO.IOException 'Simulated connection drop.')
            }
        }
        Save-Checkpoint $output -Force
    }
    finally {
        # Let reads still in flight end before their handles are closed.
        foreach ($p in $pending) { try { [void]$p.Task.GetAwaiter().GetResult() } catch { } }
        foreach ($r in $readers) { $r.Dispose() }
        $output.Dispose()
    }

    $after = New-Object IO.FileInfo $Item.source
    if (-not $after.Exists -or $after.Length -ne $Item.length -or $after.LastWriteTimeUtc.Ticks -ne $Item.lastWriteTicks) {
        throw (New-Object IO.IOException "$($Item.source) changed while it was copied; it is copied again.")
    }

    if ([IO.File]::Exists($Item.destination)) { [IO.File]::Delete($Item.destination) }
    [IO.File]::Move($partialPath, $Item.destination)
    $Item.state = 'done'
    $Item.error = $null
    Save-Finished $Item
    $script:FilesFinished++
    if ($TestExitAfterFiles -gt 0 -and $script:FilesFinished -eq $TestExitAfterFiles) { exit 3 }
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
    try { [IO.File]::Delete($JournalPath); [IO.File]::Delete($FinishedPath) } catch { }
    Save-Status 'cancelled' 'Cancelled.'
    Write-Host 'Transfer cancelled.'
    Close-Lock
    exit 2
}

$failed = @($Journal.items | Where-Object { $_.state -eq 'failed' })
if ($failed.Count -eq 0) {
    [IO.File]::Delete($JournalPath)
    if ([IO.File]::Exists($FinishedPath)) { [IO.File]::Delete($FinishedPath) }
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
