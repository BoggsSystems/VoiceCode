namespace VoiceCode.Common.Extensions;

public static class DateTimeExtensions
{
    public static string ToRelativeTime(this DateTime dateTime)
    {
        var timeSpan = DateTime.UtcNow - dateTime;

        if (timeSpan <= TimeSpan.FromSeconds(60))
            return "just now";

        if (timeSpan <= TimeSpan.FromMinutes(60))
            return $"{timeSpan.Minutes} minute{(timeSpan.Minutes > 1 ? "s" : "")} ago";

        if (timeSpan <= TimeSpan.FromHours(24))
            return $"{timeSpan.Hours} hour{(timeSpan.Hours > 1 ? "s" : "")} ago";

        if (timeSpan <= TimeSpan.FromDays(30))
            return $"{timeSpan.Days} day{(timeSpan.Days > 1 ? "s" : "")} ago";

        if (timeSpan <= TimeSpan.FromDays(365))
        {
            var months = timeSpan.Days / 30;
            return $"{months} month{(months > 1 ? "s" : "")} ago";
        }

        var years = timeSpan.Days / 365;
        return $"{years} year{(years > 1 ? "s" : "")} ago";
    }

    public static DateTime StartOfDay(this DateTime date)
    {
        return date.Date;
    }

    public static DateTime EndOfDay(this DateTime date)
    {
        return date.Date.AddDays(1).AddTicks(-1);
    }

    public static DateTime StartOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Monday)
    {
        var diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
        return date.AddDays(-1 * diff).Date;
    }

    public static DateTime EndOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Monday)
    {
        return date.StartOfWeek(startOfWeek).AddDays(7).AddTicks(-1);
    }

    public static DateTime StartOfMonth(this DateTime date)
    {
        return new DateTime(date.Year, date.Month, 1);
    }

    public static DateTime EndOfMonth(this DateTime date)
    {
        return date.StartOfMonth().AddMonths(1).AddTicks(-1);
    }

    public static long ToUnixTimestamp(this DateTime dateTime)
    {
        return ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
    }

    public static DateTime FromUnixTimestamp(this long unixTimestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
    }

    public static bool IsWeekend(this DateTime date)
    {
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
    }

    public static bool IsToday(this DateTime date)
    {
        return date.Date == DateTime.UtcNow.Date;
    }

    public static bool IsInFuture(this DateTime date)
    {
        return date > DateTime.UtcNow;
    }

    public static bool IsInPast(this DateTime date)
    {
        return date < DateTime.UtcNow;
    }
}