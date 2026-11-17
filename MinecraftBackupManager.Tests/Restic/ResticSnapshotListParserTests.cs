using MinecraftBackupManager.Restic;

namespace MinecraftBackupManager.Tests.Restic
{
    [TestClass]
    public sealed class ResticSnapshotListParserTests
    {
        // Salidas reales de restic 0.19.1 (snapshots --json), con el usuario del equipo cambiado.
        private const string UntaggedSnapshotJson = """{"time":"2026-11-14T16:11:51.5617956-05:00","tree":"d67636429b2c92f78a32ba1c3324d7771d625e635df59597c9a187f683b6c531","paths":["C:\\Temp\\carpeta de prueba"],"hostname":"prueba","username":"PC\\User","excludes":["session.lock"],"program_version":"restic 0.19.1","summary":{"backup_start":"2026-11-14T16:11:51.5617956-05:00","backup_end":"2026-11-14T16:11:52.3379061-05:00","files_new":2,"files_changed":0,"files_unmodified":0,"dirs_new":4,"dirs_changed":0,"dirs_unmodified":0,"data_blobs":2,"tree_blobs":5,"data_added":3837,"data_added_packed":2280,"total_files_processed":2,"total_bytes_processed":15},"id":"cec9822ad6863d6c49a826626cca8ea33daafc6020a4002390db28e09a25a843","short_id":"cec9822a"}""";

        private const string TaggedSnapshotJson = """{"time":"2026-11-16T19:30:25.6535776-05:00","parent":"b1abf106edfd348d2636df7ea4791abe1ff54eb52df4a6ceb866853563218c65","tree":"0ee3b82d907c34a92e3854b18437fa76eef3acb8cee74e3170e22bf73b1b26b2","paths":["C:\\Temp\\carpeta de prueba"],"hostname":"prueba","username":"PC\\User","excludes":["session.lock"],"tags":["hourly"],"program_version":"restic 0.19.1","summary":{"backup_start":"2026-11-16T19:30:25.6535776-05:00","backup_end":"2026-11-16T19:30:26.4650378-05:00","files_new":1,"files_changed":0,"files_unmodified":2,"dirs_new":0,"dirs_changed":3,"dirs_unmodified":1,"data_blobs":1,"tree_blobs":4,"data_added":4485,"data_added_packed":2172,"total_files_processed":3,"total_bytes_processed":16},"id":"1b5795b83fec9f91023dbcb31519877405e776fe8b26e0d9e9ed5a604fff2ea8","short_id":"1b5795b8"}""";

        [TestMethod]
        public void Parse_WithRealOutput_ShouldReturnSnapshotsWithTagsAndStatistics()
        {
            var snapshots = ResticSnapshotListParser.Parse("[" + UntaggedSnapshotJson + "," + TaggedSnapshotJson + "]");

            Assert.HasCount(2, snapshots);

            Assert.AreEqual("cec9822ad6863d6c49a826626cca8ea33daafc6020a4002390db28e09a25a843", snapshots[0].Id);
            Assert.AreEqual("cec9822a", snapshots[0].ShortId);
            Assert.AreEqual(
                new DateTimeOffset(2026, 11, 14, 16, 11, 51, TimeSpan.FromHours(-5)).AddTicks(5617956),
                snapshots[0].Time);
            Assert.AreEqual("prueba", snapshots[0].Host);
            Assert.HasCount(1, snapshots[0].Paths);
            Assert.AreEqual(@"C:\Temp\carpeta de prueba", snapshots[0].Paths[0]);
            Assert.IsEmpty(snapshots[0].Tags);
            Assert.AreEqual(15L, snapshots[0].TotalBytesProcessed);

            Assert.AreEqual("1b5795b8", snapshots[1].ShortId);
            Assert.HasCount(1, snapshots[1].Tags);
            Assert.AreEqual("hourly", snapshots[1].Tags[0]);
            Assert.AreEqual(16L, snapshots[1].TotalBytesProcessed);
        }

        [TestMethod]
        public void Parse_ShouldOrderSnapshotsByTime()
        {
            // Se entregan al revés a propósito.
            var snapshots = ResticSnapshotListParser.Parse("[" + TaggedSnapshotJson + "," + UntaggedSnapshotJson + "]");

            Assert.AreEqual("cec9822a", snapshots[0].ShortId);
            Assert.AreEqual("1b5795b8", snapshots[1].ShortId);
        }

        [TestMethod]
        [DataRow("[]")]
        [DataRow("null")]
        public void Parse_WithNoSnapshots_ShouldReturnEmptyList(string standardOutput)
        {
            Assert.IsEmpty(ResticSnapshotListParser.Parse(standardOutput));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("esto no es JSON")]
        [DataRow("{}")]
        [DataRow("[1]")]
        [DataRow("[{}]")]
        [DataRow("[{\"id\":\"abc\"}]")]
        [DataRow("[{\"id\":\"abc\",\"time\":\"ayer\"}]")]
        public void Parse_WithInvalidOutput_ShouldThrowInvalidDataException(string standardOutput)
        {
            TestAssert.Throws<InvalidDataException>(() => ResticSnapshotListParser.Parse(standardOutput));
        }

        [TestMethod]
        public void Parse_ShouldIgnoreUnknownFieldsAndUseDefaultsForMissingOptionalOnes()
        {
            // El campo "campo_nuevo" está inventado para comprobar la tolerancia.
            var snapshots = ResticSnapshotListParser.Parse(
                "[{\"id\":\"abc\",\"time\":\"2026-01-01T00:00:00Z\",\"campo_nuevo\":{\"x\":1}}]");

            Assert.HasCount(1, snapshots);
            Assert.AreEqual("abc", snapshots[0].Id);
            Assert.AreEqual("abc", snapshots[0].ShortId);
            Assert.AreEqual(string.Empty, snapshots[0].Host);
            Assert.IsEmpty(snapshots[0].Paths);
            Assert.IsEmpty(snapshots[0].Tags);
            Assert.IsNull(snapshots[0].TotalBytesProcessed);
        }
    }
}
