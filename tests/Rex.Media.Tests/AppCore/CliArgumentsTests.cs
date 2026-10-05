using Rex.Media.AppCore.Cli;
using Rex.Media.AppCore.Machine;

namespace Rex.Media.Tests.AppCore;

public sealed class CliArgumentsTests
{
    [Fact]
    public void CommandPositionalsAndOptionsAreSeparated()
    {
        var arguments = CliArguments.Parse(["play", "song.wav", "--volume", "50", "--aout=wav:x.wav", "--json"]);

        Assert.Equal("play", arguments.Command);
        Assert.Equal(["song.wav"], arguments.Positional);
        Assert.Equal("50", arguments.Option("--volume"));
        Assert.Equal("wav:x.wav", arguments.Option("--aout"));
        Assert.True(arguments.Json);
        Assert.Null(arguments.Option("--start"));
        Assert.Equal(3, arguments.OptionNames.Count());
    }

    [Fact]
    public void JsonCanComeFirstAndTakesNoValue()
    {
        var arguments = CliArguments.Parse(["--json", "probe", "x.wav"]);

        Assert.Equal("probe", arguments.Command);
        Assert.Equal(["x.wav"], arguments.Positional);
        Assert.True(arguments.Json);
    }

    [Fact]
    public void AgentCommandsAreTwoWords()
    {
        Assert.Equal("agent capabilities", CliArguments.Parse(["agent", "capabilities"]).Command);
        Assert.Equal("agent", CliArguments.Parse(["agent"]).Command);
    }

    [Fact]
    public void ATrailingOptionWithoutAValueIsAFlag()
    {
        var arguments = CliArguments.Parse(["play", "--stop"]);

        Assert.Equal(string.Empty, arguments.Option("--stop"));
        Assert.Equal(string.Empty, CliArguments.Parse([]).Command);
        Assert.Throws<ArgumentNullException>(() => CliArguments.Parse(null!));
    }

    [Fact]
    public void TheReferenceFindsCommandsByName()
    {
        Assert.Equal("probe", CliReference.Find("probe")!.Name);
        Assert.Null(CliReference.Find("dance"));
        Assert.All(CliReference.Commands, command => Assert.StartsWith("rexplay " + command.Name, command.Usage, StringComparison.Ordinal));
        Assert.All(CliReference.Options, option => Assert.StartsWith("--", option.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void FailureEnvelopesCarryTheErrorTypeAndMessage()
    {
        var json = MachineEnvelope.Failure("play", "IOException", "The disk went away.");

        Assert.Equal("{\"ok\":false,\"protocolVersion\":1,\"command\":\"play\",\"error\":{\"type\":\"IOException\",\"message\":\"The disk went away.\"}}", json);
    }

    [Fact]
    public void CliExceptionsCarryTheirMessageAndCause()
    {
        var inner = new IOException("x");

        Assert.Equal("m", new CliException("m").Message);
        Assert.Same(inner, new CliException("m", inner).InnerException);
        Assert.NotNull(new CliException().Message);
    }
}
