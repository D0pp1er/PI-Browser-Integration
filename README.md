# Form Assistant

Form Assistant runs Pi with a local LFM3-1.7B model. It can read visible page text, fill standard form fields, and click labeled controls in Edge or Chrome. It previews proposed changes and asks for approval before acting.

## Install on Windows x64

1. Run **FormAssistant-Setup-x64.exe**. Accept the suggested folder at `%LOCALAPPDATA%\Programs\Form Assistant` or choose another.
2. Setup finds compatible copies of Pi, llama.cpp, and agent-browser, plus the expected GGUF model. It saves the paths it uses and downloads missing components. Allow internet access during initial setup if anything is missing. The model download is about **1.09 GB**. If you already have the model and setup cannot find it, select your copy when prompted.
3. Open **Form Assistant** from Start. Pi starts on its own and resumes the saved conversation on later launches.

Setup uses the Edge or Chrome installation on the PC. It leaves Windows browser defaults alone. If setup cannot find a supported browser, it asks you to locate one.

## Use the browser

- Enter `/browser-connect` in Pi or choose **Open Assistant Browser** from Start. This opens your Windows default browser in a dedicated profile if the default is Edge or Chrome. For another default browser, the assistant uses installed Edge or Chrome.
- Open the page you want. Ask Pi to **Read this page**, **Help me fill this form**, or **Select Contact by email**. Use `/browser-tab` to list tabs and `/browser-tab <number>` to choose one.
- Review the proposed changes in Pi. When you are ready to submit, say **Submit this form** and approve the submit click.
- To try the bundled example, press **Ctrl+O** in Assistant Browser and open `form.html` from your install folder. A successful submission displays a receipt on the page.

For an already-open regular Chrome window, enable remote debugging at `chrome://inspect/#remote-debugging`, enter `/browser-connect chrome` in Pi, and approve Chrome's prompt if shown. Browser profiles and the saved Pi conversation are stored locally under `%LOCALAPPDATA%\FormAssistant`.

## Build the installer from source

Open PowerShell in the project folder on 64-bit Windows and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Setup.ps1
```

The script uses the Windows .NET Framework compiler. It builds offline, writes **`dist\FormAssistant-Setup-x64.exe`**, and prints its full SHA-256 checksum. Distribute that EXE to users. The build script is for developers.

To put the installer in another folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Setup.ps1 -OutputDirectory 'C:\Releases\FormAssistant'
```

To build from another working directory, pass the full path to `Build-Setup.ps1` after `-File`.

## Pilot status

Verified on this PC with Edge. Setup downloaded the three runtime tools to an empty test folder, verified and reused the local model, and reused saved paths on a second run. Pi-only startup and the Edge dummy-form workflow passed. Chrome and a full first-time model download are still unverified. The installer is unsigned.

See [architecture.md](architecture.md) for the design, [CHANGELOG.md](CHANGELOG.md) for dated changes, and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for component licenses.

