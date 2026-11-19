// I21: checks every relative link in the repository's Markdown: the target file or folder must exist, and an anchor
// into a Markdown file must match one of its headings (GitHub's anchor rules, the same as the docs site's slugify).
// Usage: node tools/docs-site/check-links.mjs   (exit code 1 when a link is broken)
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const skipDirectories = new Set([".git", "node_modules", "bin", "obj", "dist", "TestResults", ".vitepress", ".cache"]);

function markdownFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) return skipDirectories.has(entry.name) ? [] : markdownFiles(full);
    return entry.name.endsWith(".md") ? [full] : [];
  });
}

/** Lines of a Markdown file outside fenced code blocks, with their line numbers. */
function proseLines(text) {
  const result = [];
  let fence = null;
  text.split(/\r?\n/).forEach((line, index) => {
    const marker = line.match(/^\s*(`{3,}|~{3,})/);
    if (marker) {
      if (fence === null) fence = marker[1];
      else if (marker[1][0] === fence[0] && marker[1].length >= fence.length && line.trim() === marker[1]) fence = null;
      return;
    }
    if (fence === null) result.push({ line, number: index + 1 });
  });
  return result;
}

/** GitHub's heading anchor: text content, lower case, punctuation and emoji dropped, spaces become dashes. */
function slug(heading) {
  const text = heading
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/`([^`]*)`/g, "$1")
    .replace(/<[^>]+>/g, "")
    .replace(/(\*\*|__)(.*?)\1/g, "$2")
    .replace(/(\*|_)(.*?)\1/g, "$2");
  return text
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{M}\p{N}\p{Pc}\- ]/gu, "")
    .replace(/ /g, "-");
}

const anchorCache = new Map();
function anchorsOf(file) {
  if (anchorCache.has(file)) return anchorCache.get(file);
  const anchors = new Set();
  const seen = new Map();
  for (const { line } of proseLines(fs.readFileSync(file, "utf8"))) {
    const heading = line.match(/^#{1,6}\s+(.*?)\s*#*\s*$/);
    if (heading) {
      const base = slug(heading[1]);
      const count = seen.get(base) ?? 0;
      seen.set(base, count + 1);
      anchors.add(count === 0 ? base : `${base}-${count}`);
    }
    for (const html of line.matchAll(/<a\s+(?:id|name)="([^"]+)"/g)) anchors.add(html[1]);
  }
  anchorCache.set(file, anchors);
  return anchors;
}

function linksOf(text) {
  const links = [];
  for (const { line, number } of proseLines(text)) {
    const prose = line.replace(/`[^`]*`/g, "");
    for (const match of prose.matchAll(/\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)/g)) links.push({ href: match[1], number });
    const reference = prose.match(/^\s*\[[^\]]+\]:\s+(\S+)/);
    if (reference) links.push({ href: reference[1], number });
  }
  return links;
}

const broken = [];
const files = markdownFiles(repoRoot);
let checked = 0;
for (const file of files) {
  for (const { href, number } of linksOf(fs.readFileSync(file, "utf8"))) {
    if (/^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith("/")) continue;
    checked++;
    const hashAt = href.indexOf("#");
    const target = hashAt < 0 ? href : href.slice(0, hashAt);
    const anchor = hashAt < 0 ? "" : decodeURIComponent(href.slice(hashAt + 1));
    const absolute = target === "" ? file : path.resolve(path.dirname(file), decodeURI(target));
    const where = `${path.relative(repoRoot, file)}:${number}`;
    if (!fs.existsSync(absolute)) {
      broken.push(`${where}: ${href} (no such file)`);
      continue;
    }
    if (anchor && absolute.endsWith(".md") && !anchorsOf(absolute).has(anchor)) {
      broken.push(`${where}: ${href} (no heading #${anchor})`);
    }
  }
}

if (broken.length > 0) {
  console.error(`${broken.length} broken link(s) in ${files.length} Markdown files:`);
  for (const line of broken) console.error(`  ${line}`);
  process.exit(1);
}
console.log(`OK: ${checked} relative links in ${files.length} Markdown files.`);
