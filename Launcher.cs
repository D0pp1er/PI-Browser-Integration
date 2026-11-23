using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

static class Launcher
{
    const string Model = "LiquidAI/LFM3-1.7B-GGUF:Q4_K_M";
    const string Endpoint = "http://127.0.0.1:9931/v1/models";
    const string Session = "76b62b15-b4cf-4f8e-8a95-b76575518073";
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Runtime = InstallPaths.Find(Root, "data") ?? Path.Combine(Root, ".runtime");

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--browser")
        {
            try { OpenBrowser(); }
            catch (Exception error) { MessageBox.Show(error.Message, "Assistant Browser"); }
            return;
        }
        var check = args.Length == 1 && args[0] == "--check";
        using (var mutex = new Mutex(false, "Local\\SentienFormAssistant"))
        {
            if (!mutex.WaitOne(0))
            {
                MessageBox.Show("Form Assistant is already open. Switch to its Pi window.", "Form Assistant");
                return;
            }
            Process model = null;
            try
            {
                model = StartModelIfNeeded();
                Run(check);
            }
            catch (Exception error)
            {
                Environment.ExitCode = 1;
                if (check) File.WriteAllText(Path.Combine(Runtime, "launcher-check.txt"), error.Message);
                else MessageBox.Show(error.Message, "Form Assistant", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { try { StopModel(model); } finally { mutex.ReleaseMutex(); } }
        }
    }

    static void Run(bool check)
    {
        var home = Path.Combine(Runtime, "assistant");
        var sockets = Path.Combine(Runtime, "sockets");
        var modules = Path.Combine(Runtime, "node_modules");
        var controller = InstallPaths.Existing(InstallPaths.Find(Root, "agent-browser"));
        if (controller == null && Directory.Exists(modules))
            controller = Directory.GetFiles(modules, "agent-browser.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (controller == null) throw new Exception("The project is missing agent-browser.");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(sockets);
        File.Copy(Path.Combine(Root, "local-models.json"), Path.Combine(home, "models.json"), true);
        if (!ModelAvailable())
            throw new Exception("Open Llama, load LFM3-1.7B (Q4_K_M), then open Form Assistant again.");

        Environment.SetEnvironmentVariable("PI_CODING_AGENT_DIR", home);
        Environment.SetEnvironmentVariable("PI_TELEMETRY", "0");
        Environment.SetEnvironmentVariable("AGENT_BROWSER_SOCKET_DIR", sockets);
        Environment.SetEnvironmentVariable("AGENT_BROWSER_HOME", Path.Combine(Runtime, "browser-home"));
        Environment.SetEnvironmentVariable("PI_ASSISTANT_BROWSER", controller);
        var browserSession = "assistant-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Environment.SetEnvironmentVariable("PI_ASSISTANT_SESSION", browserSession);

        Environment.SetEnvironmentVariable("PI_ASSISTANT_PROFILE", InstallPaths.BrowserProfile(Runtime, InstallPaths.Browser(Root)));
        var arguments = "--offline --no-builtin-tools --no-extensions --no-mcp --no-context-files --no-skills"
            + " --session-id " + Session + " -e form-assistant.ts"
            + " --model " + Quote("local-lfm/" + Model)
            + " --thinking off --system-prompt assistant-prompt.txt";
        var piPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".pi\agent\bin\pi.cmd");
        piPath = InstallPaths.Existing(InstallPaths.Find(Root, "pi"), piPath);
        var piCommand = piPath == null ? "pi" : Quote(piPath);
        var isExe = piPath != null && Path.GetExtension(piPath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        if (check) arguments += " --list-models";
        using (var pi = isExe ? Start(piPath, arguments, check) : Start("cmd.exe", "/d /c call " + piCommand + " " + arguments, check))
        {
            pi.WaitForExit();
            if (pi.ExitCode != 0) throw new Exception("Pi exited with code " + pi.ExitCode + ". Check the Pi installation.");
            if (check) File.WriteAllText(Path.Combine(Runtime, "launcher-check.txt"), "PASS Pi model discovery without launching a browser");
        }
    }

    static void OpenBrowser()
    {
        var browser = InstallPaths.Browser(Root);
        if (browser == null) throw new Exception("Choose an existing Microsoft Edge or Google Chrome installation in setup.");
        var profile = InstallPaths.BrowserProfile(Runtime, browser);
        using (Start(browser, "--remote-debugging-address=127.0.0.1 --remote-debugging-port=0 --no-first-run --user-data-dir=" + Quote(profile) + " about:blank", false)) { }
    }

    static Process StartModelIfNeeded()
    {
        if (ModelAvailable()) return null;
        var weight = InstallPaths.Existing(InstallPaths.Find(Root, "model"), InstallPaths.CachedModel());
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\llama.exe");
        var llama = InstallPaths.Existing(InstallPaths.Find(Root, "llama"), alias);
        if (weight == null || llama == null)
            throw new Exception("Run Form Assistant Setup to find or download the local model and its runtime.");

        var command = Path.GetFileName(llama).Equals("llama.exe", StringComparison.OrdinalIgnoreCase) ? "serve " : "";
        var model = Start(llama, command + "-m " + Quote(weight) + " --alias " + Quote(Model)
            + " --host 127.0.0.1 --port 9931 --jinja -c 8192 -ngl 0 -t 6", true);
        for (var attempt = 0; attempt < 60 && !model.HasExited; attempt++)
        {
            if (ModelAvailable()) return model;
            Thread.Sleep(300);
        }
        StopModel(model);
        throw new Exception("Llama could not start the local model. Open Llama, load LFM3-1.7B, and try again.");
    }

    static void StopModel(Process model)
    {
        if (model == null) return;
        using (model)
        {
            if (!model.HasExited) model.Kill();
        }
    }

    static bool ModelAvailable()
    {
        try
        {
            var request = WebRequest.CreateHttp(Endpoint);
            request.Timeout = 2500;
            request.Proxy = null;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                return reader.ReadToEnd().Contains(Model);
        }
        catch (WebException) { return false; }
    }

    static Process Start(string file, string arguments, bool hidden)
    {
        return Process.Start(new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            CreateNoWindow = hidden,
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        });
    }

    static string Quote(string value) { return "\"" + value + "\""; }
}
