import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";
import { browser, connectChrome, openOrConnectEdge } from "./browser-connection";
import { registerPageTools } from "./browser-page";

const fieldPattern = /^\s*-\s*(textbox|combobox|checkbox)\s+"([^"]+)"\s*\[[^\]]*\bref=(e\d+)\b/;
const sensitive = /password|passcode|pin\b|security code|card number|cvv|cvc|otp|one.time|national id|social security/i;
type Field = { kind: string; label: string; ref: string; required: boolean; options: string[] };
type Edit = { ref: string; value: string };

const requiredLabelsScript = `JSON.stringify(Array.from(
  document.querySelectorAll("input[required], select[required], textarea[required], [aria-required=true]"),
  field => field.getAttribute("aria-label") || Array.from(field.labels || [], label => label.innerText.trim()).join(" ")
))`;

async function inspect() {
  const url = await browser("get", "url");
  const snapshot = await browser("snapshot", "-i");
  const requiredLabels: string[] = JSON.parse(JSON.parse(await browser("eval", requiredLabelsScript)));
  const fields: Field[] = [];
  let select: Field | undefined;
  for (const line of snapshot.split(/\r?\n/)) {
    const option = /^\s+- option "([^"]+)"/.exec(line);
    if (select && option) {
      select.options.push(option[1]);
      continue;
    }
    select = undefined;
    const match = fieldPattern.exec(line);
    if (!match || sensitive.test(match[2])) continue;
    const [, kind, label, ref] = match;
    const required = requiredLabels.includes(label) || /\brequired\b/.test(match[0]);
    const field = { kind, label, ref, required, options: [] };
    fields.push(field);
    if (kind === "combobox") select = field;
  }
  return { url, snapshot, fields };
}

const result = (text: string) => ({ content: [{ type: "text" as const, text }] });

export default function (pi: ExtensionAPI) {
  let lastView: Awaited<ReturnType<typeof inspect>> | undefined;
  const clearPage = registerPageTools(pi);
  pi.registerCommand("browser-connect", {
    description: "Connect to Assistant Browser (Edge), or use /browser-connect chrome for Chrome",
    async handler(args, ctx) {
      const choice = args.trim().toLowerCase();
      if (choice && choice !== "edge" && choice !== "chrome") {
        ctx.ui.notify("Use /browser-connect for Edge or /browser-connect chrome for Chrome.", "warning");
        return;
      }
      if (choice === "chrome") await connectChrome();
      else await openOrConnectEdge();
      clearPage();
      lastView = undefined;
      ctx.ui.notify((choice === "chrome" ? "Chrome" : "Assistant Browser") + " connected. Use /browser-tab to choose a tab.", "info");
    },
  });

  pi.on("session_start", (_event, ctx) => {
    ctx.ui.setStatus("form-assistant", "Open Assistant Browser, then ask Pi to read the page or fill a form");
  });

  pi.registerCommand("browser-tab", {
    description: "List browser tabs or select one with /browser-tab 2 (use the listed index)",
    async handler(args, ctx) {
      const index = args.trim();
      if (index && !/^\d+$/.test(index)) throw new Error("Use a tab number shown by /browser-tab.");
      if (index) {
        await browser("tab", index);
        clearPage();
        lastView = undefined;
      }
      ctx.ui.notify(await browser("tab", "list"), "info");
    },
  });

  pi.registerTool({
    name: "inspect_form",
    label: "Read form",
    description: "Read the selected browser tab and its form controls. Use before filling.",
    parameters: { type: "object", properties: {}, additionalProperties: false } as any,
    async execute() {
      lastView = await inspect();
      return result(JSON.stringify({ url: lastView.url, fields: lastView.fields }));
    },
  });

  pi.registerTool({
    name: "fill_form_fields",
    label: "Fill form fields",
    description: "Propose values for the inspected fields. Use exact refs. Checkbox values are yes or no.",
    parameters: {
      type: "object",
      properties: {
        edits: {
          type: "array", minItems: 1, maxItems: 20,
          items: {
            type: "object", additionalProperties: false,
            properties: { ref: { type: "string" }, value: { type: "string", maxLength: 500 } },
            required: ["ref", "value"],
          },
        },
      },
      required: ["edits"], additionalProperties: false,
    } as any,
    async execute(_id, { edits }: { edits: Edit[] }, _signal, _update, ctx) {
      const view = lastView ?? await inspect();
      if (!ctx.hasUI) return result("Open an interactive Pi session to approve edits.");
      const proposed = new Map(edits.map(({ ref, value }) => [ref, value.trim()]));
      if (proposed.size !== edits.length || edits.some(edit => !view.fields.some(field => field.ref === edit.ref))) {
        return result("Use unique refs from the inspected field list.");
      }

      const history = ctx.sessionManager.getBranch().flatMap(entry => {
        if (entry.type !== "message" || entry.message.role !== "user") return [];
        return entry.message.content.flatMap(part => part.type === "text" ? [part.text] : []);
      }).join("\n").toLowerCase();

      const approved: { field: Field; value: string }[] = [];
      for (const field of view.fields) {
        if (!proposed.has(field.ref)) continue;
        let value = proposed.get(field.ref)!;
        if (field.kind === "combobox" && (!value || !history.includes(value.toLowerCase()))) {
          const matches = field.options.filter(option => history.includes(option.toLowerCase()));
          if (matches.length === 1) value = matches[0];
        }
        const known = value && (field.kind === "checkbox" || history.includes(value.toLowerCase()));
        if (!known && !field.required) continue;
        if (!known) {
          const answer = await ctx.ui.input("Enter " + field.label, "Required for this form");
          if (!answer?.trim()) return result("User left " + field.label + " unanswered. No fields changed.");
          value = answer.trim();
        }
        if (value.startsWith("-")) return result("Enter " + field.label + " directly in the browser.");
        approved.push({ field, value });
      }
      if (!approved.length) return result("No supplied values to fill. Ask the user for the needed information.");
      const preview = approved.map(({ field, value }) => field.label + " = " + JSON.stringify(value)).join("\n");
      if (!await ctx.ui.confirm("Fill these fields?", view.url + "\n\n" + preview)) {
        return result("User declined. No fields changed.");
      }

      const current = await inspect();
      if (current.url !== view.url || current.snapshot !== view.snapshot) {
        lastView = undefined;
        return result("The page changed. Read it again before editing.");
      }

      const applied: string[] = [];
      for (const { field, value } of approved) {
        const target = "@" + field.ref;
        try {
          if (field.kind === "checkbox") {
            if (value !== "yes" && value !== "no") throw new Error("Expected yes or no.");
            await browser(value === "yes" ? "check" : "uncheck", target);
          } else if (field.kind === "combobox") {
            await browser("select", target, value);
          } else {
            await browser("fill", target, value);
          }
          applied.push(field.label);
        } catch {
          lastView = undefined;
          return result("Filled: " + (applied.join(", ") || "none") + ". Stopped at " + field.label + ". Read the form again.");
        }
      }
      lastView = undefined;
      return result("Filled: " + applied.join(", ") + ". Review the form in the browser.");
    },
  });
}
