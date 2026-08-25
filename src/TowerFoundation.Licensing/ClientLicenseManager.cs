namespace TowerFoundation.Licensing;

public sealed class ClientLicenseManager
{
    public const int GraceDays = 15;
    public const int ReminderDays = 30;
    private readonly ClientLicenseStore _store;
    private readonly string _trustedRootPublicKey;

    public ClientLicenseManager(ClientLicenseStore store, string? machineCode = null,
        string? trustedRootPublicKey = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        MachineCode = MachineCodeProvider.Normalize(machineCode ?? MachineCodeProvider.GetCurrent());
        _trustedRootPublicKey = trustedRootPublicKey ?? LicenseTrust.RootPublicKeyBase64Url;
    }
    public string MachineCode { get; }

    public ClientLicenseAssessment Assess(DateOnly? today = null)
    {
        var current = today ?? DateOnly.FromDateTime(DateTime.Today);
        var token = _store.LoadToken();
        if (string.IsNullOrWhiteSpace(token))
            return new(ClientLicenseStatus.Missing,
                "本机尚未授权。可以浏览软件，但正式计算、AI、保存和导出需要先输入授权码。", MachineCode);
        CustomerLicense license;
        try { license = LicenseCryptography.VerifyCustomerLicense(token, MachineCode, _trustedRootPublicKey); }
        catch (LicenseException exception) { return new(ClientLicenseStatus.Invalid, exception.Message, MachineCode); }
        if (current < license.IssuedOn)
            return new(ClientLicenseStatus.ClockError,
                $"当前系统日期早于授权签发日期 {license.IssuedOn:yyyy-MM-dd}，请校准电脑时间。", MachineCode, license);
        var lastSeen = _store.LoadLastSeen(license.LicenseId);
        if (lastSeen is not null && current < lastSeen.Value.AddDays(-1))
            return new(ClientLicenseStatus.ClockRollback,
                $"检测到系统日期从 {lastSeen:yyyy-MM-dd} 回退，请校准电脑时间。", MachineCode, license);
        if (lastSeen is null || current > lastSeen.Value) _store.SaveLastSeen(license.LicenseId, current);
        if (license.ExpiresOn is null)
            return new(ClientLicenseStatus.Permanent, $"永久授权 · {license.CustomerName}", MachineCode, license);
        var remaining = license.ExpiresOn.Value.DayNumber - current.DayNumber;
        if (remaining >= 0)
        {
            var status = remaining <= ReminderDays ? ClientLicenseStatus.Expiring : ClientLicenseStatus.Valid;
            return new(status, status == ClientLicenseStatus.Expiring
                ? $"授权将于 {license.ExpiresOn:yyyy-MM-dd} 到期，剩余 {remaining} 天。"
                : $"授权有效期至 {license.ExpiresOn:yyyy-MM-dd}。", MachineCode, license, remaining);
        }
        var overdue = -remaining;
        if (overdue <= GraceDays)
            return new(ClientLicenseStatus.Grace,
                $"授权已到期，当前处于第 {overdue}/{GraceDays} 天宽限期。", MachineCode, license, remaining);
        return new(ClientLicenseStatus.Expired,
            $"授权已于 {license.ExpiresOn:yyyy-MM-dd} 到期，请导入新授权码。", MachineCode, license, remaining);
    }

    public ClientLicenseAssessment Activate(string token, DateOnly? today = null)
    {
        var current = today ?? DateOnly.FromDateTime(DateTime.Today);
        var license = LicenseCryptography.VerifyCustomerLicense(token.Trim(), MachineCode, _trustedRootPublicKey);
        if (current < license.IssuedOn) throw new LicenseException("当前系统日期早于授权签发日期。");
        if (license.ExpiresOn is not null && current > license.ExpiresOn.Value.AddDays(GraceDays))
            throw new LicenseException("该授权码已经超过到期日及宽限期，不能用于激活。");
        _store.SaveToken(token.Trim());
        var result = Assess(current);
        if (!result.IsUsable) throw new LicenseException(result.Message);
        return result;
    }

    public ClientLicenseAssessment ActivateFromFile(string path, DateOnly? today = null)
    {
        try { return Activate(File.ReadAllText(Path.GetFullPath(path), System.Text.Encoding.UTF8), today); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        { throw new LicenseException("无法读取授权文件。", exception); }
    }
}
