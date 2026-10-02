using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RvtMcp.Plugin;
using Xunit;
using ServerAuthToken = RvtMcp.Server.AuthToken;

namespace RvtMcp.Tests
{
    // Two Revits of one year: each plugin writes revit-YYYY-PID.json beside the shared revit-YYYY.json, and the server
    // must reach either one by pid. Discovery skips files whose pid is dead, so both pids here are live processes.
    [Collection("DiscoveryDir")]
    public class DiscoveryPerPidTests : IDisposable
    {
        private readonly string _dir;
        private readonly int _first = Process.GetCurrentProcess().Id;
        private readonly int _second = Process.GetProcesses().First(p => p.Id > 4 && p.Id != Process.GetCurrentProcess().Id).Id;

        public DiscoveryPerPidTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "rvtmcp-discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ServerAuthToken.DiscoveryDirOverride = _dir;
            ServerAuthToken.Target = null;
            ServerAuthToken.TargetPid = null;
        }

        public void Dispose()
        {
            ServerAuthToken.DiscoveryDirOverride = null;
            ServerAuthToken.Target = null;
            ServerAuthToken.TargetPid = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static string Json(int pid) =>
            "{ \"schema_version\": 2, \"revit_year\": 2025, \"transport\": \"pipe\", \"port\": null, " +
            "\"pipe_name\": \"RvtMcp-" + pid + "\", \"auth_token\": \"token-" + pid + "\", \"pid\": " + pid + " }";

        private void WritePlugin(int pid)
        {
            File.WriteAllText(Path.Combine(_dir, DiscoveryFiles.PidFileName("2025", pid)), Json(pid));
            File.WriteAllText(Path.Combine(_dir, DiscoveryFiles.YearFileName("2025")), Json(pid));
        }

        [Fact]
        public void ListAvailable_TwoRevitsOfOneYear_ListsEachPidOnce()
        {
            WritePlugin(_first);
            WritePlugin(_second);

            var pids = ServerAuthToken.ListAvailable().Select(d => d.Pid).OrderBy(p => p).ToArray();

            Assert.Equal(new[] { _first, _second }.OrderBy(p => p).ToArray(), pids);
        }

        [Fact]
        public void TryReadPipe_PinnedToTheRevitThatStartedFirst_ReturnsItsPipeNotTheYearFiles()
        {
            WritePlugin(_first);
            WritePlugin(_second);
            ServerAuthToken.Target = "2025";
            ServerAuthToken.TargetPid = _first;

            ServerAuthToken.TryReadPipe(out var pipeName, out var token, out _, out var pid);

            Assert.Equal("RvtMcp-" + _first + "|token-" + _first + "|" + _first, pipeName + "|" + token + "|" + pid);
        }

        [Fact]
        public void TryReadPipe_YearOnly_ReturnsTheRevitThatWroteTheYearFileLast()
        {
            WritePlugin(_first);
            WritePlugin(_second);
            ServerAuthToken.Target = "2025";

            ServerAuthToken.TryReadPipe(out var pipeName, out _, out _, out _);

            Assert.Equal("RvtMcp-" + _second, pipeName);
        }

        [Fact]
        public void ListAvailable_OlderPluginWithOnlyAYearFile_IsStillListed()
        {
            File.WriteAllText(Path.Combine(_dir, DiscoveryFiles.YearFileName("2025")), Json(_first));

            var pids = ServerAuthToken.ListAvailable().Select(d => d.Pid).ToArray();

            Assert.Equal(new[] { _first }, pids);
        }

        [Fact]
        public void DeleteOwn_YearFileTakenOverByALaterRevit_KeepsTheYearFile()
        {
            WritePlugin(_first);
            WritePlugin(_second);

            DiscoveryFiles.DeleteOwn(_dir, "2025", _first);

            var left = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { DiscoveryFiles.PidFileName("2025", _second), DiscoveryFiles.YearFileName("2025") }.OrderBy(n => n).ToArray(), left);
        }

        [Fact]
        public void DeleteOwn_YearFileStillNamesThisRevit_DeletesBothFiles()
        {
            WritePlugin(_first);

            DiscoveryFiles.DeleteOwn(_dir, "2025", _first);

            Assert.Empty(Directory.GetFiles(_dir));
        }
    }
}
