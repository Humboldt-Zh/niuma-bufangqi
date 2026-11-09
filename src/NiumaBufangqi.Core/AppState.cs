namespace NiumaBufangqi.Core;

/// <summary>Only durable, local application preferences. No thoughts or emotional diary are stored.</summary>
public sealed record AppState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public SalarySettings? Settings { get; init; }
    public bool Privacy { get; init; }
    public bool StartWithWindows { get; init; }
    public bool WidgetVisible { get; init; } = true;
    public List<BreakRecord> BreakSessions { get; init; } = [];
}

public sealed record BreakRecord
{
    public double Seconds { get; init; }
    public decimal Amount { get; init; }

    // Opaque day key from the front end. In v1 its month is zero-based; do not reinterpret it.
    public string Date { get; init; } = "";
    public string At { get; init; } = "";
    public string Source { get; init; } = "real";
}

public sealed class UnsupportedStateVersionException : IOException
{
    public UnsupportedStateVersionException(int version)
        : base($"配置版本 {version} 不受此版本软件支持。请使用匹配的软件版本，原配置未被覆盖。") => Version = version;

    public int Version { get; }
}
