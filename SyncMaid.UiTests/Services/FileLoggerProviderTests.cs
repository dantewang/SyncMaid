using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Services;

public class FileLoggerProviderTests
{
    private static string TempLogPath() =>
        Path.Combine(Path.GetTempPath(), "syncmaid-tests", Guid.NewGuid().ToString("N"), "syncmaid.log");

    [Fact]
    public void Writes_a_formatted_entry_with_the_exception()
    {
        var path = TempLogPath();
        try
        {
            using var provider = new FileLoggerProvider(path, LogLevel.Information);
            provider.CreateLogger("SyncMaid.ViewModels.TaskNodeViewModel")
                .LogError(new InvalidOperationException("boom"), "sync failed for {Task}", "Photos");

            var text = File.ReadAllText(path);
            Assert.Contains("[ERR]", text);
            Assert.Contains("TaskNodeViewModel:", text);          // shortened category
            Assert.Contains("sync failed for Photos", text);      // formatted message
            Assert.Contains("InvalidOperationException", text);   // exception appended
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Skips_entries_below_the_minimum_level()
    {
        var path = TempLogPath();
        try
        {
            using var provider = new FileLoggerProvider(path, LogLevel.Warning);
            provider.CreateLogger("cat").LogInformation("chatter"); // below Warning

            Assert.False(File.Exists(path)); // nothing written
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static void Cleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
