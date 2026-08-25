namespace TowerFoundation.Licensing;

public sealed class LicenseException : InvalidOperationException
{
    public LicenseException(string message) : base(message) { }

    public LicenseException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed record IssuerRequest(
    string IssuerId,
    string IssuerName,
    string MachineCode,
    string PublicKeyBase64Url,
    DateTimeOffset CreatedAt,
    string Token);

public sealed record IssuerCertificate(
    string IssuerId,
    string IssuerName,
    string MachineCode,
    string PublicKeyBase64Url,
    int MaximumCustomerDays,
    bool CanIssuePermanent,
    DateTimeOffset IssuedAt,
    string Token);

public sealed record CustomerLicense(
    string LicenseId,
    string MachineCode,
    string CustomerName,
    DateOnly IssuedOn,
    DateOnly? ExpiresOn,
    string IssuerId,
    string IssuerName,
    string Token);

public enum ClientLicenseStatus
{
    DevelopmentBypass,
    Missing,
    Invalid,
    ClockError,
    ClockRollback,
    Valid,
    Expiring,
    Grace,
    Expired,
    Permanent,
}

public sealed record ClientLicenseAssessment(
    ClientLicenseStatus Status,
    string Message,
    string MachineCode,
    CustomerLicense? License = null,
    int? DaysRemaining = null)
{
    public bool IsUsable =>
        Status is ClientLicenseStatus.DevelopmentBypass or
            ClientLicenseStatus.Valid or
            ClientLicenseStatus.Expiring or
            ClientLicenseStatus.Grace or
            ClientLicenseStatus.Permanent;
}

public sealed record LicenseKeyPair(byte[] PrivateKey, byte[] PublicKey);

public sealed record LicenseHistoryEntry(
    string LicenseId,
    string MachineCode,
    string CustomerName,
    string IssuedOn,
    string ExpiresOn,
    string IssuerId,
    string IssuerName,
    string CreatedAt);
