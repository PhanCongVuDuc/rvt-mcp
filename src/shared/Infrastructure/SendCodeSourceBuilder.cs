using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RvtMcp.Plugin
{
    public static class SendCodeSourceBuilder
    {
        public static string Build(string code)
        {
            var root = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();
            var statements = root.Members.OfType<GlobalStatementSyntax>().ToArray();
            // A compilation unit with declarations only is the existing full-source mode.
            if (statements.Length == 0 && root.Members.Count != 0) return code;

            var body = string.Concat(statements.Select(s => s.ToFullString()));

            // An add-in assembly asked for by name, through that add-in's own live reload when it has one. Reaching into
            // AppDomain instead hands back the build Revit opened with, which is why SendCodeStaleAssemblyGuard refuses it.
            // A snippet declaring its own 'live' predates the helper and keeps it: injecting ours would not compile.
            var liveHelper = "        var live = new System.Func<string, System.Reflection.Assembly>(RvtMcp.Plugin.LiveAssembly.Of);\n";
            if (DeclaresLive(statements)) liveHelper = "";

            var wrapper = SyntaxFactory.ParseMemberDeclaration(@"
public class McpDynamicScript
{
    public static object Run(UIApplication app)
    {
        var uidoc = app.ActiveUIDocument;
        var doc = uidoc?.Document;
" + liveHelper + body + @"
    }
}");
            // Keep helper types outside Run; even a type between statements is valid input.
            var members = root.Members.Where(m => !(m is GlobalStatementSyntax)).Concat(new[] { wrapper });
            root = root.WithMembers(SyntaxFactory.List(members));
            foreach (var name in new[] { "System", "System.Linq", "System.Collections.Generic", "Autodesk.Revit.DB", "Autodesk.Revit.UI" })
            {
                if (!root.Usings.Any(u => u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.Name.ToString() == name))
                    root = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name)));
            }
            return root.NormalizeWhitespace().ToFullString();
        }

        /// <summary>An identifier token that is not a name reference is a declaration: a local, a loop or catch variable, a parameter.</summary>
        private static bool DeclaresLive(GlobalStatementSyntax[] statements)
        {
            return statements
                .SelectMany(s => s.DescendantTokens())
                .Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == "live" && !(t.Parent is IdentifierNameSyntax));
        }
    }
}
