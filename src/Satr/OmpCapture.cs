using System.IO;
using System.Text;

namespace Satr;

internal static class OmpCapture
{
    public const string Script = """
import fs from "node:fs";

const delay = ms => new Promise(r => setTimeout(r, ms));

export default function (pi) {
  pi.on("agent_end", async event => {
    const target = process.env.SATR_AI_CAPTURE_FILE;
    if (!target || !event?.messages) return;
    for (let i = event.messages.length - 1; i >= 0; i--) {
      const msg = event.messages[i];
      if (msg?.role === "assistant" && Array.isArray(msg.content)) {
        const texts = msg.content
          .filter(c => c?.type === "text" && typeof c.text === "string" && c.text.length > 0)
          .map(c => c.text);
        if (texts.length > 0) {
          const payload = texts.join("\n");
          const tmp = `${target}.tmp.${process.pid}.${Date.now()}.${Math.random().toString(16).slice(2)}`;
          try {
            fs.writeFileSync(tmp, payload, "utf8");
          } catch {
            return;
          }
          for (let attempt = 0; attempt < 4; attempt++) {
            try {
              fs.renameSync(tmp, target);
              return;
            } catch (err) {
              if (err?.code !== "EPERM" || attempt === 3) {
                try { fs.unlinkSync(tmp); } catch {}
                return;
              }
              await delay(50);
            }
          }
          return;
        }
      }
    }
  });
}
""";

    public static string EnsureExtension()
    {
        var dir = WorkspaceStore.DataDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "omp-capture-extension.js");
        try
        {
            if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != Script)
                File.WriteAllText(path, Script, Encoding.UTF8);
        }
        catch { }
        return path;
    }
}
