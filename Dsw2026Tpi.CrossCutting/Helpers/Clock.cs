namespace Dsw2026Tpi.CrossCutting.Helpers;

public static class Clock
{
    private static readonly TimeZoneInfo ArgentinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
    public static DateTime Now =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ArgentinaTimeZone),
            DateTimeKind.Utc);

    public static DateTime Today => Now.Date;
}