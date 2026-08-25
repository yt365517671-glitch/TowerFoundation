using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TowerFoundation.Licensing;

namespace TowerFoundation.LicenseTool;

public partial class LicenseToolWindow : Window
{
    private readonly bool _rootMode = LicenseToolRole.IsRootManager;
#if TOWER_FOUNDATION_ROOT_MANAGER
    private readonly RootKeyStore _rootStore = new();
#endif
    private readonly IssuerIdentityStore _issuerStore = new();
    private readonly LicenseHistoryStore _historyStore = new();

    public LicenseToolWindow()
    {
        InitializeComponent();
        Title = _rootMode ? "塔基智设 · 根授权管理器" : "塔基智设 · 授权码生成器";
        TitleText.Text = Title;
        SubtitleText.Text = _rootMode
            ? "根密钥本机隔离 · 签发员分权 · 加密灾难恢复"
            : "完全离线签发 · 本机私钥隔离 · 客户机器绑定";
        RoleBadge.Text = _rootMode ? "制作方私有" : "签发员工具";
        RootTab.Visibility = _rootMode ? Visibility.Visible : Visibility.Collapsed;
        IssueDate.SelectedDate = DateTime.Today;
        UpdateExpiryDate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        if (string.IsNullOrWhiteSpace(LicenseTrust.RootPublicKeyBase64Url))
        {
            SetStatus("当前构建尚未写入根授权公钥。", true);
            GenerateCustomerButton.IsEnabled = false;
            IssueIssuerButton.IsEnabled = false;
            return;
        }

        IssuerCertificate? certificate = null;
        if (_issuerStore.Exists)
        {
            try
            {
                var request = _issuerStore.GetRequest();
                IssuerName.Text = request.IssuerName;
                IssuerName.IsEnabled = false;
                IssuerRequestToken.Text = request.Token;
                certificate = _issuerStore.GetCertificate();
                LocalIssuerStatus.Text = certificate is null
                    ? $"本机签发员：{request.IssuerName}（{request.IssuerId}）。请把申请码交给根授权负责人。"
                    : $"本机签发员：{certificate.IssuerName} · 永久签发资格有效。";
                if (certificate is not null) IssuerCertificateToken.Text = certificate.Token;
            }
            catch (LicenseException exception) { LocalIssuerStatus.Text = exception.Message; }
        }
        else
        {
            IssuerName.IsEnabled = true;
            LocalIssuerStatus.Text = $"本机机器码：{MachineCodeProvider.GetCurrent()}。填写姓名后创建一次申请码。";
        }

        CreateIssuerButton.IsEnabled = !_issuerStore.Exists;
        GenerateCustomerButton.IsEnabled = certificate is not null;
        PermanentCustomer.IsEnabled = certificate?.CanIssuePermanent == true;
        if (certificate?.CanIssuePermanent != true) PermanentCustomer.IsChecked = false;
        IssuerStatus.Text = certificate is null
            ? "本机尚未获得签发权限，请先完成“本机签发员”授权。"
            : $"当前签发员：{certificate.IssuerName} · 客户授权最长 {certificate.MaximumCustomerDays} 天" +
              (certificate.CanIssuePermanent ? " · 可签发永久授权" : string.Empty);

        if (!_rootMode)
        {
            SetStatus(certificate is null ? "请创建申请码并交给根授权负责人。" : "签发资格有效，可以离线生成客户授权码。");
            return;
        }

#if TOWER_FOUNDATION_ROOT_MANAGER
        if (_rootStore.Exists)
        {
            try
            {
                var key = _rootStore.LoadPrivateKey();
                CryptographicOperations.ZeroMemory(key);
                RootStatus.Text = "本机根授权私钥有效；私钥由当前 Windows 用户 DPAPI 加密保存。";
                IssueIssuerButton.IsEnabled = true;
                AuthorizeLocalButton.IsEnabled = _issuerStore.Exists;
                ExportRootBackupButton.IsEnabled = true;
                ImportRootBackupButton.IsEnabled = false;
                SetStatus("根授权管理器已就绪。请勿向客户或普通签发员分发此程序。");
            }
            catch (LicenseException exception)
            {
                RootStatus.Text = exception.Message;
                IssueIssuerButton.IsEnabled = false;
                AuthorizeLocalButton.IsEnabled = false;
                ExportRootBackupButton.IsEnabled = false;
                SetStatus(exception.Message, true);
            }
        }
        else
        {
            RootStatus.Text = "本机没有根授权私钥，只能导入与当前正式版匹配的 .tjzroot 备份。";
            IssueIssuerButton.IsEnabled = false;
            AuthorizeLocalButton.IsEnabled = false;
            ExportRootBackupButton.IsEnabled = false;
            ImportRootBackupButton.IsEnabled = true;
            SetStatus("根私钥缺失，请使用加密备份恢复。", true);
        }
#endif
    }

