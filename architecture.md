# Architecture

## Startup and browser connection

1. **Form Assistant.exe** starts the local LFM model if needed and opens Pi. The launcher does not start a browser.
2. When ready, the user opens **Open Assistant Browser** or enters `/browser-connect` in Pi. It launches the installed Windows default browser when that browser is Edge or Chrome, using a dedicated profile and a local DevTools endpoint. If neither is the Windows default, it falls back to installed Edge or Chrome. The user chooses the page. The first browser request from Pi attaches to that browser session. The browser can be opened before or after Pi.
3. To use a regular Chrome window, enable remote debugging at `chrome://inspect/#remote-debugging`, enter `/browser-connect chrome` in Pi, and approve Chrome's prompt if shown. Use `/browser-tab` to list tabs and `/browser-tab <number>` to select one.
4. Pi opens the same saved conversation each launch. Both Edge and Chrome remain under the user's control after Pi exits.

User → Pi conversation → local LFM → typed extension tools → agent-browser → selected tab

## Components

| File | Responsibility |
| --- | --- |
| `Launcher.cs` | Start Pi and reuse its fixed session. The optional browser shortcut calls the same executable with `--browser`. |
| `browser-connection.ts` | Discover the dedicated browser endpoint and attach on demand, or connect to running Chrome on user request. |
| `browser-page.ts` | Read visible page text in chunks, expose labeled links and controls, and confirm clicks. |
| `form-assistant.ts` | Inspect fields and native dropdown choices. Use prior messages, ask for missing required details, and confirm edits. |
| `assistant-prompt.txt` | Tell LFM when to use each tool, when to ask, and when to submit. |
| `local-models.json` | Configure local LFM3‑1.7B Q4_K_M for Pi. |
| `form.html` | An optional local test page with a submission receipt. |

## Actions and memory

The assistant reads rendered text from the selected page in 3,200-character chunks. It can fill labeled text fields, check or uncheck checkboxes, choose native dropdown options, and click labeled links, radios, and buttons. Each proposed click has a confirmation. If the user asks to submit, Pi uses the same confirmed click flow for the submit button. A click confirms only that the action ran. Pi reads the result to check for a receipt or validation message. The tools recheck the page before acting after a confirmation.

Installed Pi stores its conversation in `%LOCALAPPDATA%/FormAssistant/assistant/sessions`. Saved user messages can supply later form values, but Pi can compact older turns. The history is plain text. Pi's shell, file-writing, and MCP tools are disabled in this launcher. Ordinary Edge tabs need a dedicated compatible session. Chrome requires explicit browser opt-in.

## Limits and package size

Visible text and labeled accessibility controls are the first supported browser surface. Custom widgets, PDF viewers, canvas-based text, embedded frames, complex forms, and sites requiring CAPTCHA or authentication may need the user's help. The selected controller tab can differ from the frontmost tab, so use `/browser-tab` when switching tabs. Use the browser's displayed result to verify important actions.

Setup never downloads a browser or changes Windows defaults. The default-browser route uses a dedicated profile. The optional regular Chrome attachment requires Chrome's debugging permission.

## Setup layout

- Build-Setup.ps1 embeds the application and third-party notices in one x64 setup EXE.
- Setup.cs detects compatible Pi, llama.cpp, and agent-browser executables and a local LFM cache. It downloads missing tools and weights from pinned sources and verifies SHA-256 hashes before use.
- InstallPaths.cs saves and reads local paths in install.paths. Setup suggests a per-user install folder and lets users pick another. The model picker suggests the per-user app data folder, and the browser picker suggests the standard Edge installation folder.
- Setup creates Start menu shortcuts. The app puts mutable state under %LOCALAPPDATA%/FormAssistant, separately from installed binaries. The model weights are about 1.09 GB if a download is needed.

Build the release with Build-Setup.ps1. It leaves developer caches and existing user sessions out of the embedded application.
