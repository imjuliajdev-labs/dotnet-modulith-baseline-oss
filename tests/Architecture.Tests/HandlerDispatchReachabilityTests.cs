using Architecture.Tests.Support;

namespace Architecture.Tests;

/// <summary>
/// Unit tests for the BP-031 dispatch reachability analyzer. These guard the
/// behavior of <see cref="HandlerDispatchReachability"/> in isolation so a
/// regression in the analyzer is caught before the architecture-level
/// coverage Fact runs over real source.
/// </summary>
public sealed class HandlerDispatchReachabilityTests
{
    private static readonly HandlerDispatchReachability.HandlerSourceInfo SampleHandler =
        new("EnableModuleCommandHandler", "EnableModuleCommand", "Platform.Application.ModuleState", "ignored.cs");

    [Fact]
    public void StripCommentsAndStringLiterals_removes_line_and_block_comments()
    {
        const string source = "var x = 1; // EnableModuleCommand mention\n/* EnableModuleCommand */\nvar y = 2;";

        var cleaned = HandlerDispatchReachability.StripCommentsAndStringLiterals(source);

        Assert.False(HandlerDispatchReachability.ContainsWholeWord(cleaned, "EnableModuleCommand"));
        Assert.True(HandlerDispatchReachability.ContainsWholeWord(cleaned, "var"));
    }

    [Fact]
    public void StripCommentsAndStringLiterals_removes_string_literal_contents()
    {
        const string source = "var x = \"EnableModuleCommand\";";

        var cleaned = HandlerDispatchReachability.StripCommentsAndStringLiterals(source);

        Assert.False(HandlerDispatchReachability.ContainsWholeWord(cleaned, "EnableModuleCommand"));
    }

    [Fact]
    public void StripCommentsAndStringLiterals_removes_verbatim_string_contents()
    {
        const string source = "var x = @\"EnableModuleCommand and more\";";

        var cleaned = HandlerDispatchReachability.StripCommentsAndStringLiterals(source);

        Assert.False(HandlerDispatchReachability.ContainsWholeWord(cleaned, "EnableModuleCommand"));
    }

    [Fact]
    public void ContainsWholeWord_does_not_match_substring_inside_other_identifier()
    {
        const string text = "var BiggerThanEnableModuleCommandX = 1;";

        Assert.False(HandlerDispatchReachability.ContainsWholeWord(text, "EnableModuleCommand"));
    }

    [Fact]
    public void ContainsWholeWord_matches_token_at_word_boundaries()
    {
        const string text = "new EnableModuleCommand(\"reports\");";

        Assert.True(HandlerDispatchReachability.ContainsWholeWord(text, "EnableModuleCommand"));
    }

    [Fact]
    public void IsHandlerReachable_detects_direct_message_construction()
    {
        const string source = """
            using SomethingElse;

            public class FakeTests
            {
                public void Run() { var cmd = new EnableModuleCommand(); }
            }
            """;

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.True(HandlerDispatchReachability.IsHandlerReachable(fingerprint, SampleHandler));
    }

    [Fact]
    public void IsHandlerReachable_detects_handler_class_reference()
    {
        const string source = """
            public class FakeTests
            {
                public void Run() { var t = typeof(EnableModuleCommandHandler); }
            }
            """;

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.True(HandlerDispatchReachability.IsHandlerReachable(fingerprint, SampleHandler));
    }

    [Fact]
    public void IsHandlerReachable_detects_namespace_using_directive()
    {
        const string source = """
            using Platform.Application.ModuleState;

            public class FakeTests
            {
                public void Run() { /* dispatches via HTTP */ }
            }
            """;

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.True(HandlerDispatchReachability.IsHandlerReachable(fingerprint, SampleHandler));
    }

    [Fact]
    public void IsHandlerReachable_rejects_name_only_mention_in_comment()
    {
        const string source = """
            using SomethingUnrelated;

            public class FakeTests
            {
                // EnableModuleCommand is exercised somewhere else, supposedly.
                public void Run() { /* no dispatch */ }
            }
            """;

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.False(HandlerDispatchReachability.IsHandlerReachable(fingerprint, SampleHandler));
    }

    [Fact]
    public void IsHandlerReachable_rejects_name_only_mention_in_string_literal()
    {
        const string source = """
            using SomethingUnrelated;

            public class FakeTests
            {
                public string Note => "EnableModuleCommand and EnableModuleCommandHandler";
            }
            """;

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.False(HandlerDispatchReachability.IsHandlerReachable(fingerprint, SampleHandler));
    }

    [Fact]
    public void IsHandlerReachable_rejects_unrelated_namespace_using()
    {
        const string source = """
            using Platform.Application.OtherFeature;

            public class FakeTests
            {
                public void Run() { }
            }
            """;

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.False(HandlerDispatchReachability.IsHandlerReachable(fingerprint, SampleHandler));
    }

    [Fact]
    public void Fingerprint_extracts_static_using_directives()
    {
        const string source = "using static Foo.Bar.Baz;\nusing Platform.Application.ModuleState;\n";

        var fingerprint = HandlerDispatchReachability.Fingerprint("FakeTests.cs", source);

        Assert.Contains("Foo.Bar.Baz", fingerprint.UsingNamespaces);
        Assert.Contains("Platform.Application.ModuleState", fingerprint.UsingNamespaces);
    }

    [Fact]
    public void BuildHandlerSourceMap_extracts_handler_with_message_type_and_namespace()
    {
        const string sample = """
            namespace Sample.Application.Things;

            internal sealed class DoThingCommandHandler
                : ICommandHandler<DoThingCommand, ThingResult>
            {
                public Task HandleAsync(DoThingCommand command) => Task.CompletedTask;
            }
            """;

        var map = HandlerDispatchReachability.BuildHandlerSourceMap(
            new[] { "src/Modules/Sample/Sample.Application/Things/DoThingCommandHandler.cs" },
            _ => sample);

        Assert.True(map.ContainsKey("DoThingCommandHandler"));
        var info = map["DoThingCommandHandler"];
        Assert.Equal("DoThingCommand", info.MessageType);
        Assert.Equal("Sample.Application.Things", info.Namespace);
    }
}
