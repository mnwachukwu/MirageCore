// Check the README's claims about THIS repository against the repository.
//
//     node .github/checks/check-readme-facts.mjs
//
// The sibling check-seed-counts.mjs guards the size of the shipped world. This one guards the facts
// about the codebase itself — how many projects the solution ties together, the level ceiling, the
// framework — which drift for the same reason and are caught by nothing else.
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

/** A `const <type> <Name> = <value>;` from a source file. */
function constant(relPath, name) {
  const file = join(root, relPath);
  if (!existsSync(file)) return null;
  const m = readFileSync(file, 'utf8').match(new RegExp(`const\\s+\\w+\\s+${name}\\s*=\\s*([0-9.]+)`));
  return m ? Number(m[1]) : null;
}

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

facts.push({
  what: 'level ceiling',
  actual: constant('shared/src/Mirage.Shared/Constants.cs', 'MaxLevel'),
  phrase: n => `${n} levels`,
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
