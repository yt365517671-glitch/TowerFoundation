using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

public sealed class LocalApplicationSettingsService : IApplicationSettingsService
{
    private static readonly HashSet<string> SupportedDeepSeekModels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "deepseek-v4-pro",
            "deepseek-v4-flash"
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public LocalApplicationSettingsService(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TowerFoundation");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public ApplicationSettings Load()
    {
        StoredSettings stored;
        try
        {
            stored = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new()
                : new StoredSettings();
        }
        catch (JsonException)
        {
            stored = new StoredSettings();
        }

        var settings = stored.Settings ?? new ApplicationSettings();
        Normalize(settings);
        var apiKey = GetApiKey(stored);
        settings.HasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        settings.ApiKeyLastFour = settings.HasApiKey
            ? apiKey![Math.Max(0, apiKey!.Length - 4)..]
            : string.Empty;
        var visionApiKey = GetVisionApiKey(stored);
        settings.HasVisionApiKey = !string.IsNullOrWhiteSpace(visionApiKey);
        settings.VisionApiKeyLastFour = settings.HasVisionApiKey
            ? visionApiKey![Math.Max(0, visionApiKey!.Length - 4)..]
            : string.Empty;
        return settings;
    }

    public void Save(
        ApplicationSettings settings,
        string? replacementApiKey = null,
        bool clearApiKey = false,
        string? replacementVisionApiKey = null,
        bool clearVisionApiKey = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        var existing = LoadStoredSettings();
        var encryptedApiKey = existing.EncryptedApiKey;
        if (clearApiKey)
        {
            encryptedApiKey = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(replacementApiKey))
        {
            encryptedApiKey = Protect(replacementApiKey.Trim());
        }

        var encryptedVisionApiKey = existing.EncryptedVisionApiKey;
        if (clearVisionApiKey)
        {
            encryptedVisionApiKey = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(replacementVisionApiKey))
        {
            encryptedVisionApiKey = Protect(replacementVisionApiKey.Trim());
        }

        var safeSettings = new ApplicationSettings
        {
            SchemaVersion = settings.SchemaVersion,
            AiMode = settings.AiMode,
            DeepSeekBaseUrl = settings.DeepSeekBaseUrl,
            DeepSeekModel = settings.DeepSeekModel,
            VisionBaseUrl = settings.VisionBaseUrl,
            VisionModel = settings.VisionModel,
            VisionPagesPerBatch = settings.VisionPagesPerBatch,
            RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
            DefaultProjectDirectory = settings.DefaultProjectDirectory,
            DefaultExportDirectory = settings.DefaultExportDirectory,
            DefaultGeotechnicalHistoryDirectory =
                settings.DefaultGeotechnicalHistoryDirectory,
            DefaultMonitoringDrawingHistoryDirectory =
                settings.DefaultMonitoringDrawingHistoryDirectory,
            OcrStartPage = settings.OcrStartPage,
            OcrEndPage = settings.OcrEndPage
        };
        var stored = new StoredSettings
        {
            Settings = safeSettings,
            EncryptedApiKey = encryptedApiKey,
            EncryptedVisionApiKey = encryptedVisionApiKey
        };

        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(stored, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    public string? GetApiKey()
    {
        var stored = LoadStoredSettings();
        return GetApiKey(stored);
    }

    public string? GetVisionApiKey()
    {
        var stored = LoadStoredSettings();
        return GetVisionApiKey(stored);
    }

    public VisualApiKeyImportResult ImportVisualApiFromCsv(string csvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        if (!File.Exists(csvPath))
        {
            return new VisualApiKeyImportResult(false, "找不到所选业务空间 API CSV。", string.Empty);
        }

        try
        {
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            if (lines.Length < 2)
            {
                return new VisualApiKeyImportResult(false, "业务空间 API CSV 内容为空。", string.Empty);
            }

            var headers = ParseCsvLine(lines[0]);
            if (headers.Count < 2 || !headers[0].Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                return new VisualApiKeyImportResult(false, "业务空间 API CSV 表头无法识别。", string.Empty);
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var fields = ParseCsvLine(line);
                if (fields.Count >= 2 && !string.IsNullOrWhiteSpace(fields[0]))
                {
                    values[fields[0].Trim()] = fields[1].Trim();
                }
            }

            if (!values.TryGetValue("apiKey", out var apiKey) ||
                string.IsNullOrWhiteSpace(apiKey) ||
                !values.TryGetValue("openAiCompatible", out var baseUrl) ||
                !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                return new VisualApiKeyImportResult(
                    false,
                    "CSV 中缺少有效的 apiKey 或 OpenAI 兼容 HTTPS 地址。",
                    string.Empty);
            }

            var settings = Load();
            settings.AiMode = AiOperatingMode.OnlinePreferred;
            settings.VisionBaseUrl = baseUrl;
            settings.VisionModel = VisualAiModelCatalog.DefaultModel;
            Save(settings, replacementVisionApiKey: apiKey);
            return new VisualApiKeyImportResult(
                true,
                "已导入阿里云百炼视觉 API，并使用 Windows DPAPI 按当前用户加密保存。",
                headers[1]);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new VisualApiKeyImportResult(false, exception.Message, string.Empty);
        }
    }

    public LegacyApiKeyImportResult ImportFrom451BudgetAssistant()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "451BudgetAssistant",
            "ai_settings.json");
        if (!File.Exists(legacyPath))
        {
            return new LegacyApiKeyImportResult(false, "未找到既有的451预算助手 AI 设置。");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
            if (!document.RootElement.TryGetProperty(
                    "encrypted_api_key",
                    out var encryptedElement))
            {
                return new LegacyApiKeyImportResult(false, "既有 AI 设置中没有已保存密钥。");
            }

            var encryptedApiKey = encryptedElement.GetString();
            if (string.IsNullOrWhiteSpace(encryptedApiKey))
            {
                return new LegacyApiKeyImportResult(false, "既有 AI 设置中的密钥为空。");
            }

            var apiKey = Unprotect(encryptedApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new LegacyApiKeyImportResult(false, "既有密钥无法由当前 Windows 用户解密。");
            }

            var settings = Load();
            settings.AiMode = AiOperatingMode.OnlinePreferred;
            settings.DeepSeekModel = "deepseek-v4-pro";
            Save(settings, apiKey);
            return new LegacyApiKeyImportResult(
                true,
                "已从本机既有安全设置导入 DeepSeek 密钥，并使用 DPAPI 为塔基智设重新加密。");
        }
        catch (JsonException)
        {
            return new LegacyApiKeyImportResult(false, "既有 AI 设置文件格式无效。");
        }
        catch (InvalidOperationException exception)
        {
            return new LegacyApiKeyImportResult(false, exception.Message);
        }
    }

    private string? GetApiKey(StoredSettings stored)
    {
        if (!string.IsNullOrWhiteSpace(stored.EncryptedApiKey))
        {
            try
            {
                return Unprotect(stored.EncryptedApiKey);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        var environmentKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        return string.IsNullOrWhiteSpace(environmentKey) ? null : environmentKey.Trim();
    }

    private static string? GetVisionApiKey(StoredSettings stored)
    {
        if (!string.IsNullOrWhiteSpace(stored.EncryptedVisionApiKey))
        {
            try
            {
                return Unprotect(stored.EncryptedVisionApiKey);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        var environmentKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY");
        return string.IsNullOrWhiteSpace(environmentKey) ? null : environmentKey.Trim();
    }

    private StoredSettings LoadStoredSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new StoredSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<StoredSettings>(
                       File.ReadAllText(_settingsPath),
                       JsonOptions) ??
                   new StoredSettings();
        }
        catch (JsonException)
        {
            return new StoredSettings();
        }
    }

    private static void Normalize(ApplicationSettings settings)
    {
        if (settings.SchemaVersion < 4)
        {
            settings.VisionPagesPerBatch = 2;
            settings.SchemaVersion = 4;
        }
        if (settings.SchemaVersion < 5)
        {
            settings.DefaultGeotechnicalHistoryDirectory =
                ApplicationPathDefaults.ResolveGeotechnicalHistoryDirectory();
            settings.SchemaVersion = 5;
        }
        if (settings.SchemaVersion < 6)
        {
            settings.DefaultMonitoringDrawingHistoryDirectory =
                ApplicationPathDefaults.ResolveMonitoringDrawingHistoryDirectory();
            settings.SchemaVersion = 6;
        }
        settings.DeepSeekBaseUrl = string.IsNullOrWhiteSpace(settings.DeepSeekBaseUrl)
            ? "https://api.deepseek.com"
            : settings.DeepSeekBaseUrl.Trim().TrimEnd('/');
        settings.DeepSeekModel = string.IsNullOrWhiteSpace(settings.DeepSeekModel)
            ? "deepseek-v4-pro"
            : settings.DeepSeekModel.Trim();
        if (!SupportedDeepSeekModels.Contains(settings.DeepSeekModel))
        {
            settings.DeepSeekModel = "deepseek-v4-pro";
        }
        settings.VisionBaseUrl = string.IsNullOrWhiteSpace(settings.VisionBaseUrl)
            ? "https://dashscope.aliyuncs.com/compatible-mode/v1"
            : settings.VisionBaseUrl.Trim().TrimEnd('/');
        settings.VisionModel = VisualAiModelCatalog.IsSupported(settings.VisionModel)
            ? settings.VisionModel.Trim()
            : VisualAiModelCatalog.DefaultModel;
        settings.VisionPagesPerBatch = Math.Clamp(settings.VisionPagesPerBatch, 1, 6);
        settings.RequestTimeoutSeconds = Math.Clamp(settings.RequestTimeoutSeconds, 10, 180);
        settings.DefaultProjectDirectory = ApplicationPathDefaults.NormalizeDirectory(
            settings.DefaultProjectDirectory,
            ApplicationPathDefaults.ResolveProjectDirectory());
        settings.DefaultExportDirectory = ApplicationPathDefaults.NormalizeDirectory(
            settings.DefaultExportDirectory,
            ApplicationPathDefaults.ResolveExportDirectory());
        settings.DefaultGeotechnicalHistoryDirectory =
            ApplicationPathDefaults.NormalizeDirectory(
                settings.DefaultGeotechnicalHistoryDirectory,
                ApplicationPathDefaults.ResolveGeotechnicalHistoryDirectory());
        settings.DefaultMonitoringDrawingHistoryDirectory =
            ApplicationPathDefaults.NormalizeDirectory(
                settings.DefaultMonitoringDrawingHistoryDirectory,
                ApplicationPathDefaults.ResolveMonitoringDrawingHistoryDirectory());
        settings.OcrStartPage = Math.Max(1, settings.OcrStartPage);
        settings.OcrEndPage = settings.OcrEndPage <= 0
            ? 0
            : Math.Max(settings.OcrStartPage, settings.OcrEndPage);
    }

    private static string Protect(string plainText)
    {
        var input = Encoding.UTF8.GetBytes(plainText);
        var inputBlob = CreateBlob(input);
        try
        {
            if (!CryptProtectData(
                    ref inputBlob,
                    "TowerFoundation AI API key",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var outputBlob))
            {
                throw new InvalidOperationException("无法使用 Windows DPAPI 加密 API 密钥。");
            }

            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return Convert.ToBase64String(output);
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputBlob.Data);
        }
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        fields.Add(builder.ToString());
        return fields;
    }

    private static string Unprotect(string cipherText)
    {
        byte[] encrypted;
        try
        {
            encrypted = Convert.FromBase64String(cipherText);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("本机 API 密钥配置已损坏。", exception);
        }

        var inputBlob = CreateBlob(encrypted);
        try
        {
            if (!CryptUnprotectData(
                    ref inputBlob,
                    out var description,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var outputBlob))
            {
                throw new InvalidOperationException("无法使用当前 Windows 用户解密 API 密钥。");
            }

            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return Encoding.UTF8.GetString(output);
            }
            finally
            {
                LocalFree(outputBlob.Data);
                if (description != IntPtr.Zero)
                {
                    LocalFree(description);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputBlob.Data);
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private sealed class StoredSettings
    {
        public ApplicationSettings? Settings { get; set; } = new();

        public string EncryptedApiKey { get; set; } = string.Empty;

        public string EncryptedVisionApiKey { get; set; } = string.Empty;
    }
}

public sealed record LegacyApiKeyImportResult(bool Imported, string Message);

public sealed record VisualApiKeyImportResult(
    bool Imported,
    string Message,
    string WorkspaceId);
