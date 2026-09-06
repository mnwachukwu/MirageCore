// Check that the committed content binaries were built from the content sources beside them.
//
//     node .github/checks/check-prebuilt-content.mjs
//
// WHY THIS EXISTS. The client's compiled content is committed rather than built — see
// docs/architecture.md, "Why compiled content is committed". The cost of that trade is a set of
// binaries that can silently fall behind their sources: editing a .spritefont or the shader changes
// nothing anybody sees until someone rebuilds on Windows, and the game keeps running the OLD content
// with no error anywhere. This is the thing that notices.
//
// The pairing is recorded as hashes of the SOURCES, taken when the binaries were built, so this is a
// comparison rather than a timestamp — a fresh clone has no useful mtimes and a rebased branch has
// misleading ones.
//
// The source list is DERIVED, not written down here, so adding a font cannot quietly escape the check:
// a new source with no entry in the manifest fails, and so does a manifest entry whose source is gone.
//
// Node with no dependencies, matching the other checks here.

import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, dirname, resolve, basename, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

function findRepoRoot(start) {
  for (let dir = resolve(start); ; dir = dirname(dir)) {
    if (existsSync(join(dir, 'Mirage.slnx'))) return dir;
    if (dirname(dir) === dir) return resolve(start);
  }
}
const root = findRepoRoot(dirname(fileURLToPath(import.meta.url)));
const content = join(root, 'client', 'src', 'Mirage.Client.Shell', 'Content');
const prebuilt = join(content, 'prebuilt');
const manifestPath = join(prebuilt, 'sources.sha256');

/** Everything mgcb reads: font descriptions, any font file bundled beside them, and effects. */
const SOURCE_EXTENSIONS = new Set(['.spritefont', '.ttf', '.otf', '.fx']);
const SKIP_DIRS = new Set(['bin', 'obj', 'prebuilt']);

function collectSources(dir, rel = '') {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (SKIP_DIRS.has(entry.name)) continue;
      out.push(...collectSources(join(dir, entry.name), rel ? `${rel}/${entry.name}` : entry.name));
    } else if (SOURCE_EXTENSIONS.has(extname(entry.name).toLowerCase())) {
      out.push({ rel: rel ? `${rel}/${entry.name}` : entry.name, full: join(dir, entry.name) });
    }
  }
  return out;
}

const problems = [];

if (!existsSync(prebuilt) || !existsSync(manifestPath)) {
  console.error('FAILED — no committed content found:\n');
  console.error(`  expected ${manifestPath.replace(root, '').replace(/\\/g, '/')}`);
  console.error('\n  Rebuild on Windows and commit Content/prebuilt/:');
  console.error('    dotnet msbuild client/src/Mirage.Client.Shell/Mirage.Client.Shell.csproj -t:BuildContent');
  process.exit(1);
}

// The manifest is sha256sum's own format, "<hash> *<name>", written by MSBuild in upper case. Keyed by
// file name: MSBuild does not carry a relative directory through GetFileHash, and every content source
// here has a distinct name — which the duplicate check below is what enforces.
const recorded = new Map();
for (const line of readFileSync(manifestPath, 'utf8').split(/\r?\n/)) {
  const m = line.match(/^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$/);
  if (!m) continue;
  const key = basename(m[2].replace(/\\/g, '/'));
  if (recorded.has(key)) problems.push(`  ${key}: listed twice in sources.sha256`);
  recorded.set(key, m[1].toLowerCase());
}

const sources = collectSources(content);
const seen = new Set();

for (const { rel, full } of sources) {
  const key = basename(rel);
  if (seen.has(key)) {
    problems.push(`  ${rel}: another content source is also named ${key}; the manifest cannot tell them apart`);
    continue;
  }
  seen.add(key);

  const actual = createHash('sha256').update(readFileSync(full)).digest('hex');
  const was = recorded.get(key);
  if (was === undefined) {
    problems.push(`  ${rel}: no entry in sources.sha256 — the committed content predates this file`);
    continue;
  }
  if (was !== actual) {
    problems.push(
      `  ${rel}: changed since the committed content was built.\n` +
      `      source now: ${actual}\n` +
      `      built from: ${was}`);
  }
}

for (const key of recorded.keys()) {
  if (!seen.has(key)) problems.push(`  ${key}: listed in sources.sha256 but no longer exists`);
}

// A description with no compiled counterpart means the rebuild did not write everything it should have.
for (const { rel } of sources) {
  const ext = extname(rel).toLowerCase();
  if (ext !== '.spritefont' && ext !== '.fx') continue;   // a bundled .ttf is an input, not an asset
  const xnb = join(prebuilt, rel.replace(/\.[^.]+$/, '.xnb'));
  if (!existsSync(xnb)) problems.push(`  ${rel}: no compiled ${rel.replace(/\.[^.]+$/, '.xnb')} in prebuilt/`);
}

if (problems.length > 0) {
  console.error('FAILED — the committed content disagrees with its sources:\n');
  console.error(problems.join('\n'));
  console.error('\n  Rebuild on Windows and commit what changes under Content/prebuilt/:');
  console.error('    dotnet msbuild client/src/Mirage.Client.Shell/Mirage.Client.Shell.csproj -t:BuildContent');
  process.exit(1);
}

console.log(`Committed content matches its sources (${sources.length} inputs, ` +
            `${readdirSync(join(prebuilt, 'fonts')).length + readdirSync(join(prebuilt, 'shaders')).length} assets).`);
