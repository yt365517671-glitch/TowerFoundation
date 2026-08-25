using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TowerFoundation.Licensing;

public static partial class MachineCodeProvider
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GetCurrent()
    {
        var sources = OperatingSystem.IsWindows() ? GetWindowsSources() : [];
        if (sources.Count == 0)
        {
            sources.Add($"node:{Environment.MachineName}");
            sources.Add($"platform:{RuntimeInformation.OSDescription}");
        }
        return FromStableSources(sources);
    }

    public static string FromStableSources(IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var normalized = sources
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("机器码来源不能为空。", nameof(sources));
        }

        var material = "TowerFoundationMachineV1|" + string.Join("|", normalized);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var body = ToBase32(digest.AsSpan(0, 16));
        return "TJSM-" + string.Join("-", Enumerable.Range(0, 6).Select(index =>
        {
            var start = index * 5;
            return body.Substring(start, Math.Min(5, body.Length - start));
        }));
    }

    public static string Normalize(string value)
    {
        var compact = new string((value ?? string.Empty)
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (!compact.StartsWith("TJSM", StringComparison.Ordinal) || compact.Length != 30)
        {
            throw new LicenseException("机器码格式不正确。");
        }
        var body = compact[4..];
        if (body.Any(character => Alphabet.IndexOf(character) < 0))
        {
            throw new LicenseException("机器码格式不正确。");
        }
        var result = "TJSM-" + string.Join("-", Enumerable.Range(0, 6).Select(index =>
        {
            var start = index * 5;
            return body.Substring(start, Math.Min(5, body.Length - start));
        }));
        if (!MachineCodePattern().IsMatch(result))
        {
            throw new LicenseException("机器码格式不正确。");
        }
        return result;
    }

    private static List<string> GetWindowsSources()
    {
        var sources = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            return sources;
        }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography", writable: false);
            var machineGuid = key?.GetValue("MachineGuid")?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(machineGuid))
            {
                sources.Add($"machine-guid:{machineGuid.ToLowerInvariant()}");
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            if (GetVolumeInformation(root, null, 0, out var serial, out _, out _, null, 0))
            {
                sources.Add($"system-volume:{serial:x8}");
            }
        }
        catch
        {
        }
        return sources;
    }

    private static string ToBase32(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }
        return output.ToString();
    }

    [GeneratedRegex(@"^TJSM-[A-Z2-7]{5}(?:-[A-Z2-7]{5}){4}-[A-Z2-7]$")]
    private static partial Regex MachineCodePattern();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
