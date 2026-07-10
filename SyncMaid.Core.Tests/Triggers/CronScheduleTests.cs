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

        var next = CronSchedule.NextOccurrenceUtc("0 0 * * *", after, TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_is_strictly_after_the_given_time()
    {
        var after = new DateTime(2026, 1, 1, 0, 7, 0, DateTimeKind.Utc);

        var next = CronSchedule.NextOccurrenceUtc("*/15 * * * *", after, TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_treats_unspecified_kind_as_utc()
    {
        var after = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var next = CronSchedule.NextOccurrenceUtc("*/15 * * * *", after, TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_interprets_cron_as_wall_clock_time_in_the_supplied_zone()
    {
        var utcPlusEight = TimeZoneInfo.CreateCustomTimeZone(
            "Test UTC+08", TimeSpan.FromHours(8), "Test UTC+08", "Test UTC+08");
        var after = new DateTime(2026, 1, 1, 16, 0, 0, DateTimeKind.Utc);

        var next = CronSchedule.NextOccurrenceUtc("0 2 * * *", after, utcPlusEight);

        Assert.Equal(new DateTime(2026, 1, 1, 18, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_defaults_to_the_machine_local_time_zone()
    {
        var after = new DateTime(2026, 1, 1, 16, 0, 0, DateTimeKind.Utc);

        var implicitLocal = CronSchedule.NextOccurrenceUtc("0 2 * * *", after);
        var explicitLocal = CronSchedule.NextOccurrenceUtc(
            "0 2 * * *", after, TimeZoneInfo.Local);

        Assert.Same(TimeZoneInfo.Local, CronSchedule.DefaultTimeZone);
        Assert.Equal(explicitLocal, implicitLocal);
    }

    [Fact]
    public void NextOccurrenceUtc_adjusts_a_missing_DST_wall_clock_occurrence()
    {
        var zone = CreatePacificTestZone();
        var beforeSpringForward = new DateTime(2026, 3, 8, 9, 0, 0, DateTimeKind.Utc);

        var next = CronSchedule.NextOccurrenceUtc("30 2 * * *", beforeSpringForward, zone);

        Assert.Equal(new DateTime(2026, 3, 8, 10, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrenceUtc_skips_months_without_the_requested_day()
    {
        var afterJanuaryOccurrence = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var next = CronSchedule.NextOccurrenceUtc("0 0 31 * *", afterJanuaryOccurrence, TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc), next);
    }

    private static TimeZoneInfo CreatePacificTestZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test Pacific",
            TimeSpan.FromHours(-8),
            "Test Pacific",
            "Test Pacific Standard",
            "Test Pacific Daylight",
            [rule]);
    }
}
