# ADR-0050: Transfers of several files show the progress of the whole

* Status: Accepted
* Date: 2026-11-18
* Requirements: FR-70, FR-60, FR-63, FR-64

## Context

Uploading a folder gave no clear picture of where the copy stood. Over a network share every file was its own row
with its own bar, so a folder of hundreds of files was hundreds of rows and nothing said how much was left in total.
Through Remote Desktop there was one row, but the helper only learned a file's size when it reached that file, so the
"total" grew as the copy went on and the bar behaved like a per-file bar; the file being copied was not shown, and
neither way showed a speed.

## Options considered

1. A summary bar above the list for everything that is running — simple, but mixes unrelated transfers and keeps the
   hundreds of rows.
2. One row per batch (the files queued together), with totals of all its files — matches the Remote Desktop row;
   per-file rows of a batch are no longer shown, so a failed file is reported in the row's text.
3. Let the helper and the queue compute speeds — needs the same logic twice (PowerShell and C#).

## Decision

Option 2. Files queued together (a folder, or several picked files) get a `BatchId`, the batch's name and its
destination folder in their journal, and the list shows them as one row (`TransferBatchRowViewModel`) whose Pause,
Resume and Cancel act on all its files. Each file's size is read when it is queued (`SizeHint`) so the total is right
from the start; the Remote Desktop helper reads every file's size (and its path in the transfer) up front, also for a
journal written by an older helper, and reports the current file with its bytes. Skipped files count for neither part
of the bytes. Summing (`TransferBatchProgress`), the average speed (`TransferRate`, sampled by the app every 2 s while
copying, so paused, waiting and retrying time does not count) and the texts (`TransferText`) are in `Harbor.Core`,
shared by both ways of copying and tested on any OS.

## Consequences

* A folder upload is one row with *file n of m*, *x of y (p%)*, average speed, time left and the file being copied now.
* Speed is measured by the app while the window is open, so it starts again when the window is reopened.
* Journals written before this change have no batch and still show one row per file.
* Per-file Pause/Resume inside a batch is gone; the batch's buttons act on all files.
* The time elapsed and the time a transfer took are measured from the first start (`TransferJob.StartedUtc`, the
  helper's `startedTicks`) to the end, so they survive a closed window, an app restart and a dropped connection, and
  include pauses. The helper stores its start as ticks because Windows PowerShell and PowerShell 7 write dates to JSON
  differently.
