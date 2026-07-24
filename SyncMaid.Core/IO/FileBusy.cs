namespace SyncMaid.Core.IO;

/// <summary>
/// Recognizes the I/O failures that mean "another process is holding this file right
/// now", as opposed to something being wrong with it. A busy file is not an error: the
/// engine defers it and the next run picks it up once the writer is done.
/// </summary>
public static class FileBusy
{
    // Win32 sharing/lock violations, surfaced by System.IO as an IOException whose
    // HResult carries the underlying error code in the FACILITY_WIN32 space.
    private const uint Win32FacilityMask = 0xFFFF0000;
    private const uint Win32Facility = 0x80070000;
    private const uint ErrorSharingViolation = 32;
    private const uint ErrorLockViolation = 33;

    /// <summary>
    /// True when <paramref name="exception"/> reports a file held open by another
    /// process (a sharing or lock violation), directly or as an inner exception.
    /// </summary>
    public static bool IsBusy(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException && IsSharingOrLockViolation(current.HResult))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSharingOrLockViolation(int hresult)
    {
        // Unsigned: an HResult with the high bit set is a negative int, and masking it as
        // one sign-extends the comparison into never matching.
        var value = unchecked((uint)hresult);
        return (value & Win32FacilityMask) == Win32Facility
               && (value & ~Win32FacilityMask) is ErrorSharingViolation or ErrorLockViolation;
    }
}
