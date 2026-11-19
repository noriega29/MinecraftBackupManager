using System.Text.Json;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Restic
{
    internal static class ResticBackupOutputParser
    {
        public static ResticBackupResult Parse(string standardOutput, string standardError, bool isIncomplete)
        {
            ArgumentNullException.ThrowIfNull(standardOutput);
            ArgumentNullException.ThrowIfNull(standardError);

            string? snapshotId = null;
            long filesNew = 0;
            long filesChanged = 0;
            long filesUnmodified = 0;
            long dataAdded = 0;
            double durationSeconds = 0;

            foreach (string line in SplitLines(standardOutput))
            {
                using JsonDocument? document = TryParseObject(line);

                if (document is null || GetString(document.RootElement, "message_type") != "summary")
                {
                    continue;
                }

                JsonElement root = document.RootElement;

                snapshotId = GetString(root, "snapshot_id");
                filesNew = GetInt64(root, "files_new");
                filesChanged = GetInt64(root, "files_changed");
                filesUnmodified = GetInt64(root, "files_unmodified");
                dataAdded = GetInt64(root, "data_added");
                durationSeconds = GetDouble(root, "total_duration");
            }

            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                throw new InvalidDataException("Restic no devolvió el resumen del respaldo con el identificador del snapshot.");
            }

            return new ResticBackupResult(
                snapshotId,
                isIncomplete,
                filesNew,
                filesChanged,
                filesUnmodified,
                dataAdded,
                TimeSpan.FromSeconds(durationSeconds),
                ReadWarnings(standardError));
        }

        private static List<ResticBackupWarning> ReadWarnings(string standardError)
        {
            var warnings = new List<ResticBackupWarning>();

            foreach (string line in SplitLines(standardError))
            {
                using JsonDocument? document = TryParseObject(line);

                if (document is null || GetString(document.RootElement, "message_type") != "error")
                {
                    continue;
                }

                JsonElement root = document.RootElement;

                string message = root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
                    ? GetString(error, "message") ?? string.Empty
                    : string.Empty;

                warnings.Add(new ResticBackupWarning(GetString(root, "item") ?? string.Empty, message));
            }

            return warnings;
        }

        private static string[] SplitLines(string text) =>
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        private static JsonDocument? TryParseObject(string line)
        {
            try
            {
                JsonDocument document = JsonDocument.Parse(line);

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return document;
                }

                document.Dispose();

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static long GetInt64(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number)
                ? number
                : 0;

        private static double GetDouble(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number)
                ? number
                : 0;
    }
}
