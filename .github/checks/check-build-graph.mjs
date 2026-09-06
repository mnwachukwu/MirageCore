// No MSBuild task may pass a global property to a Build target.
//
// A global property forks the build CONFIGURATION, and configuration is what MSBuild keys its result
// cache on. It also flows down every ProjectReference. So `<MSBuild Targets="Build" Properties="x=1"/>`
// inside a solution member does not reuse the outer build's results — it starts a second compile of that
// project AND its whole dependency graph, concurrently, into the same obj/. The two compilers then race
// for the same output file and one loses with CS2012 or MSB3883, which reads as a flaky CI run because
// whether they overlap is a matter of timing.
//
// Properties on any OTHER target are fine: Restore writes package assets and never invokes the compiler,
// and the publish projects pass _Rid to their own copy targets within one project.
//
// Run from the repository root.
import fs from "node:fs";
import path from "node:path";

const ROOT = process.cwd();
const SKIP = new Set(["bin", "obj", ".git", "node_modules", "dist"]);

function* projectFiles(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (SKIP.has(entry.name)) continue;
      yield* projectFiles(path.join(dir, entry.name));
    } else if (/\.(csproj|props|targets)$/.test(entry.name)) {
      yield path.join(dir, entry.name);
    }
  }
}

const offenders = [];
let scanned = 0;

for (const file of projectFiles(ROOT)) {
  scanned++;
  const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
  lines.forEach((line, i) => {
    if (!line.includes("<MSBuild ")) return;
    if (!/Targets="Build"/.test(line)) return;
    if (!/\bProperties="/.test(line)) return;
    offenders.push(`${path.relative(ROOT, file)}:${i + 1}\n    ${line.trim()}`);
  });
}

if (offenders.length > 0) {
  console.error("Global properties passed to a Build target — this forks the configuration and builds");
  console.error("the dependency graph a second time, racing the outer build into the same obj/:\n");
  for (const o of offenders) console.error(`  ${o}\n`);
  console.error("Move the property to the Restore call, or drop it. See tests/Mirage.Test.csproj.");
  process.exit(1);
}

console.log(`No Build target takes a global property (${scanned} project files checked).`);
