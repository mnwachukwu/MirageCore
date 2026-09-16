// Check the README's claims about THIS repository against the repository.
//
//     node .github/checks/check-readme-facts.mjs
//
// The sibling check-seed-counts.mjs guards the size of the shipped world. This one guards the facts
// about the codebase itself — how many projects the solution ties together, the framework it targets,
// how many test suites there are — which drift for the same reason and are caught by nothing else.
//
// A number is only a fact for this check when the README is stating it ABOUT THE CODE. The README's
// level bands describe the seed world's content, not an engine limit, so they belong to the seed check
// even where a constant happens to carry the same number.
//
// It found the README claiming eighteen projects when the solution held twenty-one. Nobody adding a
// test project rereads a sentence in Project Structure.
//
// Everything here is read from files already in this repository, so it needs no checkout but its own
// and costs nothing to run.
//
// A claim that cannot be FOUND is a failure. If a sentence is reworded past recognition this must
// go red rather than quietly verifying nothing — a check that silently stops checking is worse than
// no check, because it also stops anyone worrying about the thing.

import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

function findRepoRoot(start) {
  for (let dir = resolve(start); ; dir = dirname(dir)) {
    if (existsSync(join(dir, 'Mirage.slnx'))) return dir;
    if (dirname(dir) === dir) return resolve(start);
  }
}
const root = findRepoRoot(dirname(fileURLToPath(import.meta.url)));
const readme = readFileSync(join(root, 'README.md'), 'utf8');

const NUMBER_WORDS = {
  1: 'one', 2: 'two', 3: 'three', 4: 'four', 5: 'five', 6: 'six', 7: 'seven', 8: 'eight', 9: 'nine',
  10: 'ten', 11: 'eleven', 12: 'twelve', 13: 'thirteen', 14: 'fourteen', 15: 'fifteen',
  16: 'sixteen', 17: 'seventeen', 18: 'eighteen', 19: 'nineteen', 20: 'twenty',
  21: 'twenty-one', 22: 'twenty-two', 23: 'twenty-three', 24: 'twenty-four', 25: 'twenty-five',
};

const facts = [];

// How many projects the root solution actually ties together.
const slnx = readFileSync(join(root, 'Mirage.slnx'), 'utf8');
const projects = (slnx.match(/<Project\s/g) ?? []).length;
facts.push({
  what: 'projects in Mirage.slnx',
  actual: projects,
  // Spelled out in prose, which is how the README writes it.
  phrase: n => `all ${NUMBER_WORDS[n] ?? n} projects together`,
});

// The framework every project targets. Read from Mirage.Shared rather than a props file, because that
// is the one project everything else references.
const csproj = readFileSync(join(root, 'shared/src/Mirage.Shared/Mirage.Shared.csproj'), 'utf8');
const tfm = csproj.match(/<TargetFramework>net([0-9.]+)<\/TargetFramework>/);
facts.push({
  what: 'target framework',
  actual: tfm ? tfm[1].replace(/\.0$/, '') : null,
  phrase: v => `.NET ${v}`,
});

// How many test suites sit under tests/src/. The number is prose in two documents and in three comments in
// the CI workflow, and nothing but this ties any of them to the folder they describe.
const suites = readdirSync(join(root, 'tests', 'src'), { withFileTypes: true })
  .filter(e => e.isDirectory() && existsSync(join(root, 'tests', 'src', e.name, `${e.name}.csproj`)))
  .length;
facts.push({
  what: 'test suites',
  actual: suites,
  phrase: n => `the ${NUMBER_WORDS[n] ?? n} test suites`,
  files: ['README.md', 'docs/testing.md'],
});

// How many declaration seams ICoreBuilder offers. modules/README.md claims the demo game uses all of
// them, which is a claim about two files at once: adding a seam and not using it in survey/ makes that
// sentence false, and nothing else would say so.
//
// Add* and Set* both count. A seam that takes a LIST of things is an Add; one that takes a single
// number a game has exactly one of - the action bar's width - is a Set, and it is no less a
// declaration. ExtendFamily is neither: it refines a family somebody else declared.
const coreModule = readFileSync(join(root, 'shared/src/Mirage.Shared/Extensibility/ICoreModule.cs'), 'utf8');
const builderBody = coreModule.match(/interface ICoreBuilder\s*\{([\s\S]*?)\n\}/);
const seams = builderBody
  ? (builderBody[1].match(/^\s*(?:void (?:Add|Set)\w+\(|[\w.]+(?:<[\w.]+>)?\s+\w+\s*\{\s*get;)/gm) ?? []).length
  : null;
facts.push({
  what: 'declaration seams on ICoreBuilder',
  actual: seams,
  phrase: n => `all ${NUMBER_WORDS[n] ?? n} seams`,
  files: ['modules/README.md'],
});

const problems = [];
const passed = [];

for (const { what, actual, phrase, files = ['README.md'] } of facts) {
  if (actual === null || actual === undefined) {
    problems.push(`  ${what}: could not read the real value from the repository`);
    continue;
  }
  const expected = phrase(actual).toLowerCase();
  // A fact stated in several documents has to hold in every one of them: the copy nobody updated is
  // exactly the copy a reader will find first.
  const missing = files.filter(f => !readFileSync(join(root, f), 'utf8').toLowerCase().includes(expected));
  if (missing.length === 0) {
    passed.push(`  ${what}: ${actual}`);
    continue;
  }
  problems.push(
    `  ${what}: the repository says ${actual}, so ${missing.join(' and ')} should contain "${phrase(actual)}" — ` +
    `${missing.length > 1 ? 'they do' : 'it does'} not.\n` +
    `      Either the number drifted, or the sentence moved and this check needs its new wording.`);
}

if (problems.length > 0) {
  console.error('FAILED — the README disagrees with the repository:\n');
  console.error(problems.join('\n'));
  process.exit(1);
}

console.log(`README matches the repository on ${passed.length} facts:\n${passed.join('\n')}`);
