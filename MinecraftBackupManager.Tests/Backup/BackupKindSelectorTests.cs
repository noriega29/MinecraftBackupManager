using System.Globalization;
using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Backup
{
    [TestClass]
    public sealed class BackupKindSelectorTests
    {
        // Miércoles 25 de noviembre de 2026. La semana empieza el lunes 23.
        private const string Wednesday = "2026-11-25T12:00:00Z";

        // ============================================================
        // CASOS BÁSICOS
        // ============================================================

        [TestMethod]
        public void Select_WithNoSnapshots_ShouldReturnWeekly()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            Assert.AreEqual(ResticBackupKind.Weekly, selector.Select(Array.Empty<ResticSnapshot>()));
        }

        [TestMethod]
        public void Select_WithOnlyInitialManualAndHourly_ShouldReturnWeekly()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-25T08:00:00Z", "initial"),
                Snapshot("2026-11-25T09:00:00Z", "manual"),
                Snapshot("2026-11-25T10:00:00Z", "hourly"),
                Snapshot("2026-11-25T11:00:00Z")
            };

            // Ninguno de estos tipos cubre un weekly ni un daily.
            Assert.AreEqual(ResticBackupKind.Weekly, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_WithWeeklyThisWeekAndDailyToday_ShouldReturnHourly()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-24T12:00:00Z", "weekly"),
                Snapshot("2026-11-25T03:00:00Z", "daily")
            };

            Assert.AreEqual(ResticBackupKind.Hourly, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_WithWeeklyThisWeekButNoDailyToday_ShouldReturnDaily()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-24T12:00:00Z", "weekly"),
                Snapshot("2026-11-25T10:00:00Z", "hourly")
            };

            Assert.AreEqual(ResticBackupKind.Daily, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_WithDailyFromYesterdayOnly_ShouldReturnDaily()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-24T12:00:00Z", "weekly"),
                Snapshot("2026-11-24T13:00:00Z", "daily")
            };

            Assert.AreEqual(ResticBackupKind.Daily, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_WithWeeklyFromLastWeek_ShouldReturnWeeklyEvenWithDailyToday()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-20T12:00:00Z", "weekly"),
                Snapshot("2026-11-25T03:00:00Z", "daily")
            };

            // El weekly tiene prioridad sobre el daily.
            Assert.AreEqual(ResticBackupKind.Weekly, selector.Select(snapshots));
        }

        // ============================================================
        // LÍMITES DE LA SEMANA
        // ============================================================

        [TestMethod]
        [DataRow("2026-11-23T00:00:00Z", ResticBackupKind.Hourly)]  // Lunes 00:00: ya es esta semana.
        [DataRow("2026-11-22T23:59:59Z", ResticBackupKind.Weekly)]  // Domingo 23:59:59: es la semana pasada.
        public void Select_ShouldTreatMondayAsTheStartOfTheWeek(string weeklyTime, ResticBackupKind expected)
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot(weeklyTime, "weekly"),
                Snapshot("2026-11-25T03:00:00Z", "daily")
            };

            Assert.AreEqual(expected, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_WhenTheWeekChangesFromSundayToMonday_ShouldAskForANewWeekly()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-29T23:00:00Z", CultureInfo.InvariantCulture));
            var selector = new BackupKindSelector(clock);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-23T12:00:00Z", "weekly"),
                Snapshot("2026-11-29T10:00:00Z", "daily")
            };

            // Domingo 29, 23:00: el weekly del lunes 23 sigue siendo de esta semana.
            Assert.AreEqual(ResticBackupKind.Hourly, selector.Select(snapshots));

            clock.SetUtcNow(DateTimeOffset.Parse("2026-11-30T00:30:00Z", CultureInfo.InvariantCulture));

            // Lunes 30, 00:30: empezó otra semana y todavía no hay weekly.
            Assert.AreEqual(ResticBackupKind.Weekly, selector.Select(snapshots));
        }

        // ============================================================
        // ZONAS HORARIAS
        // ============================================================

        [TestMethod]
        public void Select_ShouldPlaceSnapshotsOnTheLocalDayNotOnTheDayWrittenByRestic()
        {
            // El equipo está en UTC-5. 02:00 UTC del 26 son las 21:00 del 25 (miércoles).
            var selector = CreateSelector("2026-11-26T02:00:00Z", offsetHours: -5);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-24T12:00:00Z", "weekly"),

                // Restic lo escribió como "día 26" (UTC+9), pero son las 15:00 del 25 en el equipo.
                Snapshot("2026-11-26T05:00:00+09:00", "daily")
            };

            Assert.AreEqual(ResticBackupKind.Hourly, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_ShouldNotCountADailyThatWasLocalYesterdayEvenIfResticWroteTodaysDate()
        {
            // El equipo está en UTC-5. 17:00 UTC del 25 son las 12:00 del 25.
            var selector = CreateSelector("2026-11-25T17:00:00Z", offsetHours: -5);

            ResticSnapshot[] snapshots =
            {
                    Snapshot("2026-11-24T12:00:00Z", "weekly"),

                    // Restic lo escribió como "día 25" (UTC+9), pero son las 11:00 del 24 en el equipo.
                    Snapshot("2026-11-25T01:00:00+09:00", "daily")
                };

            Assert.AreEqual(ResticBackupKind.Daily, selector.Select(snapshots));
        }

        // ============================================================
        // FECHAS FUTURAS (EL RELOJ SE ATRASÓ)
        // ============================================================

        [TestMethod]
        [DataRow("2026-11-27T12:00:00Z")]  // Viernes de esta misma semana, todavía no llegó.
        [DataRow("2026-12-05T12:00:00Z")]  // Otra semana.
        public void Select_ShouldIgnoreAWeeklyDatedInTheFuture(string weeklyTime)
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot(weeklyTime, "weekly"),
                Snapshot("2026-11-25T03:00:00Z", "daily")
            };

            Assert.AreEqual(ResticBackupKind.Weekly, selector.Select(snapshots));
        }

        [TestMethod]
        public void Select_ShouldIgnoreADailyDatedInTheFuture()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-24T12:00:00Z", "weekly"),
                Snapshot("2026-11-26T03:00:00Z", "daily")
            };

            Assert.AreEqual(ResticBackupKind.Daily, selector.Select(snapshots));
        }

        // ============================================================
        // ORDEN Y VALIDACIÓN
        // ============================================================

        [TestMethod]
        public void Select_ShouldNotDependOnTheOrderOfTheList()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            ResticSnapshot[] snapshots =
            {
                Snapshot("2026-11-24T12:00:00Z", "weekly"),
                Snapshot("2026-11-25T03:00:00Z", "daily"),
                Snapshot("2026-11-25T09:00:00Z", "manual"),
                Snapshot("2026-11-25T10:00:00Z", "hourly")
            };

            ResticSnapshot[] reversed = snapshots.Reverse().ToArray();

            Assert.AreEqual(ResticBackupKind.Hourly, selector.Select(snapshots));
            Assert.AreEqual(ResticBackupKind.Hourly, selector.Select(reversed));
        }

        [TestMethod]
        public void Select_WithNullList_ShouldThrowArgumentNullException()
        {
            BackupKindSelector selector = CreateSelector(Wednesday);

            TestAssert.Throws<ArgumentNullException>(() => selector.Select(null!));
        }

        [TestMethod]
        public void Constructor_WithoutTimeProvider_ShouldUseTheSystemClock()
        {
            var selector = new BackupKindSelector();

            // Con la lista vacía la respuesta es la misma sea cual sea el día.
            Assert.AreEqual(ResticBackupKind.Weekly, selector.Select(Array.Empty<ResticSnapshot>()));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        // offsetHours = 0 deja el reloj en UTC; otro valor simula un equipo en esa zona.
        private static BackupKindSelector CreateSelector(string utcNow, int offsetHours = 0)
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture));

            if (offsetHours != 0)
            {
                clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone(
                    "Test zone",
                    TimeSpan.FromHours(offsetHours),
                    "Test zone",
                    "Test zone"));
            }

            return new BackupKindSelector(clock);
        }

        private static ResticSnapshot Snapshot(string time, params string[] tags)
        {
            return new ResticSnapshot(
                Id: Guid.NewGuid().ToString("N"),
                ShortId: "abcd1234",
                Time: DateTimeOffset.Parse(time, CultureInfo.InvariantCulture),
                Host: "minecraft-backup-manager",
                Paths: new[] { @"C:\Test Server" },
                Tags: tags,
                TotalBytesProcessed: null);
        }
    }
}
