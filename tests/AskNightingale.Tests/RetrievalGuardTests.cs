using System.Globalization;
using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Rag;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

public class RetrievalGuardTests
{
    [Fact]
    public void Default_threshold_is_0_3_when_config_unset()
    {
        MakeSut().MinScore.ShouldBe(0.3f);
    }

    [Fact]
    public void Threshold_reads_from_config()
    {
        MakeSut(threshold: 0.5f).MinScore.ShouldBe(0.5f);
    }

    [Fact]
    public void Empty_results_should_refuse()
    {
        MakeSut().ShouldRefuse([]).ShouldBeTrue();
    }

    [Fact]
    public void All_scores_below_threshold_should_refuse()
    {
        var sut = MakeSut(threshold: 0.3f);

        sut.ShouldRefuse([
            new RetrievalResult(MakeChunk(), 0.1f),
            new RetrievalResult(MakeChunk(), 0.2f)
        ]).ShouldBeTrue();
    }

    [Fact]
    public void Score_at_threshold_should_allow()
    {
        var sut = MakeSut(threshold: 0.3f);

        sut.ShouldRefuse([
            new RetrievalResult(MakeChunk(), 0.1f),
            new RetrievalResult(MakeChunk(), 0.3f)
        ]).ShouldBeFalse();
    }

    [Fact]
    public void Score_above_threshold_should_allow()
    {
        var sut = MakeSut(threshold: 0.3f);

        sut.ShouldRefuse([
            new RetrievalResult(MakeChunk(), 0.5f)
        ]).ShouldBeFalse();
    }

    private static RetrievalGuard MakeSut(float? threshold = null)
    {
        var settings = new Dictionary<string, string?>();
        if (threshold.HasValue)
        {
            settings["RAG_MIN_SCORE"] = threshold.Value.ToString(CultureInfo.InvariantCulture);
        }
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new RetrievalGuard(config);
    }

    private static Chunk MakeChunk() => new(0, "x", 0);
}
