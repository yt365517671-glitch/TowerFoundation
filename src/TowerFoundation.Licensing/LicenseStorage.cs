using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TowerFoundation.Licensing;

public static class LicenseStoragePaths
{
    public static string AuthorityDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TowerFoundation", "LicenseAuthority");

    public static string GeneratorDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TowerFoundation", "LicenseGenerator");
}

public sealed class RootKeyStore
{
    private const string Format = "tower-foundation-root-authority-key";
    private const string BackupFormat = "tower-foundation-root-key-backup";
    private readonly string _path;
    private readonly string _trustedRootPublicKey;

    public RootKeyStore(string? path = null, string? trustedRootPublicKey = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            LicenseStoragePaths.AuthorityDirectory, "root-authority.json"));
        _trustedRootPublicKey = trustedRootPublicKey ?? LicenseTrust.RootPublicKeyBase64Url;
    }

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);

    public string Create()
    {
        if (Exists) throw new LicenseException("本机根授权私钥已经存在，不能重复创建。");
        var keyPair = LicenseCryptography.GenerateKeyPair();
        try
        {
            var publicKey = Base64Url.Encode(keyPair.PublicKey);
            SavePrivateKey(keyPair.PrivateKey, publicKey);
            return publicKey;
        }
        finally { CryptographicOperations.ZeroMemory(keyPair.PrivateKey); }
    }

    public string GetPublicKey()
    {
        var record = JsonFile.Read<RootKeyRecord>(_path, "根授权私钥");
        ValidateRootRecord(record);
        return record.PublicKey;
    }

    public byte[] LoadPrivateKey()
    {
        var record = JsonFile.Read<RootKeyRecord>(_path, "根授权私钥");
        ValidateRootRecord(record);
        byte[] privateKey;
        try
        {
            privateKey = WindowsDataProtector.Unprotect(Convert.FromBase64String(record.EncryptedPrivateKey));
        }
        catch (Exception exception) when (exception is FormatException or Win32Exception or CryptographicException)
        {
            throw new LicenseException("根授权私钥无法解密。", exception);
        }
        var publicKey = Base64Url.Encode(LicenseCryptography.GetPublicKey(privateKey));
        if (!string.Equals(publicKey, record.PublicKey, StringComparison.Ordinal) || !MatchesTrustedRoot(publicKey))
        {
            CryptographicOperations.ZeroMemory(privateKey);
            throw new LicenseException("本机根授权私钥与当前程序信任的根公钥不匹配。");
        }
        return privateKey;
    }

    public string ExportBackup(string outputPath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ValidateBackupPassword(password);
        var privateKey = LoadPrivateKey();
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var derivedKey = DeriveBackupKey(password, salt);
        try
        {
            var publicKey = Base64Url.Encode(LicenseCryptography.GetPublicKey(privateKey));
            var ciphertext = new byte[privateKey.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(derivedKey, tag.Length))
                aes.Encrypt(nonce, privateKey, ciphertext, tag, Encoding.ASCII.GetBytes(publicKey));
            var backup = new RootBackupRecord
            {
                Format = BackupFormat, Version = 1, PublicKey = publicKey,
                Salt = Base64Url.Encode(salt), Nonce = Base64Url.Encode(nonce),
                Ciphertext = Base64Url.Encode(ciphertext), Tag = Base64Url.Encode(tag),
                CreatedAt = DateTimeOffset.Now.ToString("O"),
            };
            var path = outputPath.EndsWith(".tjzroot", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(outputPath) : Path.GetFullPath(outputPath + ".tjzroot");
            JsonFile.Write(path, backup);
            return path;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public string ImportBackup(string inputPath, string password)
    {
        if (Exists) throw new LicenseException("本机已经存在根授权私钥，不能覆盖导入。");
        ValidateBackupPassword(password);
        var backup = JsonFile.Read<RootBackupRecord>(inputPath, "根密钥备份");
        if (!string.Equals(backup.Format, BackupFormat, StringComparison.Ordinal) || backup.Version != 1)
            throw new LicenseException("不是受支持的塔基智设根密钥备份。");
        if (!MatchesTrustedRoot(backup.PublicKey))
            throw new LicenseException("该备份不是当前客户端体系信任的根密钥。");
        byte[] privateKey = [];
        byte[] derivedKey = [];
        try
        {
            var salt = Base64Url.Decode(backup.Salt);
            var nonce = Base64Url.Decode(backup.Nonce);
            var ciphertext = Base64Url.Decode(backup.Ciphertext);
            var tag = Base64Url.Decode(backup.Tag);
            if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16) throw new FormatException();
            derivedKey = DeriveBackupKey(password, salt);
            privateKey = new byte[ciphertext.Length];
            using (var aes = new AesGcm(derivedKey, tag.Length))
                aes.Decrypt(nonce, ciphertext, tag, privateKey, Encoding.ASCII.GetBytes(backup.PublicKey));
            if (!string.Equals(Base64Url.Encode(LicenseCryptography.GetPublicKey(privateKey)),
                    backup.PublicKey, StringComparison.Ordinal)) throw new CryptographicException();
            SavePrivateKey(privateKey, backup.PublicKey);
            return backup.PublicKey;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            throw new LicenseException("根密钥备份密码错误或文件已经损坏。", exception);
        }
        finally
        {
            if (privateKey.Length > 0) CryptographicOperations.ZeroMemory(privateKey);
            if (derivedKey.Length > 0) CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    private void SavePrivateKey(byte[] privateKey, string publicKey)
    {
        if (!MatchesTrustedRoot(publicKey))
            throw new LicenseException("准备保存的根私钥与当前程序信任的根公钥不匹配。");
        JsonFile.Write(_path, new RootKeyRecord
        {
            Format = Format, Version = 1, PublicKey = publicKey,
            EncryptedPrivateKey = Convert.ToBase64String(WindowsDataProtector.Protect(privateKey)),
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            SecretStorage = "windows-dpapi-current-user",
        });
    }

    private bool MatchesTrustedRoot(string publicKey) =>
        string.IsNullOrWhiteSpace(_trustedRootPublicKey) ||
        string.Equals(publicKey, _trustedRootPublicKey, StringComparison.Ordinal);

    private static void ValidateRootRecord(RootKeyRecord record)
    {
        if (!string.Equals(record.Format, Format, StringComparison.Ordinal) || record.Version != 1 ||
            string.IsNullOrWhiteSpace(record.PublicKey) || string.IsNullOrWhiteSpace(record.EncryptedPrivateKey))
            throw new LicenseException("根授权私钥文件格式不正确。");
    }

    private static void ValidateBackupPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            throw new LicenseException("根密钥备份密码至少需要 10 个字符。");
    }

    private static byte[] DeriveBackupKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 600_000,
            HashAlgorithmName.SHA256, 32);

    private sealed class RootKeyRecord
    {
        public string Format { get; init; } = string.Empty; public int Version { get; init; }
        public string PublicKey { get; init; } = string.Empty;
        public string EncryptedPrivateKey { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string SecretStorage { get; init; } = string.Empty;
    }
    private sealed class RootBackupRecord
    {
        public string Format { get; init; } = string.Empty; public int Version { get; init; }
        public string PublicKey { get; init; } = string.Empty; public string Salt { get; init; } = string.Empty;
        public string Nonce { get; init; } = string.Empty; public string Ciphertext { get; init; } = string.Empty;
        public string Tag { get; init; } = string.Empty; public string CreatedAt { get; init; } = string.Empty;
    }
}

public sealed class IssuerIdentityStore
{
    private const string Format = "tower-foundation-issuer-identity";
    private readonly string _path;
    private readonly string _machineCode;
    private readonly string _trustedRootPublicKey;

    public IssuerIdentityStore(string? path = null, string? machineCode = null,
        string? trustedRootPublicKey = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            LicenseStoragePaths.GeneratorDirectory, "issuer-identity.json"));
        _machineCode = MachineCodeProvider.Normalize(machineCode ?? MachineCodeProvider.GetCurrent());
        _trustedRootPublicKey = trustedRootPublicKey ?? LicenseTrust.RootPublicKeyBase64Url;
    }

    public bool Exists => File.Exists(_path);

    public IssuerRequest CreateIdentity(string issuerName)
    {
        if (Exists) throw new LicenseException("本机已经创建签发员身份。");
        var keyPair = LicenseCryptography.GenerateKeyPair();
        try
        {
            var request = LicenseCryptography.CreateIssuerRequest(issuerName, _machineCode, keyPair.PrivateKey);
            JsonFile.Write(_path, new IssuerIdentityRecord
            {
                Format = Format, Version = 1, IssuerId = request.IssuerId,
                IssuerName = request.IssuerName, MachineCode = request.MachineCode,
                PublicKey = request.PublicKeyBase64Url,
                EncryptedPrivateKey = Convert.ToBase64String(WindowsDataProtector.Protect(keyPair.PrivateKey)),
                RequestToken = request.Token, CreatedAt = request.CreatedAt.ToString("O"),
            });
            return request;
        }
        finally { CryptographicOperations.ZeroMemory(keyPair.PrivateKey); }
    }

    public IssuerRequest GetRequest()
    {
        var record = ReadRecord();
        var request = LicenseCryptography.VerifyIssuerRequest(record.RequestToken);
        if (!string.Equals(request.IssuerId, record.IssuerId, StringComparison.Ordinal) ||
            !string.Equals(request.PublicKeyBase64Url, record.PublicKey, StringComparison.Ordinal))
            throw new LicenseException("本机签发员申请码与身份文件不一致。");
        return request;
    }

    public byte[] LoadPrivateKey()
    {
        var record = ReadRecord();
        byte[] privateKey;
        try { privateKey = WindowsDataProtector.Unprotect(Convert.FromBase64String(record.EncryptedPrivateKey)); }
        catch (Exception exception) when (exception is FormatException or Win32Exception or CryptographicException)
        { throw new LicenseException("本机签发员私钥无法解密。", exception); }
        if (!string.Equals(Base64Url.Encode(LicenseCryptography.GetPublicKey(privateKey)),
                record.PublicKey, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(privateKey);
            throw new LicenseException("本机签发员私钥与身份文件不匹配。");
        }
        return privateKey;
    }

    public IssuerCertificate ImportCertificate(string token)
    {
        var certificate = LicenseCryptography.VerifyIssuerCertificate(token, _trustedRootPublicKey);
        var record = ReadRecord();
        if (!string.Equals(certificate.IssuerId, record.IssuerId, StringComparison.Ordinal) ||
            !string.Equals(certificate.PublicKeyBase64Url, record.PublicKey, StringComparison.Ordinal) ||
            !string.Equals(certificate.MachineCode, _machineCode, StringComparison.Ordinal))
            throw new LicenseException("签发员证书与本机申请身份不一致。");
        JsonFile.Write(_path, record with
        {
            CertificateToken = certificate.Token,
            AuthorizedAt = DateTimeOffset.Now.ToString("O"),
        });
        return certificate;
    }

    public IssuerCertificate? GetCertificate()
    {
        var record = ReadRecord();
        if (string.IsNullOrWhiteSpace(record.CertificateToken)) return null;
        var certificate = LicenseCryptography.VerifyIssuerCertificate(record.CertificateToken, _trustedRootPublicKey);
        if (!string.Equals(certificate.PublicKeyBase64Url, record.PublicKey, StringComparison.Ordinal) ||
            !string.Equals(certificate.MachineCode, _machineCode, StringComparison.Ordinal))
            throw new LicenseException("签发员证书与本机身份不匹配。");
        return certificate;
    }

    private IssuerIdentityRecord ReadRecord()
    {
        var record = JsonFile.Read<IssuerIdentityRecord>(_path, "签发员身份");
        if (!string.Equals(record.Format, Format, StringComparison.Ordinal) || record.Version != 1 ||
            string.IsNullOrWhiteSpace(record.IssuerId) || string.IsNullOrWhiteSpace(record.PublicKey) ||
            string.IsNullOrWhiteSpace(record.EncryptedPrivateKey) || string.IsNullOrWhiteSpace(record.RequestToken) ||
            !string.Equals(MachineCodeProvider.Normalize(record.MachineCode), _machineCode, StringComparison.Ordinal))
            throw new LicenseException("签发员身份文件格式不正确或不属于当前电脑。");
        return record;
    }

    private sealed record IssuerIdentityRecord
    {
        public string Format { get; init; } = string.Empty; public int Version { get; init; }
        public string IssuerId { get; init; } = string.Empty; public string IssuerName { get; init; } = string.Empty;
        public string MachineCode { get; init; } = string.Empty; public string PublicKey { get; init; } = string.Empty;
        public string EncryptedPrivateKey { get; init; } = string.Empty; public string RequestToken { get; init; } = string.Empty;
        public string CertificateToken { get; init; } = string.Empty; public string CreatedAt { get; init; } = string.Empty;
        public string AuthorizedAt { get; init; } = string.Empty;
    }
}

public sealed class ClientLicenseStore
{
    private readonly string _directory;
    public ClientLicenseStore(string dataDirectory) => _directory = Path.GetFullPath(dataDirectory);
    public string LicensePath => Path.Combine(_directory, "license.tjzlic");
    public string StatePath => Path.Combine(_directory, "license-state.dat");

    public string? LoadToken()
    {
        try { var token = File.ReadAllText(LicensePath, Encoding.UTF8).Trim(); return token.Length == 0 ? null : token; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    public void SaveToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Directory.CreateDirectory(_directory);
        AtomicFile.WriteText(LicensePath, token.Trim());
    }

    public DateOnly? LoadLastSeen(string licenseId)
    {
        try
        {
            var plaintext = WindowsDataProtector.Unprotect(Convert.FromBase64String(
                File.ReadAllText(StatePath, Encoding.ASCII)));
            try
            {
                var state = JsonSerializer.Deserialize<ClientStateRecord>(plaintext);
                return state is not null && string.Equals(state.LicenseId, licenseId, StringComparison.Ordinal) &&
                    DateOnly.TryParseExact(state.LastSeen, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var value) ? value : null;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch { return null; }
    }

    public void SaveLastSeen(string licenseId, DateOnly value)
    {
        Directory.CreateDirectory(_directory);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new ClientStateRecord
        { LicenseId = licenseId, LastSeen = value.ToString("yyyy-MM-dd") });
        try { AtomicFile.WriteText(StatePath, Convert.ToBase64String(WindowsDataProtector.Protect(plaintext)), Encoding.ASCII); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    private sealed class ClientStateRecord
    { public string LicenseId { get; init; } = string.Empty; public string LastSeen { get; init; } = string.Empty; }
}

public sealed class LicenseHistoryStore
{
    private readonly string _path;
    public LicenseHistoryStore(string? path = null) => _path = Path.GetFullPath(path ?? Path.Combine(
        LicenseStoragePaths.GeneratorDirectory, "license-history.json"));
    public void Append(CustomerLicense license)
    {
        List<LicenseHistoryEntry> history;
        try { history = JsonSerializer.Deserialize<List<LicenseHistoryEntry>>(
            File.ReadAllText(_path, Encoding.UTF8)) ?? []; }
        catch { history = []; }
        history.Add(new LicenseHistoryEntry(license.LicenseId, license.MachineCode,
            license.CustomerName, license.IssuedOn.ToString("yyyy-MM-dd"),
            license.ExpiresOn?.ToString("yyyy-MM-dd") ?? "永久", license.IssuerId,
            license.IssuerName, DateTimeOffset.Now.ToString("O")));
        JsonFile.Write(_path, history.TakeLast(10_000).ToArray());
    }
}

internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static T Read<T>(string path, string description)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.GetFullPath(path), Encoding.UTF8), Options)
            ?? throw new JsonException(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { throw new LicenseException($"无法读取{description}文件。", exception); }
    }
    public static void Write<T>(string path, T value) => AtomicFile.WriteText(Path.GetFullPath(path),
        JsonSerializer.Serialize(value, Options));
}

internal static class AtomicFile
{
    public static void WriteText(string path, string content, Encoding? encoding = null)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("目标文件没有有效目录。"));
        var temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, content, encoding ?? new UTF8Encoding(false));
        File.Move(temporary, fullPath, overwrite: true);
    }
}
