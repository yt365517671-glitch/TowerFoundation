using System.Security.Cryptography;
using TowerFoundation.Licensing;

if (args.Length == 3 && args[0].Equals("issue-smoke-license", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var rootPrivateKey = new RootKeyStore().LoadPrivateKey();
        var issuer = LicenseCryptography.GenerateKeyPair();
        try
        {
            var machineCode = MachineCodeProvider.Normalize(args[2]);
            var request = LicenseCryptography.CreateIssuerRequest(
                "发布验证签发员", MachineCodeProvider.GetCurrent(), issuer.PrivateKey);
            var certificate = LicenseCryptography.IssueIssuerCertificate(
                request.Token, rootPrivateKey, maximumCustomerDays: 90);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var license = LicenseCryptography.IssueCustomerLicense(
                machineCode, "塔基智设发布启动验证", today, today.AddDays(30),
                certificate.Token, issuer.PrivateKey);
            var outputPath = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, license.Token, new System.Text.UTF8Encoding(false));
            Console.WriteLine("PASS 已生成临时机器绑定发布验证授权。");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPrivateKey);
            CryptographicOperations.ZeroMemory(issuer.PrivateKey);
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

if (args.Length == 2 && args[0].Equals("initialize", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var store = new RootKeyStore(trustedRootPublicKey: string.Empty);
        var publicKey = store.Exists ? store.GetPublicKey() : store.Create();
        var outputPath = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, publicKey, new System.Text.UTF8Encoding(false));
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

if (args.Length == 1 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var privateKey = new RootKeyStore().LoadPrivateKey();
        CryptographicOperations.ZeroMemory(privateKey);
        Console.WriteLine("PASS 塔基智设根授权与内置公钥一致。");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

Console.Error.WriteLine("Usage: initialize <public-key-output> | verify | issue-smoke-license <output> <machine-code>");
return 2;
