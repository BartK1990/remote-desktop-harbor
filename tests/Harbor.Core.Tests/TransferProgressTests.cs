using Harbor.Core.Transfer;

namespace Harbor.Core.Tests;

// SPEC FR-70: the progress of a transfer of several files as a whole, its average speed and time left.
public sealed class TransferProgressTests
{
    private static TransferJob Job(string name, long size, long done, TransferState state, bool skipped = false, long length = -1) => new()
    {
        SourcePath = name,
        DestinationPath = $@"\\srv\c$\in\Photos\{name}",
        BatchDestination = @"\\srv\c$\in\Photos",
        SizeHint = size,
        Length = length,
        BytesDone = done,
        State = state,
        Skipped = skipped,
    };

    [Fact]
    public void The_total_is_every_file_from_the_start_not_only_the_files_reached()
    {
        var progress = TransferBatchProgress.Of(
        [
            Job("a.jpg", 100, 100, TransferState.Completed, length: 100),
            Job(@"sub\b.jpg", 300, 50, TransferState.Running, length: 300),
            Job("c.jpg", 600, 0, TransferState.Queued),
        ]);

        Assert.Equal((1000L, 150L), (progress.BytesTotal, progress.BytesDone));
        Assert.Equal(0.15, progress.Progress, 3);
        Assert.Equal((TransferState.Running, 3, 1), (progress.State, progress.FilesTotal, progress.FilesDone));
        Assert.Equal(@"sub\b.jpg", progress.CurrentFile);
        Assert.Equal((50L, 300L), (progress.CurrentBytesDone, progress.CurrentBytes));
    }

    [Fact]
    public void Skipped_files_count_as_done_but_not_in_the_bytes()
    {
        var progress = TransferBatchProgress.Of(
        [
            Job("a.jpg", 100, 0, TransferState.Completed, skipped: true),
            Job("b.jpg", 300, 300, TransferState.Completed, length: 300),
        ]);

        Assert.Equal((TransferState.Completed, 2, 1), (progress.State, progress.FilesDone, progress.FilesSkipped));
        Assert.Equal((300L, 300L), (progress.BytesDone, progress.BytesTotal));
        Assert.Equal(1, progress.Progress);
        Assert.Null(progress.CurrentFile);
    }

    [Theory]
    [InlineData(TransferState.Retrying, TransferState.Paused, TransferState.Retrying)]
    [InlineData(TransferState.Paused, TransferState.Failed, TransferState.Paused)]
    [InlineData(TransferState.Failed, TransferState.Completed, TransferState.Failed)]
    [InlineData(TransferState.Queued, TransferState.Completed, TransferState.Queued)]
    public void The_batch_takes_the_most_urgent_state(TransferState one, TransferState other, TransferState expected) =>
        Assert.Equal(expected, TransferBatchProgress.Of([Job("a", 1, 0, one), Job("b", 1, 0, other)]).State);

    [Theory]
    [InlineData(@"\\srv\c$\in\Photos\sub\a.jpg", @"\\srv\c$\in\Photos", @"sub\a.jpg")]
    [InlineData(@"\\srv\c$\in\Photos\a.jpg", @"\\srv\c$\in\Photos\", "a.jpg")]
    [InlineData(@"C:\Other\a.jpg", @"\\srv\c$\in\Photos", "a.jpg")]
    [InlineData("/home/me/x/a.jpg", null, "a.jpg")]
    public void The_current_file_is_shown_relative_to_the_batch_folder(string path, string? folder, string expected) =>
        Assert.Equal(expected, TransferBatchProgress.Relative(path, folder));

    [Fact]
    public void The_average_speed_counts_only_time_spent_copying()
    {
        var rate = new TransferRate();
        var t = DateTimeOffset.UnixEpoch;
        rate.Sample(0, copying: true, t);
        Assert.Null(rate.BytesPerSecond); // too early for a figure
        rate.Sample(1000, copying: true, t.AddSeconds(1));
        Assert.Null(rate.BytesPerSecond);
        rate.Sample(2000, copying: false, t.AddSeconds(2)); // paused from here on
        Assert.Equal(1000, rate.BytesPerSecond);
        rate.Sample(2000, copying: true, t.AddSeconds(60)); // the pause does not count
        rate.Sample(1500, copying: true, t.AddSeconds(61)); // a chunk copied again after a dropped connection
        rate.Sample(4500, copying: true, t.AddSeconds(62));
        Assert.Equal(5000.0 / 4, rate.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(4), rate.TimeLeft(5000));
    }

    [Fact]
    public void Progress_text_names_the_share_the_speed_and_the_time_left()
    {
        var rate = new TransferRate();
        var t = DateTimeOffset.UnixEpoch;
        rate.Sample(0, true, t);
        rate.Sample(10 * 1024 * 1024, true, t.AddSeconds(10));
        var text = TransferText.Progress(10 * 1024 * 1024, 1024L * 1024 * 1024, rate, copying: true);
        Assert.Contains("10 MB of 1 GB (0%)", text.Replace(',', '.'), StringComparison.Ordinal);
        Assert.Contains("1 MB/s average", text, StringComparison.Ordinal);
        Assert.Contains("about 17 min left", text, StringComparison.Ordinal);
        Assert.DoesNotContain("left", TransferText.Progress(1, 2, rate, copying: false), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(30, "less than a minute left")]
    [InlineData(61, "about 2 min left")]
    [InlineData(7800, "about 2 h 10 min left")]
    public void Time_left_is_rounded_for_reading(int seconds, string expected) =>
        Assert.Equal(expected, TransferText.TimeLeft(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0.4, "less than a second")]
    [InlineData(45, "45 s")]
    [InlineData(750, "12 min 30 s")]
    [InlineData(720, "12 min")]
    [InlineData(7500, "2 h 5 min")]
    public void Durations_are_short_and_readable(double seconds, string expected) =>
        Assert.Equal(expected, TransferText.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_finished_transfer_says_how_long_it_took_and_its_average_speed()
    {
        Assert.Equal("10 MB in 10 s (1 MB/s average)", TransferText.Finished(10 * 1024 * 1024, TimeSpan.FromSeconds(10)).Replace(',', '.'));
        Assert.Equal("10 MB", TransferText.Finished(10 * 1024 * 1024, null));
        Assert.Contains("1 min 5 s elapsed", TransferText.Progress(1, 2, null, copying: true, TimeSpan.FromSeconds(65)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_batch_took_from_its_first_start_to_its_last_end()
    {
        var t = DateTimeOffset.UnixEpoch;
        var a = Job("a", 1, 1, TransferState.Completed, length: 1);
        a.StartedUtc = t;
        a.CompletedUtc = t.AddMinutes(1);
        var b = Job("b", 1, 1, TransferState.Completed, length: 1);
        b.StartedUtc = t.AddMinutes(1);
        b.CompletedUtc = t.AddMinutes(3);
        Assert.Equal(TimeSpan.FromMinutes(3), TransferBatchProgress.Of([a, b]).Took);

        var c = Job("c", 1, 0, TransferState.Queued);
        var running = TransferBatchProgress.Of([a, c]);
        Assert.Equal(t, running.StartedUtc);
        Assert.Null(running.Took); // not done yet
    }
}
