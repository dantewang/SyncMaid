using SyncMaid.Core.Sync;

namespace SyncMaid.Core.Tests.Sync;

public class TransientRetryTests
{
    [Fact]
    public void Succeeds_on_the_first_attempt_without_retrying()
    {
        var attempts = 0;
        var retries = 0;

        TransientRetry.Execute(() => attempts++, maxAttempts: 3, _ => retries++);

        Assert.Equal(1, attempts);
        Assert.Equal(0, retries);
    }

    [Fact]
    public void Retries_a_transient_failure_then_succeeds()
    {
        var attempts = 0;

        TransientRetry.Execute(
            () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new IOException("locked");
                }
            },
            maxAttempts: 3);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Rethrows_after_exhausting_the_attempts()
    {
        var attempts = 0;

        Assert.Throws<IOException>(() =>
            TransientRetry.Execute(
                () =>
                {
                    attempts++;
                    throw new IOException("still locked");
                },
                maxAttempts: 3));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Does_not_retry_a_non_transient_failure()
    {
        var attempts = 0;

        Assert.Throws<SyncVerificationException>(() =>
            TransientRetry.Execute(
                () =>
                {
                    attempts++;
                    throw new SyncVerificationException("corrupt");
                },
                maxAttempts: 3));

        Assert.Equal(1, attempts); // verification failures are deterministic — fail fast
    }

    [Fact]
    public void Does_not_retry_a_vanished_source_file()
    {
        var attempts = 0;

        Assert.Throws<FileNotFoundException>(() =>
            TransientRetry.Execute(
                () =>
                {
                    attempts++;
                    throw new FileNotFoundException("vanished after planning");
                },
                maxAttempts: 3));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Does_not_retry_a_vanished_source_directory()
    {
        var attempts = 0;

        Assert.Throws<DirectoryNotFoundException>(() =>
            TransientRetry.Execute(
                () =>
                {
                    attempts++;
                    throw new DirectoryNotFoundException("source directory vanished after planning");
                },
                maxAttempts: 3));

        Assert.Equal(1, attempts);
    }
}
