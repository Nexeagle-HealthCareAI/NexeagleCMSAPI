using System;

namespace CMSAPI.Application.Models;

/// <summary>
/// Insights date filters and daily trends use India Standard Time days (UTC+05:30, no daylight
/// saving). Tracking rows are stored in UTC, so a "19 Oct" filter means 18 Oct 18:30 UTC up to,
/// but not including, 19 Oct 18:30 UTC.
/// </summary>
public static class TrackingTime
{
    public static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

    /// <summary>UTC instant at which the IST day <paramref name="day"/> starts.</summary>
    public static DateTime StartUtc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue) - IstOffset;

    /// <summary>UTC instant at which the IST day after <paramref name="day"/> starts (exclusive bound).</summary>
    public static DateTime EndUtcExclusive(DateOnly day) => StartUtc(day.AddDays(1));

    public static DateOnly IstDate(DateTime utc) => DateOnly.FromDateTime(utc + IstOffset);

    public static DateOnly TodayIst() => IstDate(DateTime.UtcNow);
}
