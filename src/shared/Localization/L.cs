using System;
using System.Threading;

namespace RvtMcp.Plugin.Localization
{
    /// <summary>
    /// UI strings from the embedded English catalog. The table is built on first use; <see cref="Changed"/> only fires
    /// from the test hooks, so UI code that refreshes on it keeps working unchanged.
    /// </summary>
    public static class L
    {
        private static volatile StringTable _current;
        private static int _version;
        private static readonly object _gate = new object();

        public static string Locale => "en";
        public static int Version => _version;
        public static event EventHandler Changed;

        /// <summary>Never throws. A key missing from the catalog comes back as the key.</summary>
        public static string T(string key, params (string Name, object Value)[] args)
        {
            var t = _current ?? Load();
            return t == null ? key ?? string.Empty : t.T(key, args);
        }

        private static StringTable Load()
        {
            lock (_gate)
            {
                if (_current != null) return _current;
                try
                {
                    var en = EmbeddedCatalog.Load(typeof(L).Assembly, "en");
                    if (en == null) return null;
                    _current = StringTable.Build("en", en, null, null);
                    Interlocked.Increment(ref _version);
                }
                catch { return null; }
                return _current;
            }
        }

        // ---- test hooks (linked sources compile into the test assembly) ----

        internal static void InitializeForTests(StringTable table)
        {
            _current = table;
            Interlocked.Increment(ref _version);
            var handler = Changed;
            if (handler == null) return;
            foreach (EventHandler h in handler.GetInvocationList())
            {
                try { h(null, EventArgs.Empty); } catch { }
            }
        }

        internal static void ResetForTests()
        {
            _current = null;
            _version = 0;
            Changed = null;
        }
    }
}
