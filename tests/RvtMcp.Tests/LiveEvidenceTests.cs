using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class LiveEvidenceTests
    {
        private const string StairEntry = "Revgen.StairDesign.Standalone.LiveReload.LiveLoader.CommandAssembly";
        private const string StageDirectory = @"C:\Temp\RevGenLive\Stair\639263378780075237";
        private static readonly LiveEntry Entry = LiveEntry.ParseAll(new[] { StairEntry }).Single();
        private static readonly DateTime BuildTime = new DateTime(2026, 9, 30, 11, 4, 26, DateTimeKind.Local);

        [Fact]
        public void From_StageResolution_SourceIsStage()
        {
            var resolution = EntryResolution.Stage(Entry, StageDirectory, new Dictionary<string, Assembly>(), BuildTime);

            var evidence = LiveEvidence.From(resolution, Entry);

            Assert.True(evidence.Source == "stage", "a snippet bound to a staged build reports 'stage', got " + evidence.Source);
        }

        [Fact]
        public void From_StartupResolution_SourceIsStartup()
        {
            var resolution = EntryResolution.Startup(Entry, StageDirectory, BuildTime);

            var evidence = LiveEvidence.From(resolution, Entry);

            Assert.True(evidence.Source == "startup",
                "with no newer build the snippet ran the startup copy, and must say so, got " + evidence.Source);
        }

        [Fact]
        public void From_StageResolution_CarriesEntryAndDirectory()
        {
            var resolution = EntryResolution.Stage(Entry, StageDirectory, new Dictionary<string, Assembly>(), BuildTime);

            var evidence = LiveEvidence.From(resolution, Entry);

            Assert.True(evidence.Entry == StairEntry && evidence.Directory == StageDirectory,
                "the evidence names the entry and the directory of the build that ran, got " + evidence.Entry + " / " + evidence.Directory);
        }

        [Fact]
        public void From_StageResolution_BuiltAtIsIsoLocalTimeWithOffset()
        {
            var resolution = EntryResolution.Stage(Entry, StageDirectory, new Dictionary<string, Assembly>(), BuildTime);

            var evidence = LiveEvidence.From(resolution, Entry);

            Assert.True(Regex.IsMatch(evidence.BuiltAt ?? "", @"^2026-09-30T11:04:26[+-]\d{2}:\d{2}$"),
                "builtAt is ISO 8601 in local time with its offset, so it compares with the build clock, got " + evidence.BuiltAt);
        }

        [Fact]
        public void FromUsedOrNull_SnippetUsesNoServedAssembly_ReturnsNull()
        {
            var evidence = LiveEvidence.FromUsedOrNull(new EntryResolution[0]);

            Assert.True(evidence == null, "a snippet that touches no served assembly keeps the old result shape, with no 'live' (FR-005)");
        }
    }
}
