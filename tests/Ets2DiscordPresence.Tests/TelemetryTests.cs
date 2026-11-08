using System.Text;
using Ets2DiscordPresence;
using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void PreparesGameFromBundledReaderAndLeavesExistingFilesAlone()
    {
        var game = Directory.CreateTempSubdirectory("ets2-presence-test-");
        try
        {
            var binary = Directory.CreateDirectory(Path.Combine(game.FullName, "bin", "win_x64"));
            File.WriteAllBytes(Path.Combine(binary.FullName, "eurotrucks2.exe"), []);
            Assert.True(PluginInstaller.Prepare(game.FullName));
            Assert.False(PluginInstaller.Prepare(game.FullName));
            var plugin = Assert.Single(Directory.GetFiles(Path.Combine(binary.FullName, "plugins")));
            Assert.True(new FileInfo(plugin).Length > 0);
            File.WriteAllText(plugin, "existing file");
            Assert.Throws<IOException>(() => PluginInstaller.Prepare(game.FullName));
            Assert.Equal("existing file", File.ReadAllText(plugin));
        }
        finally
        {
            game.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadsDeliveryFromPinnedPluginLayout()
    {
        var bytes = new byte[4400];
        bytes[0] = 1;
        BitConverter.GetBytes((uint)12).CopyTo(bytes, 40);
        BitConverter.GetBytes((uint)1).CopyTo(bytes, 52);
        BitConverter.GetBytes(1234.5).CopyTo(bytes, 2200);
        BitConverter.GetBytes(-9876.5).CopyTo(bytes, 2216);
        BitConverter.GetBytes(421500f).CopyTo(bytes, 1060);
        bytes[4300] = 1;
        Encoding.UTF8.GetBytes("Maquinaria").CopyTo(bytes, 2620);
        Encoding.UTF8.GetBytes("Madrid").CopyTo(bytes, 3004);
        Encoding.UTF8.GetBytes("Paris").CopyTo(bytes, 2748);

        var snapshot = TelemetryReader.Parse(bytes);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.OnJob);
        Assert.Equal("Maquinaria", snapshot.Cargo);
        Assert.Equal("Madrid", snapshot.SourceCity);
        Assert.Equal("Paris", snapshot.DestinationCity);
        Assert.Equal(421500f, snapshot.RouteDistanceMeters);
        Assert.Equal(1234.5, snapshot.X);
    }

    [Fact]
    public void FormatsDeliveryAndFreeRoam()
    {
        var delivery = new TelemetrySnapshot(false, true, "Maquinaria", "Madrid", "Paris", 0, 0, 421500f, 12);
        var roaming = delivery with { OnJob = false };
        Assert.Contains("Maquinaria", PresenceFormatter.Format(delivery, "Lyon").Details);
        Assert.Contains("Lyon", PresenceFormatter.Format(delivery, "Lyon").State);
        Assert.Contains("422 km", PresenceFormatter.Format(delivery, null).State);
        Assert.Equal("Explorando libremente", PresenceFormatter.Format(roaming, null).Details);
    }

    [Fact]
    public void RejectsUnknownPluginLayout()
    {
        var bytes = new byte[4400];
        bytes[0] = 1;
        BitConverter.GetBytes((uint)99).CopyTo(bytes, 40);
        Assert.Throws<InvalidDataException>(() => TelemetryReader.Parse(bytes));
    }
}
