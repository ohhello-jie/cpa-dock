using System.Text;
using CPALauncher.Models;

namespace CPALauncher.Services;

public sealed class CpaConfigInspector
{
    private const int DefaultPort = 8317;

    public CpaRuntimeInfo Inspect(string executablePath, string configPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("未提供 cli-proxy-api.exe 路径。", nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("未提供 config.yaml 路径。", nameof(configPath));
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("找不到 cli-proxy-api.exe。", executablePath);
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("找不到 config.yaml。", configPath);
        }

        var executableFullPath = Path.GetFullPath(executablePath);
        var configFullPath = Path.GetFullPath(configPath);
        var executableDirectory = Path.GetDirectoryName(executableFullPath)
            ?? throw new InvalidOperationException("无法解析 exe 所在目录。");
        var configDirectory = Path.GetDirectoryName(configFullPath)
            ?? throw new InvalidOperationException("无法解析配置文件所在目录。");

        var scalarValues = ParseScalarValues(File.ReadAllText(configFullPath));

        var bindHost = GetString(scalarValues, "host");
        var accessHost = NormalizeAccessHost(bindHost);
        var port = GetInt(scalarValues, "port") ?? DefaultPort;
        var useTls = GetBool(scalarValues, "tls.enable") ?? false;
        var authDirectory = ExpandHomeDirectory(GetString(scalarValues, "auth-dir"));
        var loggingToFile = GetBool(scalarValues, "logging-to-file") ?? false;
        var usageStatisticsEnabled = GetBool(scalarValues, "usage-statistics-enabled") ?? false;
        var controlPanelDisabled = GetBool(scalarValues, "remote-management.disable-control-panel") ?? false;
        var managementSecretConfigured = !string.IsNullOrWhiteSpace(GetString(scalarValues, "remote-management.secret-key"));

        var scheme = useTls ? "https" : "http";
        var baseUrl = $"{scheme}://{accessHost}:{port}";
        var logDirectory = ResolveLogDirectory(executableDirectory, authDirectory);

        return new CpaRuntimeInfo
        {
            ExecutablePath = executableFullPath,
            ExecutableDirectory = executableDirectory,
            ConfigPath = configFullPath,
            ConfigDirectory = configDirectory,
            BindHost = string.IsNullOrWhiteSpace(bindHost) ? "全部网卡" : bindHost,
            AccessHost = accessHost,
            Port = port,
            UseTls = useTls,
            LoggingToFile = loggingToFile,
            UsageStatisticsEnabled = usageStatisticsEnabled,
            ControlPanelDisabled = controlPanelDisabled,
            ManagementSecretConfigured = managementSecretConfigured,
            AuthDirectory = authDirectory,
            LogDirectory = logDirectory,
            BaseUrl = baseUrl,
            ManagementUrl = $"{baseUrl}/management.html#/login",
            ProbeUrl = $"{baseUrl}/management.html",
        };
    }

    private static string ResolveLogDirectory(string executableDirectory, string? authDirectory)
    {
        var writablePath = Environment.GetEnvironmentVariable("WRITABLE_PATH");
        if (string.IsNullOrWhiteSpace(writablePath))
        {
            writablePath = Environment.GetEnvironmentVariable("writable_path");
        }

        if (!string.IsNullOrWhiteSpace(writablePath))
        {
            return Path.Combine(Path.GetFullPath(writablePath), "logs");
        }

        var localLogsPath = Path.Combine(executableDirectory, "logs");
        if (Directory.Exists(localLogsPath) && IsDirectoryWritable(localLogsPath))
        {
            return localLogsPath;
        }

        if (!string.IsNullOrWhiteSpace(authDirectory))
        {
            return Path.Combine(authDirectory, "logs");
        }

        return localLogsPath;
    }

    private static bool IsDirectoryWritable(string path)
    {
        try
        {
            var probeFile = Path.Combine(path, $".launcher-perm-{Guid.NewGuid():N}.tmp");
            using var stream = File.Create(probeFile);
            stream.Close();
            File.Delete(probeFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeAccessHost(string? bindHost)
    {
        if (string.IsNullOrWhiteSpace(bindHost))
        {
            return "127.0.0.1";
        }

        var value = bindHost.Trim().Trim('"', '\'');
        return value switch
        {
            "0.0.0.0" => "127.0.0.1",
            "::" => "127.0.0.1",
            "[::]" => "127.0.0.1",
            _ => value,
        };
    }

    private static string? ExpandHomeDirectory(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var value = input.Trim();
        if (!value.StartsWith('~'))
        {
            return Path.GetFullPath(value);
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var remainder = value.TrimStart('~').TrimStart('/', '\\');
        return string.IsNullOrWhiteSpace(remainder)
            ? homeDirectory
            : Path.GetFullPath(Path.Combine(homeDirectory, remainder));
    }

    private static string? GetString(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static int? GetInt(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && int.TryParse(value, out var number) ? number : null;

    private static bool? GetBool(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && bool.TryParse(value, out var boolean) ? boolean : null;

    private static Dictionary<string, string> ParseScalarValues(string yamlText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(int Indent, string Key)>();

        foreach (var rawLine in ReadLines(yamlText))
        {
            var lineWithoutComment = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(lineWithoutComment))
            {
                continue;
            }

            var indent = CountLeadingSpaces(lineWithoutComment);
            var trimmed = lineWithoutComment.Trim();
            if (trimmed.StartsWith('-'))
            {
                continue;
            }

            var colonIndex = FindColonIndex(trimmed);
            if (colonIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..colonIndex].Trim();
            var value = trimmed[(colonIndex + 1)..].Trim();

            while (stack.Count > 0 && indent <= stack.Peek().Indent)
            {
                stack.Pop();
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                stack.Push((indent, key));
                continue;
            }

            var pathSegments = stack.Reverse().Select(item => item.Key).Append(key);
            var path = string.Join('.', pathSegments);
            result[path] = Unquote(value);
        }

        return result;
    }

    private static IEnumerable<string> ReadLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        foreach (var character in line)
        {
            if (character != ' ')
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static int FindColonIndex(string line)
    {
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            switch (character)
            {
                case '\'' when !inDoubleQuotes:
                    inSingleQuotes = !inSingleQuotes;
                    break;
                case '"' when !inSingleQuotes:
                    inDoubleQuotes = !inDoubleQuotes;
                    break;
                case ':' when !inSingleQuotes && !inDoubleQuotes:
                    return index;
            }
        }

        return -1;
    }

    private static string StripComment(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            switch (character)
            {
                case '\'' when !inDoubleQuotes:
                    inSingleQuotes = !inSingleQuotes;
                    builder.Append(character);
                    break;
                case '"' when !inSingleQuotes:
                    inDoubleQuotes = !inDoubleQuotes;
                    builder.Append(character);
                    break;
                case '#' when !inSingleQuotes && !inDoubleQuotes:
                    return builder.ToString().TrimEnd();
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }
}
