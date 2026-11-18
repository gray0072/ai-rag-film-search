using FilmSearch.Evals.Reports;

namespace FilmSearch.UnitTests.Evals;

public sealed class RunHeaderTests
{
    [Fact]
    public void The_connection_string_does_not_reach_the_committed_report()
    {
        var line = RunHeader.RedactedCommandLine(["--suite", "search", "--connection", "Host=x;Password=secret", "--label", "a"]);

        line.ShouldBe("--suite search --connection <connection> --label a");
    }
}
