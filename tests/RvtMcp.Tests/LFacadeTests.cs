using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RvtMcp.Plugin.Localization;
using Xunit;

namespace RvtMcp.Tests
{
    [Collection("L10n")]
    public class LFacadeTests : IDisposable
    {
        public LFacadeTests() => L.ResetForTests();
        public void Dispose() => L.ResetForTests();

        private static StringTable TableFor(string locale) => StringTable.Build(
            locale,
            new Dictionary<string, string> { ["k"] = "EN-" + "value" },
            locale == "en" ? null : new Dictionary<string, string> { ["k"] = locale.ToUpperInvariant() + "-value" },
            null);

        [Fact]
        public void T_BeforeInit_ReturnsKey()
        {
            Assert.Equal("some.key", L.T("some.key"));
        }

    }
}
