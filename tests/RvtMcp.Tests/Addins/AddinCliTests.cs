using Newtonsoft.Json.Linq;
using RvtMcp.Server;
using Xunit;

namespace RvtMcp.Tests.Addins
{
    /// <summary>`rvt-mcp addin list|call`: argument parsing and exit codes (0 ok, 1 command error, 2 usage/connection).</summary>
    public sealed class AddinCliTests
    {
        [Fact]
        public void List_parses_with_no_target()
        {
            Assert.True(AddinCli.TryParse(new[] { "list" }, out var request, out _));
            Assert.Equal(AddinCliVerb.List, request.Verb);
            Assert.Null(request.Target);
        }

        [Fact]
        public void Call_parses_addin_command_args_target_read_only_and_timeout()
        {
            Assert.True(AddinCli.TryParse(
                new[] { "call", "Sonny.Revit", "walls.count", "--args", "{\"level\":\"Tầng 1\"}", "--target", "pid:123", "--read-only", "--timeout", "30" },
                out var request, out var error), error);

            Assert.Equal(AddinCliVerb.Call, request.Verb);
            Assert.Equal("Sonny.Revit", request.Addin);
            Assert.Equal("walls.count", request.Command);
            Assert.Equal("Tầng 1", request.Args.Value<string>("level"));
            Assert.Equal("pid:123", request.Target);
            Assert.True(request.ReadOnly);
            Assert.Equal(30, request.TimeoutSeconds);
        }

        [Fact]
        public void Call_without_args_sends_an_empty_object()
        {
            Assert.True(AddinCli.TryParse(new[] { "call", "Sonny.Revit", "app.ping" }, out var request, out _));
            Assert.Equal(new JObject(), request.Args);
        }

        public static TheoryData<string[]> BadArguments() => new TheoryData<string[]>
        {
            new string[0],
            new[] { "nope" },
            new[] { "call" },
            new[] { "call", "Sonny.Revit" },
            new[] { "call", "Sonny.Revit", "walls.count", "--args", "{not json" },
            new[] { "call", "Sonny.Revit", "walls.count", "--args", "[1,2]" },
            new[] { "call", "Sonny.Revit", "walls.count", "--args" },
            new[] { "call", "Sonny.Revit", "walls.count", "--timeout", "abc" },
            new[] { "list", "--target" },
            new[] { "list", "--bogus" },
        };

        [Theory]
        [MemberData(nameof(BadArguments))]
        public void Bad_arguments_are_usage_errors_with_exit_code_2(string[] args)
        {
            Assert.False(AddinCli.TryParse(args, out _, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(2, AddinCli.UsageExitCode);
        }

        [Fact]
        public void An_ok_envelope_exits_0_and_a_refusal_exits_1()
        {
            Assert.Equal(0, AddinCli.ExitCodeFor(JObject.Parse("{\"ok\":true,\"result\":{\"count\":12}}")));
            Assert.Equal(1, AddinCli.ExitCodeFor(JObject.Parse("{\"ok\":false,\"error\":{\"code\":\"unknown_command\"}}")));
            Assert.Equal(1, AddinCli.ExitCodeFor(new JObject()));
        }
    }
}
