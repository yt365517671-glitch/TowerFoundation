namespace TowerFoundation.Licensing;

public static class LicenseTrust
{
    public const string RootKeyId = "TJZS-ROOT-01";

    // 仅包含可公开分发的 ECDSA P-256 根公钥；根私钥永不写入源码或发布包。
    public const string RootPublicKeyBase64Url =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEeu_Q5EHyYr1cFDhaBk3RD5wPBsYf0ClNh5ObEnWXoCk29RcO_1Dg4ZVZmDvKCtZEivm1hFZS9fLpKtSv91I38Q";
}
