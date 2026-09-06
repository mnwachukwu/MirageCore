// Check that every relative link in the repository's Markdown actually resolves.
//
//     node tools/check-doc-links.mjs
//
// Node rather than C#, for one reason: every GitHub-hosted runner ships Node already, so this costs
// nothing to run. A .NET file-based program was the first version and read better beside the rest of
// the codebase, but it needed a setup-dotnet step — .NET 10 is not preinstalled and file-based
// programs are a .NET 10 feature, so the SDK the image happens to carry cannot be assumed. That step
// took thirty seconds to check sixty lines' worth of links. No dependencies and no package.json
// either: this imports nothing outside Node's standard library, so there is no install step and
// nothing to keep up to date.
//
// WHY THIS EXISTS. Prose rots differently from code. A renamed file or a reorganized document breaks
// links that nothing compiles and no test covers, and the person who moved the file is the least
// likely to notice. Splitting the documentation out of the README broke four links and left one
// reference pointing at a heading that had stopped existing some time earlier — none of which any
// other check in this repository would ever have reported.
//
// Three kinds of link are checked:
//
//     a path              [the props file](Directory.Build.props)
//     an in-page anchor   [platform support](#platform-support)
//     both                [icons](docs/branding.md#icons)
//
// External links (http, https, mailto) are deliberately NOT followed. Reaching the network would
// make a documentation check depend on somebody else's uptime, and a check that fails at random is
// a check people learn to ignore.
//
// CASE MATTERS, even on Windows. existsSync is case-insensitive on Windows and macOS, so a link
// written `Docs/Building.md` resolves happily on the machine that wrote it and 404s on Linux and on
// github.com. Comparing against real directory entries is the only way a Windows machine catches
// that — the same lesson UserPaths teaches, arriving from a different direction.
//
// Exits 0 when every link resolves and 1 when any does not, so it works as a CI step unchanged.

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve, sep } from 'node:path'

// Build output, dependencies and version control hold Markdown nobody maintains.
const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', '.git', '.vs', 'dist', 'TestResults', '.github'])

// [text](target) — and ![image](target), which needs no special case because it is checked the same
// way. Reference-style [text][label] is not used in this repository.
const LINK = /\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g
const HEADING = /^#{1,6}[ \t]+(.+?)[ \t]*#*$/gm

// Walk up from wherever this was invoked until the repository root shows itself.
function findRepoRoot(start) {
  for (let dir = resolve(start); ; dir = dirname(dir)) {
    if (existsSync(join(dir, '.git')) || existsSync(join(dir, 'Mirage.slnx'))) return dir
    if (dirname(dir) === dir) return resolve(start)
  }
}

// GitHub's anchor for a heading: strip formatting, lowercase, spaces to hyphens. Close enough for
// the shapes this repository uses — it drops inline code backticks and link syntax first, because
// "## The `data/` folder" anchors as "the-data-folder".
function slug(heading) {
  return heading
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9 _-]/g, '')
    .trim()
    .replace(/ /g, '-')
}

// Every anchor a file offers, including GitHub's -1/-2 suffixes for repeated headings.
function anchorsOf(markdown) {
  const seen = new Map()
  const out = new Set()
  for (const [, heading] of markdown.matchAll(HEADING)) {
    const base = slug(heading)
    const n = seen.get(base) ?? 0
    out.add(n === 0 ? base : `${base}-${n}`)
    seen.set(base, n + 1)
  }
  return out
}

// True when every segment matches a real directory entry's exact spelling.
function resolvesCaseExact(repoRoot, relativeToRoot) {
  let current = repoRoot
  for (const part of relativeToRoot.split(/[\\/]/)) {
    if (part === '' || part === '.') continue
    if (part === '..') { current = dirname(current); continue }
    let entries
    try { entries = readdirSync(current) } catch { return false }
    if (!entries.includes(part)) return false
    current = join(current, part)
  }
  return true
}

function* markdownFiles(dir) {
  const entries = readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : 1)
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) yield* markdownFiles(join(dir, entry.name))
    } else if (entry.name.toLowerCase().endsWith('.md')) {
      yield join(dir, entry.name)
    }
  }
}

const repoRoot = findRepoRoot(process.cwd())
const problems = []
let checked = 0

for (const path of markdownFiles(repoRoot)) {
  const relFile = relative(repoRoot, path).split(sep).join('/')
  const text = readFileSync(path, 'utf8')
  const anchorsHere = anchorsOf(text)

  for (const [, target] of text.matchAll(LINK)) {
    if (/^(https?:|mailto:|<)/.test(target)) continue

    const hash = target.indexOf('#')
    const pathPart = hash < 0 ? target : target.slice(0, hash)
    const anchor = hash < 0 ? '' : target.slice(hash + 1)
    checked++

    if (pathPart === '') {                       // an anchor within this same file
      if (anchor && !anchorsHere.has(anchor)) problems.push([relFile, target, 'no heading with that anchor'])
      continue
    }

    const targetAbs = resolve(dirname(path), pathPart)
    if (!existsSync(targetAbs)) {
      problems.push([relFile, target, 'no such file'])
      continue
    }
    if (!resolvesCaseExact(repoRoot, relative(repoRoot, targetAbs))) {
      problems.push([relFile, target, 'exists but the spelling differs; this 404s on Linux and on GitHub'])
      continue
    }
    if (anchor && targetAbs.toLowerCase().endsWith('.md') && statSync(targetAbs).isFile()) {
      if (!anchorsOf(readFileSync(targetAbs, 'utf8')).has(anchor)) {
        problems.push([relFile, target, 'file exists but has no heading with that anchor'])
      }
    }
  }
}

for (const [file, target, why] of problems) console.log(`${file}: ${target} -> ${why}`)
console.log(`\n${checked} relative link(s) checked, ${problems.length} broken`)
process.exit(problems.length === 0 ? 0 : 1)
