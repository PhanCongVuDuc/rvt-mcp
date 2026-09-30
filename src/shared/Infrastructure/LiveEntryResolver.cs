using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RvtMcp.Plugin
{
    public enum EntryResolutionKind
    {
        Stage,
        Startup,
        Failed,
    }

    public sealed class EntryResolution
    {
        public LiveEntry Entry { get; private set; }
        public EntryResolutionKind Kind { get; private set; }
        public string Directory { get; private set; }
        public IReadOnlyDictionary<string, Assembly> Assemblies { get; private set; }
        public DateTime? BuiltAt { get; private set; }
        public string Reason { get; private set; }
        public string Message { get; private set; }

        public static EntryResolution Stage(LiveEntry entry, string directory, IReadOnlyDictionary<string, Assembly> assemblies, DateTime? builtAt)
        {
            return new EntryResolution
            {
                Entry = entry,
                Kind = EntryResolutionKind.Stage,
                Directory = directory,
                Assemblies = CopyIgnoringCase(assemblies),
                BuiltAt = builtAt,
            };
        }

        private static Dictionary<string, Assembly> CopyIgnoringCase(IReadOnlyDictionary<string, Assembly> assemblies)
        {
            var copy = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in assemblies) copy[pair.Key] = pair.Value;
            return copy;
        }

        public static EntryResolution Startup(LiveEntry entry, string directory, DateTime? builtAt)
        {
            return new EntryResolution
            {
                Entry = entry,
                Kind = EntryResolutionKind.Startup,
                Directory = directory,
                Assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase),
                BuiltAt = builtAt,
            };
        }

        public static EntryResolution Failed(LiveEntry entry, string reason, string message)
        {
            return new EntryResolution
            {
                Entry = entry,
                Kind = EntryResolutionKind.Failed,
                Assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase),
                Reason = reason,
                Message = message,
            };
        }
    }

    /// <summary>
    /// Asks an add-in's live loader which build is current, the way a click on one of its buttons would.
    /// </summary>
    public static class LiveEntryResolver
    {
        public const string ClassNotLoaded = "class-not-loaded";
        public const string MethodMissing = "method-missing";
        public const string Threw = "threw";
        public const string ReturnedNull = "returned-null";

        public static EntryResolution Resolve(LiveEntry entry, IEnumerable<Assembly> loadedInOrder)
        {
            // Every staged build carries its own copy of the loader, with empty state: calling one of those would
            // stage the same build again on every call. The copy Revit loaded at startup is always loaded first,
            // because a stage only exists after that copy's first call.
            var startup = loadedInOrder.FirstOrDefault(a => !a.IsDynamic && TypeOrNull(a, entry.ClassName) != null);
            if (startup == null)
            {
                return Fail(entry, ClassNotLoaded,
                    "no loaded assembly defines class '" + entry.ClassName + "' - is the add-in loaded in this Revit, and is the name right?");
            }

            var method = TypeOrNull(startup, entry.ClassName).GetMethod(entry.MethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (method == null || !typeof(Assembly).IsAssignableFrom(method.ReturnType))
            {
                return Fail(entry, MethodMissing,
                    "class '" + entry.ClassName + "' has no static method '" + entry.MethodName + "(string)' returning Assembly");
            }

            var mainName = startup.GetName().Name;
            var startupDirectory = Path.GetDirectoryName(startup.Location);

            Assembly main;
            try
            {
                main = method.Invoke(null, new object[] { mainName + ".dll" }) as Assembly;
            }
            catch (TargetInvocationException e)
            {
                var inner = e.InnerException ?? e;
                return Fail(entry, Threw, "calling it for '" + mainName + ".dll' threw " + inner.GetType().Name + ": " + inner.Message);
            }

            if (main == null)
            {
                return Fail(entry, ReturnedNull, "calling it for '" + mainName + ".dll' returned null");
            }

            var directory = Path.GetDirectoryName(main.Location);
            var builtAt = WriteTimeOrNull(Path.Combine(directory, mainName + ".dll"));

            if (SameDirectory(directory, startupDirectory))
            {
                return EntryResolution.Startup(entry, directory, builtAt);
            }

            return EntryResolution.Stage(entry, directory, StagedAssemblies(method, directory), builtAt);
        }

        /// <summary>
        /// The stage directory is exactly what the loader copied, so its DLLs are what it serves - no second copy of
        /// the loader's own filter here. Each is asked for by name to get the instance the add-in loaded.
        /// </summary>
        private static Dictionary<string, Assembly> StagedAssemblies(MethodInfo method, string directory)
        {
            var staged = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in System.IO.Directory.GetFiles(directory, "*.dll"))
            {
                Assembly assembly;
                try
                {
                    assembly = method.Invoke(null, new object[] { Path.GetFileName(path) }) as Assembly;
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                if (assembly == null || !SameDirectory(Path.GetDirectoryName(assembly.Location), directory)) continue;
                staged[assembly.GetName().Name] = assembly;
            }

            return staged;
        }

        private static EntryResolution Fail(LiveEntry entry, string reason, string sentence)
        {
            return EntryResolution.Failed(entry, reason, "Live entry '" + entry.Value + "' could not be reached: " + sentence + ".");
        }

        private static Type TypeOrNull(Assembly assembly, string className)
        {
            try
            {
                return assembly.GetType(className, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DateTime? WriteTimeOrNull(string path)
        {
            if (!File.Exists(path)) return null;
            return File.GetLastWriteTime(path);
        }

        private static bool SameDirectory(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
