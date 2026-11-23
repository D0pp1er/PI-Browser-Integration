import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";
import { browser } from "./browser-connection";

const result = (text: string) => ({ content: [{ type: "text" as const, text }] });
const pageSize = 3200;

async function view() {
  const url = await browser("get", "url");
  const snapshot = await browser("snapshot", "-i");
  return { url, snapshot };
}

export function registerPageTools(pi: ExtensionAPI) {
  let lastPage: Awaited<ReturnType<typeof view>> | undefined;

  pi.registerTool({
    name: "read_browser_page",
    label: "Read browser page",
    description: "Read visible page text in chunks and list labeled links, buttons, radios, and other controls. Start with offset 0.",
    parameters: {
      type: "object", properties: { offset: { type: "integer", minimum: 0 } }, additionalProperties: false,
    } as any,
    async execute(_id, { offset = 0 }: { offset?: number }) {
      lastPage = await view();
      const text: string = JSON.parse(await browser("eval", "document.body ? document.body.innerText : ''"));
      const controls = offset === 0 ? lastPage.snapshot.split(/\r?\n/)
        .map(line => /^\s*-\s*(button|link|radio|checkbox|option|menuitem)\s+"([^"]+)"\s*\[[^\]]*\bref=(e\d+)\b/.exec(line))
        .flatMap(match => match ? [{ role: match[1], label: match[2], ref: match[3] }] : [])
        .slice(0, 45) : undefined;
      return result(JSON.stringify({
        url: lastPage.url, text: text.slice(offset, offset + pageSize), controls,
        nextOffset: offset + pageSize < text.length ? offset + pageSize : null,
      }));
    },
  });

  pi.registerTool({
    name: "click_browser_control",
    label: "Click browser control",
    description: "Click a labeled button, link, radio, checkbox, or option from read_browser_page. For submission use the actual submit button and only when the user asks. Each click requires confirmation.",
    parameters: {
      type: "object", properties: { ref: { type: "string", pattern: "^e[0-9]+$" } },
      required: ["ref"], additionalProperties: false,
    } as any,
    async execute(_id, { ref }: { ref: string }, _signal, _update, ctx) {
      if (!ctx.hasUI || !lastPage || !/^e\d+$/.test(ref)) return result("Read the page first and use its control reference.");
      const page = lastPage;
      const line = page.snapshot.split(/\r?\n/).find(value => new RegExp("\\bref=" + ref + "\\b").test(value));
      const control = line && /^\s*-\s*(button|link|radio|checkbox|option|menuitem)\s+"([^"]+)"/.exec(line);
      if (!control) return result("Choose a labeled button, link, or option from read_browser_page.");
      const [, role, label] = control;
      if (!await ctx.ui.confirm("Click " + label + "?", page.url + "\n\n" + role + ": " + label)) {
        return result("Click canceled. No action taken.");
      }
      const current = await view();
      lastPage = undefined;
      if (current.url !== page.url || current.snapshot !== page.snapshot) {
        return result("The page changed. Read it again before clicking.");
      }
      await browser("click", "@" + ref);
      return result("Clicked " + label + ". Read the page again to check the result. Do not repeat a submission when the result is uncertain.");
    },
  });
  return () => { lastPage = undefined; };
}
