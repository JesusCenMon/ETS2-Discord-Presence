using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData(true, 1000, 5000, false, "Reproduciendo")]
    [InlineData(false, 1000, 5000, false, "En pausa")]
    [InlineData(false, 5000, 5000, false, "Terminada")]
    [InlineData(true, 1000, 5000, true, "Playing")]
    [InlineData(false, 1000, 5000, true, "Paused")]
    [InlineData(false, 5000, 5000, true, "Finished")]
    public void ReplayStatesDoNotConfusePauseWithCompletion(bool playing, long elapsed, long duration, bool english, string expected) =>
        Assert.Equal(expected, PresentationState.ReplayStatus(playing, elapsed, duration, english));

    [Fact]
    public void StaleDataIsLabelledWithoutCallingCurrentDataOld()
    {
        Assert.Null(PresentationState.Freshness(false, true, 60_000, true));
        Assert.Null(PresentationState.Freshness(true, true, 2000, true));
        Assert.Equal("Últimos datos recibidos hace 12 s", PresentationState.Freshness(true, true, 12_000, false));
        Assert.Equal("Waiting for game data", PresentationState.Freshness(true, false, 0, true));
    }
}
