using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Decide de qué tipo es el próximo respaldo automático (weekly, daily u hourly)
    /// según los snapshots que ya existen y el día local de hoy.
    /// </summary>
    public sealed class BackupKindSelector
    {
        private readonly TimeProvider _timeProvider;

        public BackupKindSelector(TimeProvider? timeProvider = null)
        {
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public ResticBackupKind Select(IReadOnlyList<ResticSnapshot> snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshots);

            DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

            // La semana empieza el lunes: lunes = 0 ... domingo = 6.
            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            DateOnly weekStart = today.AddDays(-daysSinceMonday);

            string weeklyTag = ResticBackupKind.Weekly.ToTag();
            string dailyTag = ResticBackupKind.Daily.ToTag();

            bool hasWeeklyThisWeek = false;
            bool hasDailyToday = false;

            foreach (ResticSnapshot snapshot in snapshots)
            {
                DateOnly day = GetLocalDay(snapshot.Time);

                // Una fecha futura (reloj atrasado) no cuenta.
                if (day > today)
                {
                    continue;
                }

                if (day >= weekStart && snapshot.Tags.Contains(weeklyTag))
                {
                    hasWeeklyThisWeek = true;
                }

                if (day == today && snapshot.Tags.Contains(dailyTag))
                {
                    hasDailyToday = true;
                }
            }

            if (!hasWeeklyThisWeek)
            {
                return ResticBackupKind.Weekly;
            }

            if (!hasDailyToday)
            {
                return ResticBackupKind.Daily;
            }

            return ResticBackupKind.Hourly;
        }

        // La hora de Restic trae su propia zona horaria: se pasa a la zona local del
        // equipo para saber a qué día pertenece realmente.
        private DateOnly GetLocalDay(DateTimeOffset time)
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(time, _timeProvider.LocalTimeZone);

            return DateOnly.FromDateTime(local.DateTime);
        }
    }
}
