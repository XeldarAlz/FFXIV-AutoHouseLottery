using System.IO;
using System.Text;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Storage;

namespace AutoHouseLottery.Tests;

public sealed class QueuedFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), string.Concat("ahl-tests-", Guid.NewGuid().ToString("N")));
    private readonly List<string> reports = [];

    public QueuedFileTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void AMissingFileIsMissingAndAWrittenOneReadsBack()
    {
        var file = Open("saved.json");

        Assert.Equal(FileReadStatus.Missing, file.Read().Status);
        file.Write(static () => Encoding.UTF8.GetBytes("{}"));
        file.Close();

        var read = file.Read();
        Assert.Equal(FileReadStatus.Read, read.Status);
        Assert.Equal("{}", Encoding.UTF8.GetString(read.Bytes!));
    }

    [Fact]
    public void AFileThatCannotBeReadIsAFailureNotAMissingFile()
    {
        var file = Open("locked.json");
        File.WriteAllText(Path.Combine(directory, "locked.json"), "{}");

        FileRead read;
        using (Lock("locked.json"))
        {
            read = file.Read();
        }

        Assert.Equal(FileReadStatus.Failed, read.Status);
        Assert.Null(read.Bytes);
        Assert.Single(reports);
    }

    [Fact]
    public async Task ALockedFileIsReadOnceItIsReleased()
    {
        var file = Open("busy.json");
        File.WriteAllText(Path.Combine(directory, "busy.json"), "{}");

        Task<FileRead> reading;
        using (Lock("busy.json"))
        {
            reading = file.ReadWhenReadableAsync();
            await Task.Delay(100);
            Assert.False(reading.IsCompleted);
        }

        var read = await reading.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(FileReadStatus.Read, read.Status);
    }

    [Fact]
    public async Task ReadingUntilReadableStopsWithAFailureWhenTheFileIsClosed()
    {
        var file = Open("closed.json");
        File.WriteAllText(Path.Combine(directory, "closed.json"), "{}");

        using (Lock("closed.json"))
        {
            var reading = file.ReadWhenReadableAsync();
            await Task.Delay(100);
            file.Close();
            var read = await reading.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(FileReadStatus.Failed, read.Status);
        }
    }

    [Fact]
    public void ADamagedFileIsKeptBeforeItIsReplaced()
    {
        var file = Open("saved.json");
        File.WriteAllText(Path.Combine(directory, "saved.json"), "{broken");

        file.BackUp(new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc));
        file.Write(static () => Encoding.UTF8.GetBytes("{}"));
        file.Close();

        Assert.Equal("{broken", File.ReadAllText(Path.Combine(directory, "saved.json.unreadable-20261014-120000")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(directory, "saved.json")));
    }

    private FileStream Lock(string name) => new(Path.Combine(directory, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private QueuedFile Open(string name) => new(Path.Combine(directory, name), name, (_, message) => reports.Add(message));
}
