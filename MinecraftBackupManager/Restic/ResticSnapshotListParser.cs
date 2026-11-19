using System.Text.Json;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Restic
{
    internal static class ResticSnapshotListParser
    {
        public static IReadOnlyList<ResticSnapshot> Parse(string standardOutput)
        {
            ArgumentNullException.ThrowIfNull(standardOutput);

            if (string.IsNullOrWhiteSpace(standardOutput))
            {
                throw new InvalidDataException("Restic no devolvió la lista de snapshots.");
            }

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(standardOutput);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("La lista de snapshots de Restic no es un JSON válido.", exception);
            }

            using (document)
            {
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Null)
                {
                    return Array.Empty<ResticSnapshot>();
                }

                if (root.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("La lista de snapshots de Restic no tiene el formato esperado.");
                }

                var snapshots = new List<ResticSnapshot>();

                foreach (JsonElement element in root.EnumerateArray())
                {
                    snapshots.Add(ParseSnapshot(element));
                }

                // No se depende del orden en que Restic los entregue.
                return snapshots.OrderBy(snapshot => snapshot.Time).ToList();
            }
        }

        private static ResticSnapshot ParseSnapshot(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Un elemento de la lista de snapshots no tiene el formato esperado.");
            }

            string? id = GetString(element, "id");

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("Un snapshot de la lista no tiene identificador.");
            }

            if (!element.TryGetProperty("time", out JsonElement timeElement)
                || timeElement.ValueKind != JsonValueKind.String
                || !timeElement.TryGetDateTimeOffset(out DateTimeOffset time))
            {
                throw new InvalidDataException($"El snapshot {id} no tiene una fecha válida.");
            }

            string shortId = GetString(element, "short_id") ?? (id.Length > 8 ? id[..8] : id);

            long? totalBytes = null;

            if (element.TryGetProperty("summary", out JsonElement summary)
                && summary.ValueKind == JsonValueKind.Object
                && summary.TryGetProperty("total_bytes_processed", out JsonElement bytes)
                && bytes.ValueKind == JsonValueKind.Number
                && bytes.TryGetInt64(out long value))
            {
                totalBytes = value;
            }

            return new ResticSnapshot(
                id,
                shortId,
                time,
                GetString(element, "hostname") ?? string.Empty,
                GetStringList(element, "paths"),
                GetStringList(element, "tags"),
                totalBytes);
        }

        private static string? GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static List<string> GetStringList(JsonElement element, string propertyName)
        {
            var values = new List<string>();

            if (element.TryGetProperty(propertyName, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
                    {
                        values.Add(text);
                    }
                }
            }

            return values;
        }
    }
}
