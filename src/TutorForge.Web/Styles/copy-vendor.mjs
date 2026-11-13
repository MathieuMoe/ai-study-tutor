// Copies the third-party browser files we need from node_modules into wwwroot/lib.
import { cpSync, mkdirSync } from "node:fs";

const copies = [
  ["node_modules/preline/dist/preline.js", "wwwroot/lib/preline/preline.js"],
  ["node_modules/preline/LICENSE", "wwwroot/lib/preline/LICENSE"],
  ["node_modules/marked/lib/marked.esm.js", "wwwroot/lib/marked/marked.esm.js"],
  ["node_modules/marked/LICENSE", "wwwroot/lib/marked/LICENSE"],
  ["node_modules/dompurify/dist/purify.es.mjs", "wwwroot/lib/dompurify/purify.es.mjs"],
  ["node_modules/dompurify/LICENSE", "wwwroot/lib/dompurify/LICENSE"],
  ["node_modules/katex/dist/katex.mjs", "wwwroot/lib/katex/katex.mjs"],
  ["node_modules/katex/dist/katex.min.css", "wwwroot/lib/katex/katex.min.css"],
  ["node_modules/katex/dist/contrib/auto-render.mjs", "wwwroot/lib/katex/contrib/auto-render.mjs"],
  ["node_modules/katex/dist/fonts", "wwwroot/lib/katex/fonts"],
  ["node_modules/katex/LICENSE", "wwwroot/lib/katex/LICENSE"],
];

for (const [from, to] of copies) {
  mkdirSync(to.substring(0, to.lastIndexOf("/")), { recursive: true });
  cpSync(from, to, { recursive: true });
  console.log(`copied ${from} -> ${to}`);
}
