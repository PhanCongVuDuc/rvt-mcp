using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using RvtMcp.Plugin.Survey;
using RvtMcp.Server.Memory;
using Xunit;

namespace RvtMcp.Tests
{
    public class SurveySnapshotTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rvt-survey-history-" + Guid.NewGuid().ToString("N"));
        private readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        private static string Id() => Guid.NewGuid().ToString("N");
        private static JArray Targets() => new JArray(new JObject { ["elementId"] = 1, ["uniqueId"] = "unique-1" });
        private static JObject Snapshot()
        {
            JObject Values(string text) => new JObject { ["status"] = "captured", ["parameters"] = new JArray(new JObject {
                ["builtInId"] = -1010106, ["ownerId"] = 1, ["storageType"] = "String", ["status"] = "read", ["value"] = text }) };
            return new JObject { ["status"] = "captured", ["surveyId"] = Id(), ["source"] = "plugin_execution_readback",
                ["beforeAt"] = "2026-10-02T00:00:01Z", ["afterAt"] = "2026-10-02T00:00:02Z", ["elements"] = new JArray(new JObject {
                    ["elementId"] = 1, ["uniqueId"] = "unique-1", ["before"] = Values("old"), ["after"] = Values("new") }) };
        }

        [Fact]
        public void Selection_is_session_bound_defensively_copied_and_single_use()
        {
            var cache = new SurveySnapshotCache(); var doc = new object(); var session = Id(); var targets = Targets();
            cache.Remember(session, doc, "model", Id(), targets, _now);
            targets[0]["uniqueId"] = "forged";
            Assert.Equal("unavailable", cache.Take(Id(), doc, "model", _now).Value<string>("status"));
            var selection = cache.Take(session, doc, "model", _now);
            Assert.Equal("unique-1", selection["targets"][0].Value<string>("uniqueId"));
            Assert.Equal("unavailable", cache.Take(session, doc, "model", _now).Value<string>("status"));
            Assert.False(Directory.Exists(_root));
        }

        [Theory]
        [InlineData("reopen")]
        [InlineData("save-as")]
        [InlineData("expired")]
        [InlineData("clock-back")]
        public void Context_or_time_changes_discard_selection(string change)
        {
            var cache = new SurveySnapshotCache(); var doc = new object(); var session = Id();
            cache.Remember(session, doc, "model", Id(), Targets(), _now);
            var result = cache.Take(session, change == "reopen" ? new object() : doc, change == "save-as" ? "copy" : "model",
                change == "expired" ? _now.AddMinutes(6) : change == "clock-back" ? _now.AddSeconds(-1) : _now);
            Assert.Equal("unavailable", result.Value<string>("status"));
            Assert.Null(result["targets"]);
            Assert.Equal("unavailable", cache.Take(session, doc, "model", _now).Value<string>("status"));
        }

        [Theory]
        [InlineData("document_changed_or_undo")]
        [InlineData("document_or_view_context_changed")]
        [InlineData("history_disabled")]
        public void Invalidation_discards_all_sessions_without_retaining_values(string reason)
        {
            var cache = new SurveySnapshotCache(); var doc = new object(); var sessions = new[] { Id(), Id() };
            foreach (var session in sessions) cache.Remember(session, doc, "model", Id(), Targets(), _now);
            cache.Clear(reason);
            foreach (var session in sessions) Assert.Equal(reason, cache.Take(session, doc, "model", _now).Value<string>("reason"));
        }

        [Fact]
        public void Capacity_and_latest_survey_are_bounded()
        {
            var cache = new SurveySnapshotCache(); var doc = new object(); var sessions = Enumerable.Range(0,9).Select(_ => Id()).ToArray();
            for (var i = 0; i < sessions.Length; i++) cache.Remember(sessions[i], doc, "model", Id(), Targets(), _now.AddSeconds(i));
            Assert.Equal("unavailable", cache.Take(sessions[0], doc, "model", _now.AddSeconds(10)).Value<string>("status"));
            var latest = Id(); cache.Remember(sessions[1], doc, "model", latest, Targets(), _now.AddSeconds(10));
            Assert.Equal(latest, cache.Take(sessions[1], doc, "model", _now.AddSeconds(11)).Value<string>("surveyId"));
            Assert.Throws<ArgumentException>(() => cache.Remember(Id(), doc, "", Id(), Targets(), _now));
            Assert.Throws<ArgumentException>(() => cache.Remember(Id(), doc, "model", Id(), new JArray(Enumerable.Range(0,26)), _now));
        }

        [Theory]
        [InlineData("modified", "unique-1", true)]
        [InlineData("modified", "replacement", false)]
        [InlineData("added", "unique-1", false)]
        [InlineData("deleted", "unique-1", false)]
        [InlineData("modified", null, false)]
        public void Observations_only_match_captured_modified_unique_identity(string kind, string unique, bool matches)
        {
            var element = new JObject { ["elementId"] = 1, ["uniqueId"] = unique, ["kind"] = kind };
            Assert.Equal(matches, SurveySnapshotCache.Match(Snapshot(), element) != null);
            Assert.Null(SurveySnapshotCache.Match(SurveySnapshotCache.Unavailable("stale"), element));
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
    }
}
