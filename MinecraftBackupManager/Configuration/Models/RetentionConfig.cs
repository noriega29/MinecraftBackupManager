namespace MinecraftBackupManager.Configuration.Models
{
    public sealed class RetentionConfig
    {
        public const int DefaultHourlyCount = 6;
        public const int DefaultDailyCount = 7;
        public const int DefaultWeeklyCount = 4;

        public const int MinimumCount = 1;
        public const int MaximumHourlyCount = 24;
        public const int MaximumDailyCount = 7;
        public const int MaximumWeeklyCount = 4;

        public int HourlyCount { get; set; } = DefaultHourlyCount;
        public int DailyCount { get; set; } = DefaultDailyCount;
        public int WeeklyCount { get; set; } = DefaultWeeklyCount;
    }
}
