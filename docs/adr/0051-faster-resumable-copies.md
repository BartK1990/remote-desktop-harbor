# ADR-0051: Resumable copies save their progress every second or two, not after every chunk, and read ahead

* Status: Accepted
* Date: 2026-11-18
* Requirements: FR-61, FR-63, FR-70
* Amends: ADR-0039 (journal after every chunk), ADR-0041 (status after every chunk), ADR-0042 (1 MiB chunks; now
  4 MiB by default and adjustable)

## Context

Copying through Remote Desktop was much slower than copy and paste in the session, although both use the same
connection. Copy and paste streams the file over the clipboard channel. The helper reads through the PC's shared drive
(`\\tsclient`), which answers one request at a time, and after **every** 1 MiB chunk it:

* wrote `status.json` (on the PC, so several round trips over Remote Desktop) and checked for the cancel file (one more);
* flushed the destination to disk and rewrote its whole journal, which holds the hash of every chunk so far and grows
  with the file (adding to a PowerShell array also copied all of it every time);
* redrew the console's progress bar (slow in Windows PowerShell);
* only then started reading the next chunk.

After each finished file it rewrote the journal again, which lists every file: slow for folders of many small files.
The network-share copier also wrote its journal after every chunk and read and wrote in turn.

## Options considered

1. Bigger chunks — fewer round trips per byte, but more lost on a drop and coarser progress; the per-chunk work stays.
2. Keep the chunk, do the expensive work on a timer, and read ahead — same resume guarantees, much less work per byte.
3. Copy several files at once — helps folders of small files most, but the journal, progress and "file n of m" become
   much more complex. Left for later.

## Decision

Option 2.

* Helper: what matters is the journal on the server; the PC only shows progress. So `status.json` (on the PC) every
  10 s while copying, any other state at once; the cancel file every 3 s; the console's progress bar (in the session,
  no round trip) at most once a second; flush and journal every 2 s while a file is copied, and before a file gets its final
  name; four chunk reads in flight, each on its own handle; the chunk hashes kept in a list that grows in place. A
  finished (or skipped) file is one line appended to `<id>.finished` next to the journal and read back when the helper
  starts, so it is never copied again or failed as "already exists" after the session ended right after its rename.
* Network share (`ResumableFileCopier`, `TransferQueue`): the next chunk is read while the current one is written and
  flushed; the journal of a running job is written at most once a second, any other change at once.
* The journal still records a chunk only after it was flushed; it may lag behind, which only means it claims fewer
  bytes, and resuming still checks the partial file's length and last chunk's hash. At most a couple of seconds of
  copying is redone after a crash.

* Chunk size: 4 MiB by default again (ADR-0042 had lowered it to 1 MiB when progress was saved after every chunk).
  Bigger chunks mean fewer round trips and less PowerShell work per byte; the cost of a drop is now bounded by the
  2 s checkpoint, plus at most the four chunks in flight. The File transfer window offers 1, 2, 4, 8 and 16 MiB
  (`TransferChunkSizeMiB`) with a tooltip on the trade-off; a journal or plan keeps the size it was started with.

## Consequences

* Far fewer round trips per megabyte over Remote Desktop; reading overlaps the latency of the shared drive.
* Copy and paste may stay somewhat faster: the shared drive is request/response by nature.
* A crash loses up to 2 s of progress instead of one chunk.
* Folders of very many small files are still bound by a few round trips per file (open, create, rename); copying files
  in parallel is the next step if that matters.
