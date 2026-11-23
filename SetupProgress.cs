using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

sealed class SetupProgress : IDisposable
{
    readonly Form window;
    readonly Label label;
    readonly ProgressBar bar;

    internal SetupProgress(bool quiet)
    {
        if (quiet) return;
        window = new Form { Text = "Form Assistant Setup", Width = 480, Height = 140,
            StartPosition = FormStartPosition.CenterScreen, ControlBox = false };
        label = new Label { Left = 16, Top = 14, Width = 430, Height = 32 };
        bar = new ProgressBar { Left = 16, Top = 54, Width = 430, Height = 20 };
        window.Controls.Add(label);
        window.Controls.Add(bar);
        window.Show();
    }

    internal void Status(string text)
    {
        if (window == null) return;
        label.Text = text;
        Application.DoEvents();
    }

    internal string Download(string name, string url, string hash, string path)
    {
        var partial = path + ".part";
        Status("Downloading " + name);
        Exception error = null;
        var complete = false;
        using (var client = new WebClient())
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            client.DownloadProgressChanged += (sender, change) => {
                if (bar != null) bar.Value = Math.Max(0, Math.Min(100, change.ProgressPercentage));
            };
            client.DownloadFileCompleted += (sender, result) => { error = result.Error; Volatile.Write(ref complete, true); };
            client.DownloadFileAsync(new Uri(url), partial);
            while (!Volatile.Read(ref complete)) { Application.DoEvents(); Thread.Sleep(40); }
        }
        if (error != null) throw new Exception("Could not download " + name + ". Run setup again to retry.", error);
        Status("Verifying " + name);
        if (!Valid(partial, hash)) throw new Exception("The checksum did not match for " + name + ".");
        if (File.Exists(path)) File.Replace(partial, path, null);
        else File.Move(partial, path);
        return path;
    }

    internal static bool Valid(string path, string expected)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        if (window != null) window.Dispose();
    }
}
