namespace MinecraftBackupManager.Tests.Backup
{
    /// <summary>
    /// Reloj controlado por las pruebas: la hora solo cambia cuando se le ordena.
    /// </summary>
    internal sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private TimeZoneInfo _localTimeZone = TimeZoneInfo.Utc;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        // Por defecto es UTC, para que el "día local" no dependa del equipo que ejecuta las pruebas.
        public override TimeZoneInfo LocalTimeZone => _localTimeZone;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;

        public void SetLocalTimeZone(TimeZoneInfo timeZone) => _localTimeZone = timeZone;

        public void AdvanceDays(int days) => _utcNow = _utcNow.AddDays(days);
    }
}
