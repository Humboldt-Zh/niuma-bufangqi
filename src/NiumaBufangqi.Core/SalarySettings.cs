namespace NiumaBufangqi.Core;

/// <summary>User-defined salary assumptions. Defaults are examples, not a configured salary.</summary>
public sealed record SalarySettings
{
    public decimal MonthlySalary { get; init; } = 12_000m;
    public decimal PaidDays { get; init; } = 22m;
    public string StartTime { get; init; } = "09:00";
    public string EndTime { get; init; } = "18:00";
    public bool LunchEnabled { get; init; } = true;
    public string LunchStart { get; init; } = "12:00";
    public string LunchEnd { get; init; } = "13:00";
    public int[] WorkDays { get; init; } = [1, 2, 3, 4, 5];
}

/// <summary>Estimated salary; it is not a bank balance or actual payment.</summary>
public sealed record SalarySnapshot(
    string Status,
    decimal Earned,
    decimal DailySalary,
    decimal HourlySalary,
    double PaidSeconds,
    double ElapsedPaidSeconds,
    double RemainingPaidSeconds,
    double RemainingWallSeconds,
    double Progress);

public sealed class SettingsValidationException : ArgumentException
{
    public SettingsValidationException(IReadOnlyDictionary<string, string> errors)
        : base("薪资设置无效，请检查具体字段。", "settings") => Errors = errors;

    public IReadOnlyDictionary<string, string> Errors { get; }
}
