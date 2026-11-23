using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Security;
using System.Runtime.InteropServices;
using System.Text;

static class InstallPaths
{
    internal const string Weights = "LFM3-1.7B-Q4_K_M.gguf";
    internal const string SettingsFile = "install.paths";

    internal static string Find(string root, string name)
    {
        var settings = Path.Combine(root, SettingsFile);
        if (!File.Exists(settings)) return null;
        var prefix = name + "=";
        var line = File.ReadLines(settings).FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line == null ? null : line.Substring(prefix.Length);
    }

    internal static string Existing(params string[] values)
    {
        return values.FirstOrDefault(value => !String.IsNullOrEmpty(value) && File.Exists(value));
    }

    internal static string OnPath(string exe)
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
            .Where(folder => !String.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.Combine(folder.Trim().Trim('"'), exe))
            .FirstOrDefault(File.Exists);
    }

    internal static string CachedModel()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var home = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        if (String.IsNullOrEmpty(home)) home = Path.Combine(user, @".cache\huggingface\hub");
        var snapshots = Path.Combine(home, @"models--LiquidAI--LFM3-1.7B-GGUF\snapshots");
        return Directory.Exists(snapshots) ? Directory.GetFiles(snapshots, Weights, SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    internal static string Browser(string root)
    {
        var associated = new StringBuilder(32768);
        uint length = (uint)associated.Capacity;
        if (AssocQueryString(0x1000, 2, "https", "open", associated, ref length) == 0)
        {
            var path = associated.ToString();
            var name = Path.GetFileName(path).ToLowerInvariant();
            if ((name == "chrome.exe" || name == "msedge.exe") && File.Exists(path)) return path;
        }
        var progId = RegistryValue(Registry.CurrentUser,
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId");
        var chrome = BrowserPath(root, true);
        var edge = BrowserPath(root, false);
        if (progId.StartsWith("ChromeHTML", StringComparison.OrdinalIgnoreCase) && chrome != null) return chrome;
        if (progId.StartsWith("MSEdgeHTM", StringComparison.OrdinalIgnoreCase) && edge != null) return edge;
        return edge ?? chrome;
    }

    static string BrowserPath(string root, bool chrome)
    {
        var exe = chrome ? "chrome.exe" : "msedge.exe";
        var relative = chrome ? @"Google\Chrome\Application\chrome.exe" : @"Microsoft\Edge\Application\msedge.exe";
        return Existing(Find(root, chrome ? "chrome" : "edge"),
            RegistryPath(Registry.CurrentUser, exe), RegistryPath(Registry.LocalMachine, exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relative),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), relative),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relative));
    }

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int AssocQueryString(uint flags, uint value, string association, string extra,
        StringBuilder result, ref uint length);

    static string RegistryPath(RegistryKey hive, string exe)
    {
        return RegistryValue(hive, @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe, "").Trim('"');
    }

    static string RegistryValue(RegistryKey hive, string path, string name)
    {
        try
        {
            using (var key = hive.OpenSubKey(path))
                return key == null ? "" : Convert.ToString(key.GetValue(name));
        }
        catch (SecurityException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    internal static string BrowserProfile(string runtime, string browser)
    {
        var name = Path.GetFileName(browser ?? "msedge.exe");
        return Path.Combine(runtime, "assistant", name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ? "chrome-profile" : "edge-profile");
    }
}
