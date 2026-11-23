using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class Setup
{
    const string PiUrl = "https://github.com/earendil-works/pi/releases/download/v1.6.0/pi-windows-x64.zip";
    const string PiHash = "f5defb6c403ee698f5b3f8886db473e55b78aac670a8ce03620a47f5f0aec57c";
    const string LlamaUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11960/llama-b11960-bin-win-cpu-x64.zip";
    const string LlamaHash = "73f962f82850d244db47eaacb9d913637027b82399124324298576d58c5f4287";
    const string BrowserUrl = "https://github.com/vercel-labs/agent-browser/releases/download/v0.43.0/agent-browser-win32-x64.exe";
    const string BrowserHash = "ebcef5ebea93c7ccf0fe3f8c18eee908ae9174dfef8e52a3dda6f24a59f630dc";
    const string ModelUrl = "https://huggingface.co/LiquidAI/LFM3-1.7B-GGUF/resolve/cd0c74c575b5c416f3011b5b4c298077f26ca8c2/LFM3-1.7B-Q4_K_M.gguf";
    const string ModelHash = "3793e1e6e2266919d6e015287221b41100175a98eddaba92e111aed3b05c93c4";
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string User = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [STAThread]
    static int Main(string[] args)
    {
        var quiet = args.Length == 2 && (args[0] == "--install-dir" || args[0] == "--fresh-install-dir");
        try
        {
            if (!Environment.Is64BitOperatingSystem) throw new Exception("Form Assistant requires 64-bit Windows.");
            var root = quiet ? Path.GetFullPath(args[1]) : ChooseFolder();
            if (root == null) return 0;
            using (var mutex = new Mutex(false, "Local\\SentienFormAssistant"))
            {
                if (!mutex.WaitOne(0)) throw new Exception("Close Form Assistant before running setup.");
                try { Install(root, quiet, quiet && args[0] == "--fresh-install-dir"); }
                finally { mutex.ReleaseMutex(); }
            }
            if (!quiet && MessageBox.Show("Form Assistant is ready. Open it now?", "Setup complete",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(Path.Combine(root, "Form Assistant.exe")) { WorkingDirectory = root });
            return 0;
        }
        catch (Exception error)
        {
            if (quiet) File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]), "setup-error.txt"), error.ToString());
            else MessageBox.Show(error.Message, "Form Assistant Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static string ChooseFolder()
    {
        var suggested = Path.Combine(Local, @"Programs\Form Assistant");
        var choice = MessageBox.Show("Install Form Assistant in this folder?\n\n" + suggested
            + "\n\nChoose No to pick another folder.", "Form Assistant Setup", MessageBoxButtons.YesNoCancel);
        if (choice == DialogResult.Yes) return suggested;
        if (choice == DialogResult.Cancel) return null;
        using (var picker = new FolderBrowserDialog { Description = "Choose a folder for Form Assistant", SelectedPath = Local })
            return picker.ShowDialog() == DialogResult.OK ? Path.Combine(picker.SelectedPath, "Form Assistant") : null;
    }

    static void Install(string root, bool quiet, bool fresh)
    {
        Directory.CreateDirectory(root);
        var data = quiet ? Path.Combine(root, "user-data") : Path.Combine(Local, "FormAssistant");
        var deps = Path.Combine(root, "dependencies");
        Directory.CreateDirectory(deps);
        Directory.CreateDirectory(data);
        var paths = new Dictionary<string, string> { { "data", data } };
        var browser = InstallPaths.Browser(root);
        if (browser == null && !quiet)
            browser = PickFile("Locate installed Edge or Chrome", "Edge or Chrome|msedge.exe;chrome.exe", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application"));
        if (browser == null) throw new Exception("Setup could not locate installed Microsoft Edge or Google Chrome.");
        paths[Path.GetFileName(browser).Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ? "chrome" : "edge"] = browser;

        using (var progress = new SetupProgress(quiet))
        {
            paths["pi"] = Find(root, "pi", fresh, Path.Combine(deps, @"pi\pi.exe"),
                Path.Combine(User, @".pi\agent\bin\pi.cmd"), InstallPaths.OnPath("pi.exe"), InstallPaths.OnPath("pi.cmd"))
                ?? Unpack(progress, "Pi", PiUrl, PiHash, Path.Combine(deps, "pi"), "pi.exe");
            paths["llama"] = Find(root, "llama", fresh, Path.Combine(deps, @"llama\llama-server.exe"),
                InstallPaths.OnPath("llama-server.exe"), Path.Combine(Local, @"Microsoft\WindowsApps\llama.exe"))
                ?? Unpack(progress, "llama.cpp", LlamaUrl, LlamaHash, Path.Combine(deps, "llama"), "llama-server.exe");
            paths["agent-browser"] = Find(root, "agent-browser", fresh, Path.Combine(deps, "agent-browser.exe"),
                InstallPaths.OnPath("agent-browser.exe"))
                ?? progress.Download("agent-browser", BrowserUrl, BrowserHash, Path.Combine(deps, "agent-browser.exe"));

            var cachePath = Path.Combine(data, InstallPaths.Weights);
            var model = Find(root, "model", false, cachePath, InstallPaths.CachedModel());
            if (model == null && !quiet && MessageBox.Show("The model download is about 1.09 GB. Have a copy already?",
                "Local model", MessageBoxButtons.YesNo) == DialogResult.Yes)
                model = PickFile("Locate LFM3-1.7B Q4_K_M", "GGUF model|*.gguf", data);
            if (model != null)
            {
                progress.Status("Verifying the local model");
                if (!SetupProgress.Valid(model, ModelHash)) model = null;
            }
            paths["model"] = model ?? progress.Download("LFM3-1.7B", ModelUrl, ModelHash, cachePath);
            progress.Status("Installing Form Assistant");
            ExtractApp(root);
            File.WriteAllLines(Path.Combine(root, InstallPaths.SettingsFile), paths.Select(pair => pair.Key + "=" + pair.Value));
        }
        if (!quiet) CreateShortcuts(root);
    }

    static string Find(string root, string key, bool fresh, params string[] candidates)
    {
        if (fresh) return null;
        var saved = InstallPaths.Existing(InstallPaths.Find(root, key));
        if (saved != null) return saved;
        return candidates.FirstOrDefault(path => File.Exists(path) && (key == "model" || CompatibleTool(path, key)));
    }

    static bool CompatibleTool(string path, string key)
    {
        var script = Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        var args = Path.GetFileName(path).Equals("llama.exe", StringComparison.OrdinalIgnoreCase) ? "version" : "--version";
        var info = new ProcessStartInfo(script ? "cmd.exe" : path,
            script ? "/d /c call \"" + path + "\" " + args : args) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        try
        {
            using (var process = new Process { StartInfo = info })
            {
                var output = new StringBuilder();
                DataReceivedEventHandler collect = (sender, data) => { if (data.Data != null) lock (output) output.AppendLine(data.Data); };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(6000)) { process.Kill(); return false; }
                process.WaitForExit();
                if (process.ExitCode != 0) return false;
                var match = Regex.Match(output.ToString(), key == "llama" ? @"\bbuild\s+(\d+)\b" : @"\b(\d+\.\d+\.\d+)\b");
                if (!match.Success) return false;
                if (key == "llama") return Int32.Parse(match.Groups[1].Value) >= 11960;
                var minimum = key == "pi" ? new Version(1, 6, 0) : new Version(0, 43, 0);
                return new Version(match.Groups[1].Value) >= minimum;
            }
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    static string PickFile(string title, string filter, string suggestedFolder)
    {
        using (var picker = new OpenFileDialog {
            Title = title, Filter = filter, InitialDirectory = suggestedFolder, CheckFileExists = true
        }) return picker.ShowDialog() == DialogResult.OK ? picker.FileName : null;
    }

    static string Unpack(SetupProgress progress, string name, string url, string hash, string folder, string exe)
    {
        var archive = progress.Download(name, url, hash, folder + ".zip");
        var staging = folder + ".incoming-" + Guid.NewGuid().ToString("N");
        ZipFile.ExtractToDirectory(archive, staging);
        var found = Directory.GetFiles(staging, exe, SearchOption.AllDirectories).Single();
        if (Directory.Exists(folder)) folder += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.Move(staging, folder);
        File.Delete(archive);
        return folder + found.Substring(staging.Length);
    }

    static void ExtractApp(string root)
    {
        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("form-assistant.zip"))
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            foreach (var file in archive.Entries)
                file.ExtractToFile(Path.Combine(root, file.Name), true);
    }

    static void CreateShortcuts(string root)
    {
        var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Form Assistant");
        Directory.CreateDirectory(menu);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        foreach (var name in new[] { "Form Assistant", "Open Assistant Browser" })
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(menu, name + ".lnk"));
            shortcut.TargetPath = Path.Combine(root, "Form Assistant.exe");
            shortcut.WorkingDirectory = root;
            shortcut.Arguments = name == "Form Assistant" ? "" : "--browser";
            shortcut.Save();
        }
    }
}
