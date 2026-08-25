using System.IO;
using System.Text;
using TowerFoundation.Infrastructure;
using TowerFoundation.Licensing;

namespace TowerFoundation.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ClientLicenseManager? licenseManager = null;
        if (AppBuildProfile.RequiresLicense)
        {
            licenseManager = new ClientLicenseManager(
                new ClientLicenseStore(AppDataPaths.ResolveLicenseDirectory()));
        }

        var mainWindow = new MainWindow(licenseManager);
        MainWindow = mainWindow;
        mainWindow.Show();
        if (licenseManager is not null && !licenseManager.Assess().IsUsable)
        {
            _ = Dispatcher.BeginInvoke(
                () => mainWindow.ShowLicenseActivation(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    internal static int RunPublishedOcrSelfTest(string resultPath)
    {
        try
        {
            var workingDirectory =
                Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(resultPath))!,
                    "tower-foundation-published-ocr");
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(resultPath, "SELF_TEST_STARTED", Encoding.UTF8);
            var pdfPath = Path.Combine(workingDirectory, "ocr-sample.pdf");
            File.WriteAllBytes(
                pdfPath,
                BuildSimplePdf("Bearing capacity 180 kPa"));
            File.WriteAllText(resultPath, "PDF_CREATED", Encoding.UTF8);
            var ocrService = new LocalPdfOcrService(
                Path.Combine(workingDirectory, "tessdata"));
            var result = Task.Run(
                    () => ocrService.ExtractAsync(pdfPath))
                .GetAwaiter()
                .GetResult();
            File.WriteAllText(
                resultPath,
                result.Content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return result.Content.Contains("180", StringComparison.Ordinal)
                ? 0
                : 2;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                resultPath,
                exception.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return 1;
        }
    }

    private static byte[] BuildSimplePdf(string text)
    {
        var content = $"BT /F1 30 Tf 72 650 Td ({text}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1)
                .Append(" 0 obj\n")
                .Append(objects[index])
                .Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ")
            .Append(objects.Length + 1)
            .Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10"))
                .Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ")
            .Append(objects.Length + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset)
            .Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
