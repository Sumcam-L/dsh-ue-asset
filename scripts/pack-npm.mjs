// Stages the plugin + compiled binary into a clean npm package and packs it.
// Usage: node scripts/pack-npm.mjs [distDir]
//   distDir defaults to <repo>/dist-release and must contain ueasset.exe,
//   e_sqlite3.dll, libSkiaSharp.dll and CUE4Parse-Natives.dll.
// Oodle is never packaged (Epic-proprietary); the settings page can fetch it.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

const root = path.resolve(import.meta.dirname, "..");
const pluginDir = path.join(root, "plugin");
const distDir = path.resolve(process.argv[2] ?? path.join(root, "dist-release"));
const outDir = path.join(root, "build");
const stage = path.join(outDir, "package");
const pkg = JSON.parse(fs.readFileSync(path.join(pluginDir, "package.json"), "utf8"));

const BINARIES = ["ueasset.exe", "e_sqlite3.dll", "libSkiaSharp.dll", "CUE4Parse-Natives.dll"];
const PLUGIN_ITEMS = ["index.js", "client.js", "package.json", "cordis.patch.yml", "README.md",
  "screenshots.json", "icon.svg", "locale", "assets", "vendor"];

const fail = (msg) => { console.error("FAIL: " + msg); process.exit(1); };

for (const b of BINARIES) {
  const p = path.join(distDir, b);
  if (!fs.existsSync(p)) fail("missing binary " + p);
}
for (const item of PLUGIN_ITEMS) {
  const p = path.join(pluginDir, item);
  if (!fs.existsSync(p)) fail("missing plugin item " + p);
}

fs.rmSync(stage, { recursive: true, force: true });
fs.mkdirSync(path.join(stage, "dist"), { recursive: true });

for (const item of PLUGIN_ITEMS) {
  fs.cpSync(path.join(pluginDir, item), path.join(stage, item), { recursive: true });
}
for (const b of BINARIES) {
  fs.copyFileSync(path.join(distDir, b), path.join(stage, "dist", b));
}

// Guards: nothing proprietary or accidentally huge may slip into the tarball.
const walk = (dir, acc = []) => {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, acc); else acc.push(p);
  }
  return acc;
};
const files = walk(stage);
const oodle = files.filter((f) => /oo2core/i.test(path.basename(f)));
if (oodle.length) fail("Oodle must not be packaged: " + oodle.join(", "));
const oversized = files.filter((f) => fs.statSync(f).size > 40 * 1024 * 1024);
if (oversized.length) fail("unexpectedly large file: " + oversized.join(", "));

const stagePkg = JSON.parse(fs.readFileSync(path.join(stage, "package.json"), "utf8"));
if (stagePkg.name !== "dsh-ue-asset") fail("unexpected package name " + stagePkg.name);

fs.mkdirSync(outDir, { recursive: true });
for (const old of fs.readdirSync(outDir)) {
  if (old.startsWith("dsh-ue-asset-") && old.endsWith(".tgz")) fs.rmSync(path.join(outDir, old));
}

const npmCmd = process.platform === "win32" ? "npm.cmd" : "npm";
execFileSync(npmCmd, ["pack", "--pack-destination", outDir], { cwd: stage, stdio: "inherit", shell: process.platform === "win32" });

const tgz = path.join(outDir, `dsh-ue-asset-${stagePkg.version}.tgz`);
if (!fs.existsSync(tgz)) fail("tarball was not produced");
console.log(`\npacked ${tgz}`);
console.log(`  version ${stagePkg.version}, ${(fs.statSync(tgz).size / 1048576).toFixed(1)} MB, ${files.length} files staged`);
