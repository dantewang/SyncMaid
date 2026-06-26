using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class CronScheduleTests
{
    [Theory]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 0 * * *", true)]
    [InlineData("not a cron", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValid_reflects_cron_validity(string expression, bool expected)
    {
        Assert.Equal(expected, CronSchedule.IsValid(expression));
    }

    [Fact]
    public void NextOccurrenceUtc_finds_the_next_daily_midnight()
    {
        var after = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var next = CronSchedule.NextOccurrenceUtc("0 0 * * *", after);

        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_is_strictly_after_the_given_time()
    {
        var after = new DateTime(2026, 1, 1, 0, 7, 0, DateTimeKind.Utc);

        var next = CronSchedule.NextOccurrenceUtc("*/15 * * * *", after);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_treats_unspecified_kind_as_utc()
    {
        var after = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var next = CronSchedule.NextOccurrenceUtc("*/15 * * * *", after);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc), next);
    }
}
