import { execFile } from "node:child_process";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { promisify } from "node:util";

const exec = promisify(execFile);
let attached = "";
let useChrome = false;

async function control(suffix: string, ...args: string[]) {
  const { PI_ASSISTANT_BROWSER, PI_ASSISTANT_SESSION } = process.env;
  if (!PI_ASSISTANT_BROWSER || !PI_ASSISTANT_SESSION) {
    throw new Error("Start Pi using Form Assistant.exe.");
  }
  const { stdout } = await exec(PI_ASSISTANT_BROWSER,
    ["--session", PI_ASSISTANT_SESSION + suffix, ...args],
    { windowsHide: true, timeout: 30000 });
  return stdout.trim();
}

export async function connectChrome() {
  try {
    await control("-chrome", "--auto-connect", "tab", "list");
    useChrome = true;
    attached = "";
  } catch {
    throw new Error("Open Chrome, enable remote debugging at chrome://inspect/#remote-debugging, and retry /browser-connect chrome. Approve Chrome's connection prompt if shown.");
  }
}

async function edgeEndpoint(profile: string) {
  try {
    const [port, path] = (await readFile(join(profile, "DevToolsActivePort"), "utf8")).trim().split(/\r?\n/);
    if (!/^\d{1,5}$/.test(port) || !/^\/devtools\/browser\/[\w-]+$/.test(path)) throw new Error();
    const response = await fetch("http://127.0.0.1:" + port + "/json/version", { signal: AbortSignal.timeout(1500) });
    const endpoint = "ws://127.0.0.1:" + port + path;
    if (!response.ok || (await response.json()).webSocketDebuggerUrl !== endpoint) throw new Error();
    return endpoint;
  } catch {
    return undefined;
  }
}

async function connectEdge(endpoint: string) {
  if (endpoint !== attached) {
    await control("", "connect", endpoint);
    attached = endpoint;
  }
}

export async function openOrConnectEdge() {
  const profile = process.env.PI_ASSISTANT_PROFILE;
  if (!profile) throw new Error("Start Pi with Form Assistant.exe to connect to Assistant Browser.");

  let endpoint = await edgeEndpoint(profile);
  if (!endpoint) {
    await exec(join(process.cwd(), "Form Assistant.exe"), ["--browser"], { timeout: 10000 });
    for (let attempt = 0; attempt < 25 && !endpoint; attempt++) {
      await delay(250);
      endpoint = await edgeEndpoint(profile);
    }
  }
  if (!endpoint) throw new Error("Assistant Browser did not open. Close any previous Assistant Browser window, then try /browser-connect again.");

  await connectEdge(endpoint);
  useChrome = false;
}

export async function browser(...args: string[]) {
  const profile = process.env.PI_ASSISTANT_PROFILE;
  if (useChrome) return control("-chrome", "--auto-connect", ...args);
  if (!profile) return control("", ...args);

  const endpoint = await edgeEndpoint(profile);
  if (!endpoint) throw new Error("Enter /browser-connect in Pi to open or reconnect Assistant Browser.");
  await connectEdge(endpoint);
  return control("", "--cdp", endpoint, ...args);
}
