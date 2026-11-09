using System.Text.Json;
using System.Text.RegularExpressions;

namespace NiumaBufangqi.Core;

/// <summary>Versioned local JSON, saved atomically on the same volume with one recovery backup.</summary>
public sealed partial class LocalStateStore
{
    private const long MaximumFileBytes = 1_048_576;
    private readonly object gate = new();
    private readonly string directory;
    private readonly string path;
    private readonly string backupPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        MaxDepth = 16
    };

    public LocalStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        path = Path.Combine(this.directory, "state.json");
        backupPath = path + ".bak";
    }

    public string? LastWarning { get; private set; }

    public AppState Load()
    {
        lock (gate)
        {
            LastWarning = null;
            var main = TryRead(path, out var mainError, out var mainWarning);
            if (main is not null)
            {
                LastWarning = mainWarning;
                return main;
            }
            var backup = TryRead(backupPath, out var backupError, out var backupWarning);
            if (backup is not null)
            {
                LastWarning = JoinWarnings("主配置不可用，已读取本地备份。原文件保持不变。", backupWarning);
                return backup;
            }
            if (mainError is not null || backupError is not null)
                LastWarning = "本地配置无法读取，暂使用未配置状态。原文件保留；保存新配置时会另存损坏副本。";
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (gate)
        {
            var normalized = Normalize(state, rejectInvalidRecords: true, out _);
            // Reading first rejects newer schemas even when the caller never called Load().
            var existing = TryRead(path, out _, out _);
            if (File.Exists(backupPath)) TryRead(backupPath, out _, out _);
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".state-{Guid.NewGuid():N}.tmp");
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, options: FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (!File.Exists(path)) File.Move(temporaryPath, path);
                else if (existing is not null) File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
                else
                {
                    var damagedCopy = Path.Combine(directory, $"state.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                    File.Replace(temporaryPath, path, damagedCopy, ignoreMetadataErrors: true);
                    LastWarning = "已保存新配置，并保留损坏的原配置副本。";
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }

    private static AppState? TryRead(string file, out string? error, out string? warning)
    {
        error = null;
        warning = null;
        if (!File.Exists(file)) return null;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("配置文件超过允许大小。");
            var state = JsonSerializer.Deserialize<AppState>(stream, JsonOptions)
                ?? throw new InvalidDataException("配置内容为空。");
            return Normalize(state, rejectInvalidRecords: false, out warning);
        }
        catch (UnsupportedStateVersionException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or SettingsValidationException)
        {
            error = exception.Message;
            return null;
        }
    }

    private static AppState Normalize(AppState state, bool rejectInvalidRecords, out string? warning)
    {
        warning = null;
        if (state.SchemaVersion != AppState.CurrentSchemaVersion)
            throw new UnsupportedStateVersionException(state.SchemaVersion);
        if (state.Settings is not null)
        {
            var errors = SalaryCalculator.Validate(state.Settings);
            if (errors.Count > 0) throw new SettingsValidationException(errors);
        }
        var records = new List<BreakRecord>();
        var rejected = 0;
        foreach (var record in state.BreakSessions ?? [])
        {
            if (record is null || !double.IsFinite(record.Seconds) || record.Seconds < 0 ||
                record.Amount < 0 || record.Date is null || !DayKeyPattern().IsMatch(record.Date) ||
                !SalaryCalculator.TrySeconds(record.At, out _) || record.Source is not ("real" or "demo"))
            {
                rejected++;
                continue;
            }
            if (records.Count < 100) records.Add(record with { });
        }
        if (rejectInvalidRecords && rejected > 0) throw new InvalidDataException("休息记录包含无效字段，未保存。");
        if (rejected > 0) warning = $"已跳过 {rejected} 条无效休息记录。原配置未改写。";
        var settings = state.Settings is null ? null : state.Settings with { WorkDays = state.Settings.WorkDays.Distinct().Order().ToArray() };
        return state with { Settings = settings, BreakSessions = records };
    }

    private static string JoinWarnings(string first, string? second) => second is null ? first : first + second;

    [GeneratedRegex(@"^\d{4}-\d{1,2}-\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DayKeyPattern();
}