    private void OnCreateIssuerIdentity(object sender, RoutedEventArgs e) => TryAction("创建签发员身份失败", () =>
    {
        IssuerRequestToken.Text = _issuerStore.CreateIdentity(IssuerName.Text).Token;
        RefreshAll();
    });

    private void OnImportIssuerCertificate(object sender, RoutedEventArgs e) => TryAction("导入签发员证书失败", () =>
    {
        var certificate = _issuerStore.ImportCertificate(IssuerCertificateToken.Text);
        SetStatus($"{certificate.IssuerName} 已获得永久签发资格。");
        RefreshAll();
        Pages.SelectedIndex = 0;
    });

    private void OnOpenIssuerCertificate(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "打开塔基智设签发员证书", Filter = "签发员证书 (*.tjzissuer)|*.tjzissuer|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) TryAction("打开签发员证书失败", () => IssuerCertificateToken.Text = File.ReadAllText(dialog.FileName));
    }

    private void OnGenerateCustomerLicense(object sender, RoutedEventArgs e) => TryAction("生成客户授权失败", () =>
    {
        var certificate = _issuerStore.GetCertificate() ?? throw new LicenseException("本机尚未获得签发员证书。");
        var issuedOn = DateOnly.FromDateTime(IssueDate.SelectedDate ?? DateTime.Today);
        DateOnly? expiresOn = PermanentCustomer.IsChecked == true ? null :
            DateOnly.FromDateTime(ExpiryDate.SelectedDate ?? throw new LicenseException("请选择到期日期。"));
        var key = _issuerStore.LoadPrivateKey();
        try
        {
            var license = LicenseCryptography.IssueCustomerLicense(CustomerMachineCode.Text,
                CustomerName.Text, issuedOn, expiresOn, certificate.Token, key);
            CustomerToken.Text = license.Token;
            _historyStore.Append(license);
            SetStatus($"客户授权已生成：{license.CustomerName} · {(license.ExpiresOn?.ToString("yyyy-MM-dd") ?? "永久")}。");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    });

    private void OnIssueIssuerCertificate(object sender, RoutedEventArgs e) => IssueIssuerCertificate(false);
    private void OnAuthorizeLocalIssuer(object sender, RoutedEventArgs e)
    {
#if TOWER_FOUNDATION_ROOT_MANAGER
        TryAction("授权本机签发员失败", () =>
        {
            RootRequestToken.Text = _issuerStore.GetRequest().Token;
            IssueIssuerCertificate(true);
        });
#endif
    }

    private void IssueIssuerCertificate(bool importForLocalIssuer)
    {
#if TOWER_FOUNDATION_ROOT_MANAGER
        TryAction("签发永久签发员证书失败", () =>
        {
            if (!int.TryParse(MaximumCustomerDays.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days is < 1 or > 3660)
                throw new LicenseException("客户授权最长天数必须是1至3660的整数。");
            var request = LicenseCryptography.VerifyIssuerRequest(RootRequestToken.Text);
            var key = _rootStore.LoadPrivateKey();
            try
            {
                var certificate = LicenseCryptography.IssueIssuerCertificate(request.Token, key, days, AllowPermanent.IsChecked == true);
                RootCertificateToken.Text = certificate.Token;
                if (importForLocalIssuer) _issuerStore.ImportCertificate(certificate.Token);
                SetStatus($"已为 {request.IssuerName} 生成永久签发员证书。" + (importForLocalIssuer ? "本机已自动导入。" : string.Empty));
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            RefreshAll();
        });
#endif
    }

    private void OnExportRootBackup(object sender, RoutedEventArgs e)
    {
#if TOWER_FOUNDATION_ROOT_MANAGER
        if (!string.Equals(BackupPassword.Password, BackupPasswordConfirm.Password, StringComparison.Ordinal))
        { SetStatus("两次备份密码不一致。", true); return; }
        var dialog = new SaveFileDialog { Title = "导出塔基智设根密钥加密备份", FileName = "塔基智设根密钥备份.tjzroot", Filter = "根密钥备份 (*.tjzroot)|*.tjzroot", AddExtension = true };
        if (dialog.ShowDialog(this) == true) TryAction("导出根密钥备份失败", () =>
        {
            var path = _rootStore.ExportBackup(dialog.FileName, BackupPassword.Password);
            BackupPassword.Clear(); BackupPasswordConfirm.Clear();
            SetStatus($"已保存：{Path.GetFileName(path)}。请将文件与密码分开保管。");
        });
#endif
    }

    private void OnImportRootBackup(object sender, RoutedEventArgs e)
    {
#if TOWER_FOUNDATION_ROOT_MANAGER
        var dialog = new OpenFileDialog { Title = "导入塔基智设根密钥备份", Filter = "根密钥备份 (*.tjzroot)|*.tjzroot" };
        if (dialog.ShowDialog(this) == true) TryAction("导入根密钥备份失败", () =>
        {
            _rootStore.ImportBackup(dialog.FileName, RestorePassword.Password);
            RestorePassword.Clear(); RefreshAll();
        });
#endif
    }

    private void OnDurationChanged(object sender, SelectionChangedEventArgs e) => UpdateExpiryDate();
    private void OnLicenseDateChanged(object sender, SelectionChangedEventArgs e) => UpdateExpiryDate();
    private void UpdateExpiryDate()
    {
        if (IssueDate is null || Duration is null || ExpiryDate is null) return;
        var months = Duration.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value) ? value : 12;
        ExpiryDate.SelectedDate = (IssueDate.SelectedDate ?? DateTime.Today).AddMonths(months);
    }
    private void OnPermanentCustomerChanged(object sender, RoutedEventArgs e)
    { if (ExpiryDate is not null) ExpiryDate.IsEnabled = PermanentCustomer.IsChecked != true; }

    private void OnCopyCustomerToken(object sender, RoutedEventArgs e) => Copy(CustomerToken.Text, "客户授权码");
    private void OnSaveCustomerToken(object sender, RoutedEventArgs e) => SaveToken(CustomerToken.Text, "导出客户授权", "客户授权.tjzlic", "客户授权 (*.tjzlic)|*.tjzlic");
    private void OnCopyIssuerRequest(object sender, RoutedEventArgs e) => Copy(IssuerRequestToken.Text, "签发员申请码");
    private void OnSaveIssuerRequest(object sender, RoutedEventArgs e) => SaveToken(IssuerRequestToken.Text, "导出签发员申请", "签发员申请.tjzissuerreq", "签发员申请 (*.tjzissuerreq)|*.tjzissuerreq");
    private void OnCopyRootCertificate(object sender, RoutedEventArgs e) => Copy(RootCertificateToken.Text, "签发员证书");
    private void OnSaveRootCertificate(object sender, RoutedEventArgs e) => SaveToken(RootCertificateToken.Text, "导出签发员证书", "永久签发员证书.tjzissuer", "签发员证书 (*.tjzissuer)|*.tjzissuer");

    private void Copy(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value)) { SetStatus($"{description}尚未生成。", true); return; }
        Clipboard.SetText(value.Trim()); SetStatus($"{description}已复制。 ");
    }
    private void SaveToken(string value, string title, string fileName, string filter)
    {
        if (string.IsNullOrWhiteSpace(value)) { SetStatus("当前没有可导出的授权数据。", true); return; }
        var dialog = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter, AddExtension = true };
        if (dialog.ShowDialog(this) == true) TryAction("导出授权文件失败", () =>
        { File.WriteAllText(dialog.FileName, value.Trim(), new System.Text.UTF8Encoding(false)); SetStatus($"已保存：{Path.GetFileName(dialog.FileName)}。"); });
    }
    private void TryAction(string title, Action action)
    { try { action(); } catch (Exception exception) { SetStatus($"{title}：{exception.Message}", true); } }
    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(error ? "#FEECEC" : "#E8F2FF"));
        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(error ? "#B42318" : "#3B5F88"));
    }
}
