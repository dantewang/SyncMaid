using Cronos;

namespace SyncMaid.Core.Triggers;

/// <summary>
/// Pure helpers over a cron expression — parsing and next-occurrence computation —
/// kept separate from the live <see cref="ScheduledTriggerSource"/> timer so the
/// scheduling logic can be unit-tested deterministically.
/// </summary>
public static class CronSchedule
{
    /// <summary>Returns <c>true</c> if <paramref name="cronExpression"/> is a valid 5-field cron expression.</summary>
    public static bool IsValid(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return false;
        }

        try
        {
            CronExpression.Parse(cronExpression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the next time the expression fires strictly after <paramref name="afterUtc"/>,
    /// in UTC. Returns <c>null</c> when the expression has no future occurrence.
    /// </summary>
    /// <exception cref="CronFormatException">The expression is not valid.</exception>
    public static DateTime? NextOccurrenceUtc(string cronExpression, DateTime afterUtc)
    {
        var from = afterUtc.Kind == DateTimeKind.Utc
            ? afterUtc
            : DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);

        return CronExpression.Parse(cronExpression).GetNextOccurrence(from);
    }
}
