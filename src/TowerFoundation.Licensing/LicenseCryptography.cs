using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TowerFoundation.Licensing;

public static class LicenseCryptography
{
    public const string IssuerRequestPrefix = "TJZIR1.";
    public const string IssuerCertificatePrefix = "TJZIC1.";
    public const string CustomerLicensePrefix = "TJZL1.";

    private const int MaximumTokenCharacters = 20_000;
    private const int MaximumPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static LicenseKeyPair GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new LicenseKeyPair(key.ExportPkcs8PrivateKey(), key.ExportSubjectPublicKeyInfo());
    }

    public static byte[] GetPublicKey(byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var consumed);
        if (consumed != privateKey.Length) throw new LicenseException("授权私钥格式不正确。");
        return key.ExportSubjectPublicKeyInfo();
    }

    public static IssuerRequest CreateIssuerRequest(
        string issuerName, string machineCode, byte[] issuerPrivateKey,
        string? issuerId = null, DateTimeOffset? createdAt = null)
    {
        var payload = new IssuerRequestPayload
        {
            Kind = "issuer-request",
            Version = 1,
            IssuerId = string.IsNullOrWhiteSpace(issuerId)
                ? Convert.ToHexString(RandomNumberGenerator.GetBytes(6))
                : CleanIdentifier(issuerId, "签发员编号"),
            IssuerName = CleanName(issuerName, "签发员姓名"),
            MachineCode = MachineCodeProvider.Normalize(machineCode),
            PublicKey = Base64Url.Encode(GetPublicKey(issuerPrivateKey)),
            CreatedAt = (createdAt ?? DateTimeOffset.Now).ToString("O"),
        };
        var token = CreateSignedToken(IssuerRequestPrefix, payload, issuerPrivateKey);
        return ToIssuerRequest(payload, token);
    }

    public static IssuerRequest VerifyIssuerRequest(string token)
    {
        var payload = DecodePayload<IssuerRequestPayload>(token, IssuerRequestPrefix,
            "签发员申请码", out var signature, out var payloadBytes);
        ValidateKind(payload.Kind, payload.Version, "issuer-request");
        VerifySignature(DecodePublicKey(payload.PublicKey), payloadBytes, signature,
            "签发员申请码签名无效。");
        return ToIssuerRequest(payload, token.Trim());
    }

    public static IssuerCertificate IssueIssuerCertificate(
        string requestToken, byte[] rootPrivateKey, int maximumCustomerDays = 366,
        bool canIssuePermanent = false, DateTimeOffset? issuedAt = null)
    {
        var request = VerifyIssuerRequest(requestToken);
        var payload = new IssuerCertificatePayload
        {
            Kind = "issuer-certificate",
            Version = 1,
            RootKeyId = LicenseTrust.RootKeyId,
            IssuerId = request.IssuerId,
            IssuerName = request.IssuerName,
            MachineCode = request.MachineCode,
            PublicKey = request.PublicKeyBase64Url,
            Permanent = true,
            MaximumCustomerDays = Math.Clamp(maximumCustomerDays, 1, 3660),
            CanIssuePermanent = canIssuePermanent,
            IssuedAt = (issuedAt ?? DateTimeOffset.Now).ToString("O"),
        };
        var token = CreateSignedToken(IssuerCertificatePrefix, payload, rootPrivateKey);
        return ToIssuerCertificate(payload, token);
    }

    public static IssuerCertificate VerifyIssuerCertificate(
        string token, string? trustedRootPublicKey = null)
    {
        var rootPublicKey = trustedRootPublicKey ?? LicenseTrust.RootPublicKeyBase64Url;
        if (string.IsNullOrWhiteSpace(rootPublicKey))
            throw new LicenseException("当前程序尚未配置可信根授权公钥。");
        var payload = DecodePayload<IssuerCertificatePayload>(token, IssuerCertificatePrefix,
            "签发员证书", out var signature, out var payloadBytes);
        ValidateKind(payload.Kind, payload.Version, "issuer-certificate");
        if (!string.Equals(payload.RootKeyId, LicenseTrust.RootKeyId, StringComparison.Ordinal) || !payload.Permanent)
            throw new LicenseException("签发员证书的根密钥或类型不受支持。");
        VerifySignature(Base64Url.Decode(rootPublicKey.Trim()), payloadBytes, signature,
            "签发员证书根签名无效。");
        return ToIssuerCertificate(payload, token.Trim());
    }

    public static CustomerLicense IssueCustomerLicense(
        string machineCode, string customerName, DateOnly issuedOn, DateOnly? expiresOn,
        string issuerCertificateToken, byte[] issuerPrivateKey,
        string? trustedRootPublicKey = null)
    {
        var certificate = VerifyIssuerCertificate(issuerCertificateToken, trustedRootPublicKey);
        if (!string.Equals(Base64Url.Encode(GetPublicKey(issuerPrivateKey)),
                certificate.PublicKeyBase64Url, StringComparison.Ordinal))
            throw new LicenseException("本机签发私钥与签发员证书不匹配。");
        if (expiresOn is null && !certificate.CanIssuePermanent)
            throw new LicenseException("当前签发员无权生成永久客户授权。");
        if (expiresOn is not null)
        {
            var days = expiresOn.Value.DayNumber - issuedOn.DayNumber;
            if (days < 0) throw new LicenseException("客户授权到期日期不能早于签发日期。");
            if (days > certificate.MaximumCustomerDays)
                throw new LicenseException($"客户授权期限不能超过 {certificate.MaximumCustomerDays} 天。");
        }
        var payload = new CustomerLicensePayload
        {
            Kind = "customer-license",
            Version = 1,
            LicenseId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            MachineCode = MachineCodeProvider.Normalize(machineCode),
            CustomerName = CleanName(customerName, "客户名称"),
            IssuedOn = issuedOn.ToString("yyyy-MM-dd"),
            ExpiresOn = expiresOn?.ToString("yyyy-MM-dd") ?? string.Empty,
            IssuerId = certificate.IssuerId,
            IssuerName = certificate.IssuerName,
            IssuerCertificate = certificate.Token,
        };
        var token = CreateSignedToken(CustomerLicensePrefix, payload, issuerPrivateKey);
        return ToCustomerLicense(payload, token);
    }

    public static CustomerLicense VerifyCustomerLicense(
        string token, string machineCode, string? trustedRootPublicKey = null)
    {
        var payload = DecodePayload<CustomerLicensePayload>(token, CustomerLicensePrefix,
            "客户授权码", out var signature, out var payloadBytes);
        ValidateKind(payload.Kind, payload.Version, "customer-license");
        var certificate = VerifyIssuerCertificate(
            Require(payload.IssuerCertificate, "issuerCertificate", MaximumTokenCharacters),
            trustedRootPublicKey);
        if (!string.Equals(payload.IssuerId, certificate.IssuerId, StringComparison.Ordinal) ||
            !string.Equals(payload.IssuerName, certificate.IssuerName, StringComparison.Ordinal))
            throw new LicenseException("客户授权码中的签发员身份不一致。");
        VerifySignature(DecodePublicKey(certificate.PublicKeyBase64Url), payloadBytes,
            signature, "客户授权码签名无效。");
        var license = ToCustomerLicense(payload, token.Trim());
        if (!string.Equals(license.MachineCode, MachineCodeProvider.Normalize(machineCode), StringComparison.Ordinal))
            throw new LicenseException("授权码与本机机器码不匹配。");
        if (license.ExpiresOn is null && !certificate.CanIssuePermanent)
            throw new LicenseException("签发员无权生成永久客户授权。");
        if (license.ExpiresOn is not null)
        {
            var days = license.ExpiresOn.Value.DayNumber - license.IssuedOn.DayNumber;
            if (days < 0 || days > certificate.MaximumCustomerDays)
                throw new LicenseException("客户授权期限超出签发员权限。");
        }
        return license;
    }

    private static string CreateSignedToken<T>(string prefix, T payload, byte[] privateKey)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var consumed);
        if (consumed != privateKey.Length) throw new LicenseException("授权私钥格式不正确。");
        var signature = key.SignData(payloadBytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return prefix + Base64Url.Encode(payloadBytes) + "." + Base64Url.Encode(signature);
    }

    private static T DecodePayload<T>(string token, string prefix, string description,
        out byte[] signature, out byte[] payloadBytes)
    {
        var text = string.Concat((token ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length > MaximumTokenCharacters)
            throw new LicenseException($"{description}格式不正确。");
        var sections = text[prefix.Length..].Split('.');
        if (sections.Length != 2) throw new LicenseException($"{description}格式不正确。");
        try
        {
            payloadBytes = Base64Url.Decode(sections[0]);
            signature = Base64Url.Decode(sections[1]);
            if (payloadBytes.Length == 0 || payloadBytes.Length > MaximumPayloadBytes || signature.Length != 64)
                throw new FormatException();
            return JsonSerializer.Deserialize<T>(payloadBytes, JsonOptions) ?? throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new LicenseException($"{description}损坏或格式不正确。", exception);
        }
    }

    private static void VerifySignature(byte[] publicKey, byte[] payload, byte[] signature, string errorMessage)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
            if (consumed != publicKey.Length || !key.VerifyData(payload, signature,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new LicenseException(errorMessage);
        }
        catch (CryptographicException exception) { throw new LicenseException(errorMessage, exception); }
    }

    private static IssuerRequest ToIssuerRequest(IssuerRequestPayload payload, string token)
    {
        var createdAt = DateTimeOffset.TryParse(payload.CreatedAt, out var value)
            ? value : throw new LicenseException("签发员申请时间格式不正确。");
        _ = DecodePublicKey(payload.PublicKey);
        return new IssuerRequest(CleanIdentifier(payload.IssuerId, "签发员编号"),
            CleanName(payload.IssuerName, "签发员姓名"),
            MachineCodeProvider.Normalize(payload.MachineCode), payload.PublicKey, createdAt, token);
    }

    private static IssuerCertificate ToIssuerCertificate(IssuerCertificatePayload payload, string token)
    {
        var issuedAt = DateTimeOffset.TryParse(payload.IssuedAt, out var value)
            ? value : throw new LicenseException("签发员证书签发时间格式不正确。");
        _ = DecodePublicKey(payload.PublicKey);
        if (payload.MaximumCustomerDays is < 1 or > 3660)
            throw new LicenseException("签发员证书的期限权限不正确。");
        return new IssuerCertificate(CleanIdentifier(payload.IssuerId, "签发员编号"),
            CleanName(payload.IssuerName, "签发员姓名"),
            MachineCodeProvider.Normalize(payload.MachineCode), payload.PublicKey,
            payload.MaximumCustomerDays, payload.CanIssuePermanent, issuedAt, token);
    }

    private static CustomerLicense ToCustomerLicense(CustomerLicensePayload payload, string token)
    {
        if (!DateOnly.TryParseExact(payload.IssuedOn, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var issuedOn))
            throw new LicenseException("客户授权签发日期格式不正确。");
        DateOnly? expiresOn = null;
        if (!string.IsNullOrEmpty(payload.ExpiresOn))
        {
            if (!DateOnly.TryParseExact(payload.ExpiresOn, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                throw new LicenseException("客户授权到期日期格式不正确。");
            expiresOn = parsed;
        }
        return new CustomerLicense(CleanIdentifier(payload.LicenseId, "授权编号"),
            MachineCodeProvider.Normalize(payload.MachineCode),
            CleanName(payload.CustomerName, "客户名称"), issuedOn, expiresOn,
            CleanIdentifier(payload.IssuerId, "签发员编号"),
            CleanName(payload.IssuerName, "签发员姓名"), token);
    }

    private static byte[] DecodePublicKey(string value)
    {
        var key = Base64Url.Decode(Require(value, "publicKey", 256));
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out var consumed);
            if (consumed != key.Length) throw new LicenseException("授权公钥格式不正确。");
            return key;
        }
        catch (CryptographicException exception) { throw new LicenseException("授权公钥格式不正确。", exception); }
    }

    private static void ValidateKind(string value, int version, string expected)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal) || version != 1)
            throw new LicenseException("授权数据类型或版本不受支持。");
    }

    private static string CleanName(string value, string field)
    {
        var text = string.Join(" ", (value ?? string.Empty).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 1 or > 80 || text.Any(char.IsControl))
            throw new LicenseException($"{field}格式不正确。");
        return text;
    }

    private static string CleanIdentifier(string value, string field)
    {
        var text = Require(value, field, 40);
        if (text.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new LicenseException($"{field}格式不正确。");
        return text;
    }

    private static string Require(string value, string field, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new LicenseException($"授权数据字段不正确：{field}。");
        return value;
    }

    private sealed class IssuerRequestPayload
    {
        public string Kind { get; init; } = string.Empty; public int Version { get; init; }
        public string IssuerId { get; init; } = string.Empty; public string IssuerName { get; init; } = string.Empty;
        public string MachineCode { get; init; } = string.Empty; public string PublicKey { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
    }
    private sealed class IssuerCertificatePayload
    {
        public string Kind { get; init; } = string.Empty; public int Version { get; init; }
        public string RootKeyId { get; init; } = string.Empty; public string IssuerId { get; init; } = string.Empty;
        public string IssuerName { get; init; } = string.Empty; public string MachineCode { get; init; } = string.Empty;
        public string PublicKey { get; init; } = string.Empty; public bool Permanent { get; init; }
        public int MaximumCustomerDays { get; init; } public bool CanIssuePermanent { get; init; }
        public string IssuedAt { get; init; } = string.Empty;
    }
    private sealed class CustomerLicensePayload
    {
        public string Kind { get; init; } = string.Empty; public int Version { get; init; }
        public string LicenseId { get; init; } = string.Empty; public string MachineCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty; public string IssuedOn { get; init; } = string.Empty;
        public string ExpiresOn { get; init; } = string.Empty; public string IssuerId { get; init; } = string.Empty;
        public string IssuerName { get; init; } = string.Empty; public string IssuerCertificate { get; init; } = string.Empty;
    }
}

internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new FormatException("Base64Url 编码不正确。");
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var bytes = Convert.FromBase64String(padded);
        if (!string.Equals(Encode(bytes), value, StringComparison.Ordinal))
            throw new FormatException("Base64Url 编码不规范。");
        return bytes;
    }
}
