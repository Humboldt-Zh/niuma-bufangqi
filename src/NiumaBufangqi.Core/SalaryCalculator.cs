using System.Collections.ObjectModel;
using System.Globalization;

namespace NiumaBufangqi.Core;

/// <summary>Pure calculations in the device's local wall-clock time, with no I/O.</summary>
public static class SalaryCalculator
{
    public static IReadOnlyDictionary<string, string> Validate(SalarySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new Dictionary<string, string>();
        if (settings.MonthlySalary is <= 0 or > 10_000_000)
            errors["monthlySalary"] = "月薪需大于 0 且不超过 10,000,000 元。";
        if (settings.PaidDays is <= 0 or > 31)
            errors["paidDays"] = "月计薪天数需大于 0 且不超过 31 天。";

        var validStart = TrySeconds(settings.StartTime, out var start);
        var validEnd = TrySeconds(settings.EndTime, out var end);
        if (!validStart) errors["startTime"] = "请使用 HH:mm 格式填写上班时间。";
        if (!validEnd) errors["endTime"] = "请使用 HH:mm 格式填写下班时间。";
        if (validStart && validEnd && end <= start)
            errors["endTime"] = "下班时间需晚于上班时间，暂不支持跨夜班。";

        if (settings.LunchEnabled)
        {
            var validLunchStart = TrySeconds(settings.LunchStart, out var lunchStart);
            var validLunchEnd = TrySeconds(settings.LunchEnd, out var lunchEnd);
            if (!validLunchStart) errors["lunchStart"] = "请使用 HH:mm 格式填写午休开始时间。";
            if (!validLunchEnd) errors["lunchEnd"] = "请使用 HH:mm 格式填写午休结束时间。";
            if (validLunchStart && validLunchEnd)
            {
                if (lunchEnd <= lunchStart) errors["lunchEnd"] = "午休结束时间需晚于开始时间。";
                if (validStart && lunchStart < start) errors["lunchStart"] = "午休需在工作时段内。";
                if (validEnd && lunchEnd > end) errors["lunchEnd"] = "午休需在工作时段内。";
                if (validStart && validEnd && end > start && lunchEnd - lunchStart >= end - start)
                    errors["lunchEnd"] = "午休扣除后需保留有效计薪时间。";
            }
        }

        if (settings.WorkDays is not { Length: > 0 } || settings.WorkDays.Any(day => day is < 0 or > 6))
            errors["workDays"] = "请至少选择一个有效工作日。";
        if (errors.Count == 0)
        {
            var netSeconds = end - start;
            if (settings.LunchEnabled)
            {
                TrySeconds(settings.LunchStart, out var lunchStart);
                TrySeconds(settings.LunchEnd, out var lunchEnd);
                netSeconds -= lunchEnd - lunchStart;
            }
            try
            {
                _ = settings.MonthlySalary / settings.PaidDays;
                _ = settings.MonthlySalary * 3600m / (settings.PaidDays * netSeconds);
            }
            catch (OverflowException) { errors["paidDays"] = "月计薪天数过小，超出可计算金额范围。"; }
        }
        return new ReadOnlyDictionary<string, string>(errors);
    }

    public static SalarySnapshot Calculate(SalarySettings settings, DateTime now)
    {
        var errors = Validate(settings);
        if (errors.Count > 0) throw new SettingsValidationException(errors);
        TrySeconds(settings.StartTime, out var start);
        TrySeconds(settings.EndTime, out var end);
        var lunchStart = end;
        var lunchEnd = end;
        if (settings.LunchEnabled)
        {
            TrySeconds(settings.LunchStart, out lunchStart);
            TrySeconds(settings.LunchEnd, out lunchEnd);
        }

        var paidSeconds = end - start - (lunchEnd - lunchStart);
        var currentSecond = now.TimeOfDay.Ticks / (decimal)TimeSpan.TicksPerSecond;
        var isWorkday = settings.WorkDays.Contains((int)now.DayOfWeek);
        var dailySalary = settings.MonthlySalary / settings.PaidDays;
        var status = "rest";
        decimal elapsed = 0;
        if (isWorkday)
        {
            status = currentSecond < start ? "before" : currentSecond >= end ? "after"
                : settings.LunchEnabled && currentSecond >= lunchStart && currentSecond < lunchEnd ? "lunch" : "working";
            var cappedNow = Math.Clamp(currentSecond, start, end);
            var elapsedLunch = settings.LunchEnabled ? Math.Max(0, Math.Min(cappedNow, lunchEnd) - lunchStart) : 0;
            elapsed = Math.Clamp(cappedNow - start - elapsedLunch, 0, paidSeconds);
        }

        var progress = elapsed / paidSeconds;
        return new SalarySnapshot(
            status,
            Money(settings.MonthlySalary * elapsed / (settings.PaidDays * paidSeconds)),
            Money(dailySalary),
            Money(settings.MonthlySalary * 3600m / (settings.PaidDays * paidSeconds)),
            paidSeconds,
            (double)elapsed,
            isWorkday ? (double)(paidSeconds - elapsed) : 0,
            isWorkday ? (double)Math.Max(0, end - currentSecond) : 0,
            (double)progress);
    }

    internal static bool TrySeconds(string? value, out int seconds)
    {
        seconds = 0;
        if (value is not { Length: 5 } || value[2] != ':' ||
            value.Where((_, index) => index != 2).Any(character => character is < '0' or > '9') ||
            !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;
        seconds = time.Hour * 3600 + time.Minute * 60;
        return true;
    }

    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
