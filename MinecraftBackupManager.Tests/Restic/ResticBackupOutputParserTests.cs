using MinecraftBackupManager.Restic;

namespace MinecraftBackupManager.Tests.Restic
{
    [TestClass]
    public sealed class ResticBackupOutputParserTests
    {
        // Salidas reales de restic 0.19.1, copiadas de los experimentos manuales.
        private const string RealSummaryOutput = """{"message_type":"summary","files_new":2,"files_changed":0,"files_unmodified":0,"dirs_new":4,"dirs_changed":0,"dirs_unmodified":0,"data_blobs":2,"tree_blobs":5,"data_added":3837,"data_added_packed":2280,"total_files_processed":2,"total_bytes_processed":15,"total_duration":0.7761105,"backup_start":"2026-11-14T16:11:51.5617956-05:00","backup_end":"2026-11-14T16:11:52.3379061-05:00","snapshot_id":"cec9822ad6863d6c49a826626cca8ea33daafc6020a4002390db28e09a25a843"}""";

        private const string LockedFileSummaryOutput = """{"message_type":"summary","files_new":0,"files_changed":0,"files_unmodified":2,"dirs_new":0,"dirs_changed":0,"dirs_unmodified":4,"data_blobs":0,"tree_blobs":0,"data_added":0,"data_added_packed":0,"total_files_processed":2,"total_bytes_processed":15,"total_duration":0.7442333,"backup_start":"2026-11-14T16:20:54.1155472-05:00","backup_end":"2026-11-14T16:20:54.8597805-05:00","snapshot_id":"b1abf106edfd348d2636df7ea4791abe1ff54eb52df4a6ceb866853563218c65"}""";

        private const string LockedFileErrorOutput = """
            {"message_type":"error","error":{"message":"open \\\\?\\C:\\Temp\\carpeta de prueba\\bloqueado.dat: El proceso no tiene acceso al archivo porque está siendo utilizado por otro proceso."},"during":"archival","item":"C:\\Temp\\carpeta de prueba\\bloqueado.dat"}
            {"message_type":"exit_error","code":3,"message":"Warning: at least one source file could not be read"}
            """;

        [TestMethod]
        public void Parse_WithCompleteBackup_ShouldReturnSnapshotAndStatistics()
        {
            var result = ResticBackupOutputParser.Parse(RealSummaryOutput, string.Empty, isIncomplete: false);

            Assert.AreEqual("cec9822ad6863d6c49a826626cca8ea33daafc6020a4002390db28e09a25a843", result.SnapshotId);
            Assert.IsFalse(result.IsIncomplete);
            Assert.AreEqual(2, result.FilesNew);
            Assert.AreEqual(0, result.FilesChanged);
            Assert.AreEqual(0, result.FilesUnmodified);
            Assert.AreEqual(3837, result.DataAdded);
            Assert.AreEqual(0.776, result.Duration.TotalSeconds, 0.01);
            Assert.IsEmpty(result.Warnings);
        }

        [TestMethod]
        public void Parse_WithLockedFile_ShouldReturnIncompleteResultWithWarning()
        {
            var result = ResticBackupOutputParser.Parse(LockedFileSummaryOutput, LockedFileErrorOutput, isIncomplete: true);

            Assert.AreEqual("b1abf106edfd348d2636df7ea4791abe1ff54eb52df4a6ceb866853563218c65", result.SnapshotId);
            Assert.IsTrue(result.IsIncomplete);
            Assert.AreEqual(2, result.FilesUnmodified);

            // La línea "exit_error" no cuenta como advertencia de un archivo.
            Assert.HasCount(1, result.Warnings);
            Assert.AreEqual(@"C:\Temp\carpeta de prueba\bloqueado.dat", result.Warnings[0].Item);
            StringAssert.Contains(result.Warnings[0].Message, "bloqueado.dat");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("{\"message_type\":\"status\",\"percent_done\":0.5}")]
        [DataRow("esto no es JSON")]
        public void Parse_WithoutSummary_ShouldThrowInvalidDataException(string standardOutput)
        {
            TestAssert.Throws<InvalidDataException>(
                () => ResticBackupOutputParser.Parse(standardOutput, string.Empty, isIncomplete: false));
        }

        [TestMethod]
        public void Parse_ShouldIgnoreUnknownMessagesAndFields()
        {
            // Tolerancia: mensajes y campos que hoy no existen (inventados para la prueba).
            string standardOutput =
                "{\"message_type\":\"status\",\"percent_done\":0.5}\n" +
                "esto no es JSON\n" +
                "{\"message_type\":\"summary\",\"snapshot_id\":\"abc123\",\"files_new\":7,\"campo_nuevo\":{\"x\":1}}\n";

            var result = ResticBackupOutputParser.Parse(standardOutput, "texto suelto\n", isIncomplete: false);

            Assert.AreEqual("abc123", result.SnapshotId);
            Assert.AreEqual(7, result.FilesNew);
            Assert.IsEmpty(result.Warnings);
        }
    }
}
