import { readdir, rm } from "node:fs/promises";
import { resolve } from "node:path";

const output = resolve(import.meta.dirname, "../../wwwroot/admin");
const assets = resolve(output, "assets");

await Promise.all([
  rm(resolve(output, "index.html"), { force: true }),
  rm(resolve(output, "admin.js"), { force: true }),
  rm(resolve(output, "admin.css"), { force: true })
]);

for (const entry of await readdir(assets, { withFileTypes: true })) {
  if (entry.isFile() && /^(?:admin(?:-.+)?\.(?:js|css)(?:\.map)?|chunk-.+\.js(?:\.map)?)$/.test(entry.name)) {
    await rm(resolve(assets, entry.name), { force: true });
  }
}
