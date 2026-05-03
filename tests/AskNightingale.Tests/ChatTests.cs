using AskNightingale.Components.Pages;
using AskNightingale.Services;
using Bunit;
using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AskNightingale.Tests;

public class ChatTests : BunitContext
{
    [Fact]
    public void Send_button_is_disabled_when_input_is_empty()
    {
        Services.AddSingleton(A.Fake<IChatService>());

        var cut = Render<Chat>();

        cut.Find("[data-testid=chat-send]")
           .HasAttribute("disabled")
           .ShouldBeTrue();
    }

    [Fact]
    public void Sending_a_message_renders_user_then_assistant_reply()
    {
        var chat = A.Fake<IChatService>();
        A.CallTo(() => chat.RespondAsync("Hello", A<CancellationToken>._))
            .Returns(new ChatResponse("echo: Hello", []));
        Services.AddSingleton(chat);

        var cut = Render<Chat>();

        cut.Find("[data-testid=chat-input]").Input("Hello");
        cut.Find("[data-testid=chat-send]").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=chat-message]").Count.ShouldBe(2));

        var messages = cut.FindAll("[data-testid=chat-message]");
        messages[0].TextContent.ShouldContain("Hello");
        messages[1].TextContent.ShouldContain("echo: Hello");
    }

    [Fact]
    public void Empty_input_does_not_call_chat_service()
    {
        var chat = A.Fake<IChatService>();
        Services.AddSingleton(chat);

        var cut = Render<Chat>();

        // Button is disabled, but force-click anyway to assert defensive guard.
        cut.Find("[data-testid=chat-send]").Click();

        A.CallTo(() => chat.RespondAsync(A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public void Citations_render_below_assistant_message_when_provided()
    {
        var chat = A.Fake<IChatService>();
        A.CallTo(() => chat.RespondAsync(A<string>._, A<CancellationToken>._))
            .Returns(new ChatResponse("answer", [
                new Citation(7, "fresh air is essential", 0.9f),
                new Citation(12, "noise harms recovery", 0.7f)
            ]));
        Services.AddSingleton(chat);

        var cut = Render<Chat>();
        cut.Find("[data-testid=chat-input]").Input("anything");
        cut.Find("[data-testid=chat-send]").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=citation]").Count.ShouldBe(2));

        var citations = cut.FindAll("[data-testid=citation]");
        citations[0].TextContent.ShouldContain("§7");
        citations[0].TextContent.ShouldContain("fresh air is essential");
        citations[1].TextContent.ShouldContain("§12");
    }

    [Fact]
    public void No_citation_block_when_citations_empty()
    {
        var chat = A.Fake<IChatService>();
        A.CallTo(() => chat.RespondAsync(A<string>._, A<CancellationToken>._))
            .Returns(new ChatResponse("answer", []));
        Services.AddSingleton(chat);

        var cut = Render<Chat>();
        cut.Find("[data-testid=chat-input]").Input("anything");
        cut.Find("[data-testid=chat-send]").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=chat-message]").Count.ShouldBe(2));

        cut.FindAll("[data-testid=chat-citations]").ShouldBeEmpty();
    }
}
