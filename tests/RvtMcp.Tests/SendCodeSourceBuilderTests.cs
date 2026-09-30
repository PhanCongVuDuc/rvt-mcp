using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class SendCodeSourceBuilderTests
    {
        [Theory]
        [InlineData("return 14;", 14)]
        [InlineData("// helper class example\nreturn 14;", 14)]
        [InlineData("return \"a class marker\";", "a class marker")]
        [InlineData("return new Helper().Value;\npublic class Helper { public int Value => 14; }", 14)]
        [InlineData("var h = new Helper();\npublic class Helper { public int Value => 14; }\nreturn h.Value;", 14)]
        [InlineData("public class Helper { public int Value => 14; }\nreturn new Helper().Value;", 14)]
        [InlineData("using X = System.Math;\nreturn X.Abs(-14);", 14)]
        [InlineData("int Value() { return 14; }\nreturn Value();", 14)]
        [InlineData("return (int)Value.Good;\npublic enum Value { Good = 14 }", 14)]
        public void CompilesAndRunsBodyWithOptionalHelperTypes(string code, object expected)
        {
            Assert.Equal(expected, CompileAndRun(SendCodeSourceBuilder.Build(code)));
        }

        [Fact]
        public void PreservesExplicitEntrypointAndHelperClass()
        {
            const string code = @"using Autodesk.Revit.UI;
public class McpDynamicScript { public static object Run(UIApplication app) { return new Helper().Value; } }
public class Helper { public int Value => 14; }";
            Assert.Equal(code, SendCodeSourceBuilder.Build(code));
            Assert.Equal(14, CompileAndRun(SendCodeSourceBuilder.Build(code)));
        }

        [Fact]
        public void RunsBodyWhenNoDocumentIsOpen()
        {
            // With no model open ActiveUIDocument is null; the preamble must not throw before the body
            // runs, or no snippet can call app.OpenAndActivateDocument to open one.
            Assert.Equal(true, CompileAndRun(SendCodeSourceBuilder.Build("return doc == null && uidoc == null;"), NoDocumentApi));
        }

        [Theory]
        [InlineData("var live = 14;\nreturn live;")]
        [InlineData("foreach (var live in new[] { 14 }) return live;\nreturn 0;")]
        [InlineData("return new[] { 14 }.Select(live => live).First();")]
        public void Build_BodyDeclaresItsOwnLive_RunsAsBefore(string code)
        {
            var ran = CompileAndRun(SendCodeSourceBuilder.Build(code));

            Assert.True(Equals(ran, 14), "a snippet naming its own 'live' ran before the live helper existed and must still run (FR-007), got " + ran);
        }

        [Fact]
        public void PreservesCompilerErrorsForInvalidBody()
        {
            Assert.Contains(Compile(SendCodeSourceBuilder.Build("return missingName; ")).GetDiagnostics(),
                d => d.Id == "CS0103");
        }

        // Only stand in for UIApplication/Document and the plugin's live helper, which the wrapper names and
        // Revit always has loaded; use the real Roslyn compiler and entrypoint.
        private const string PluginApi = @"
namespace RvtMcp.Plugin { public static class LiveAssembly { public static System.Reflection.Assembly Of(string f) { return null; } } }";

        private const string OpenDocumentApi = PluginApi + @"namespace Autodesk.Revit.DB { public class Document {} }
namespace Autodesk.Revit.UI {
 public class UIApplication { public UIDocument ActiveUIDocument { get; } = new UIDocument(); }
 public class UIDocument { public Autodesk.Revit.DB.Document Document { get; } = new Autodesk.Revit.DB.Document(); }
}";

        private const string NoDocumentApi = PluginApi + @"namespace Autodesk.Revit.DB { public class Document {} }
namespace Autodesk.Revit.UI {
 public class UIApplication { public UIDocument ActiveUIDocument { get; } = null; }
 public class UIDocument { public Autodesk.Revit.DB.Document Document { get; } = new Autodesk.Revit.DB.Document(); }
}";

        private static CSharpCompilation Compile(string source, string api = OpenDocumentApi)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
            return CSharpCompilation.Create("SendCodeTest_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(api) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static object CompileAndRun(string source, string api = OpenDocumentApi)
        {
            using var bytes = new MemoryStream();
            var result = Compile(source, api).Emit(bytes);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            var assembly = Assembly.Load(bytes.ToArray());
            var app = Activator.CreateInstance(assembly.GetType("Autodesk.Revit.UI.UIApplication"));
            return assembly.GetType("McpDynamicScript").GetMethod("Run").Invoke(null, new[] { app });
        }
    }
}
