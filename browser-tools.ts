import { execFile } from "node:child_process";
import { promisify } from "node:util";

const exec = promisify(execFile);
const fields = {
  name: "#name",
  email: "#email",
  visit: "#visit",
  notes: "#notes",
  dummy: '[name="dummy"]',
};
type Field = keyof typeof fields;

export default function (pi: any) {
  async function browser(args: string[]) {
    const { PI_FORM_BROWSER, PI_FORM_SESSION } = process.env;
    if (!PI_FORM_BROWSER || !PI_FORM_SESSION) throw new Error("Run Test-Integration.ps1 first.");
    const { stdout } = await exec(PI_FORM_BROWSER, ["--session", PI_FORM_SESSION, ...args], {
      timeout: 15000,
      windowsHide: true,
    });
    return stdout.trim();
  }

  pi.registerTool({
    name: "read_form",
    label: "Read dummy form",
    description: "Read the visible form controls and any submission receipt.",
    parameters: { type: "object", properties: {}, additionalProperties: false },
    async execute() {
      const [controls, receipt] = await Promise.all([
        browser(["snapshot", "-i"]),
        browser(["get", "text", "#receipt"]),
      ]);
      return { content: [{ type: "text", text: controls + "\n" + receipt }] };
    },
  });

  pi.registerTool({
    name: "fill_form",
    label: "Fill dummy field",
    description: "Fill one field. Use field name, email, visit, notes, or dummy. For dummy use yes.",
    parameters: {
      type: "object",
      properties: {
        field: { type: "string", enum: Object.keys(fields) },
        value: { type: "string" },
      },
      required: ["field", "value"],
      additionalProperties: false,
    },
    async execute(_id: string, { field, value }: { field: Field; value: string }) {
      if (!Object.hasOwn(fields, field)) throw new Error("Unknown form field.");
      const selector = fields[field];
      let args = ["fill", selector, value];
      if (field === "visit") args = ["select", selector, value];
      if (field === "dummy") {
        if (value !== "yes") throw new Error("Use yes for the dummy information checkbox.");
        args = ["check", selector];
      }
      return { content: [{ type: "text", text: await browser(args) }] };
    },
  });

  pi.registerTool({
    name: "submit_dummy_form",
    label: "Submit dummy form",
    description: "Submit the local dummy form and read its receipt.",
    parameters: { type: "object", properties: {}, additionalProperties: false },
    async execute() {
      await browser(["click", '#appointment button[type="submit"]']);
      const receipt = await browser(["get", "text", "#receipt"]);
      return { content: [{ type: "text", text: receipt || "No receipt. Check the required fields." }] };
    },
  });
}

