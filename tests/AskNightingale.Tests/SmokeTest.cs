using Shouldly;

namespace AskNightingale.Tests;

public class SmokeTest
{
    [Fact]
    public void TestProjectIsWired()
    {
        true.ShouldBeTrue();
    }
}
