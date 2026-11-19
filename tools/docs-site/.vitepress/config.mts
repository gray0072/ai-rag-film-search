import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, type DefaultTheme } from "vitepress";
import type MarkdownIt from "markdown-it";

// I21: the site renders the Markdown in docs/ as it is. GitHub stays the first view of the same files, so the pages
// keep GitHub's conventions (README.md as the folder page, relative links to any file in the repository, GitHub
// anchors). This config maps those conventions to the site instead of changing the Markdown.

const repo = "https://github.com/gray0072/ai-rag-film-search";
// Real paths: with a lower-case drive letter in the working directory (VS Code terminal on Windows) VitePress could not
// match pages to their chunks ("Cannot read properties of undefined (reading 'imports')").
const siteDir = fs.realpathSync.native(path.resolve(path.dirname(fileURLToPath(import.meta.url)), ".."));
const repoRoot = path.resolve(siteDir, "../..");
const docsDir = path.join(repoRoot, "docs");

/** GitHub's heading anchors: lower case, punctuation and emoji dropped, every space becomes a dash. */
function githubSlug(text: string): string {
  return text
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{M}\p{N}\p{Pc}\- ]/gu, "")
    .replace(/ /g, "-");
}

function githubUrl(absolute: string, hash: string): string {
  const relative = path.relative(repoRoot, absolute).split(path.sep).join("/");
  const kind = fs.existsSync(absolute) && fs.statSync(absolute).isDirectory() ? "tree" : "blob";
  return `${repo}/${kind}/main/${relative}${hash}`;
}

/**
 * Rewrites a relative link of a page in docs/ for the site:
 * - a file or folder outside docs/, a folder without a README, or a file that is not Markdown → its page on GitHub;
 * - README.md (or a folder with one) → index.md, the page it becomes on the site (see `rewrites`).
 */
function siteHref(href: string, sourceFile: string): string {
  if (/^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith("#") || href.startsWith("/")) return href;

  const hashAt = href.indexOf("#");
  const target = hashAt < 0 ? href : href.slice(0, hashAt);
  const hash = hashAt < 0 ? "" : href.slice(hashAt);
  if (target === "") return href;

  const absolute = path.resolve(path.dirname(sourceFile), decodeURI(target));
  const insideDocs = !path.relative(docsDir, absolute).startsWith("..");
  if (!insideDocs) return githubUrl(absolute, hash);

  const isDirectory = fs.existsSync(absolute) && fs.statSync(absolute).isDirectory();
  if (isDirectory) {
    if (!fs.existsSync(path.join(absolute, "README.md"))) return githubUrl(absolute, hash);
    return `${target.replace(/\/?$/, "/")}index.md${hash}`;
  }
  if (!target.endsWith(".md")) return githubUrl(absolute, hash);
  return target.replace(/(^|\/)README\.md$/, "$1index.md") + hash;
}

function relativeLinksPlugin(md: MarkdownIt): void {
  md.core.ruler.push("filmsearch_relative_links", (state) => {
    const sourceFile: string | undefined = state.env?.path;
    if (!sourceFile) return;
    for (const block of state.tokens) {
      for (const token of block.children ?? []) {
        if (token.type !== "link_open") continue;
        const href = token.attrGet("href");
        if (href) token.attrSet("href", siteHref(href, sourceFile));
      }
    }
  });
}

/** ```mermaid fences become a client-side <Mermaid> component (registered in theme/index.ts). */
function mermaidPlugin(md: MarkdownIt): void {
  const fence = md.renderer.rules.fence!;
  md.renderer.rules.fence = (tokens, index, options, env, self) => {
    const token = tokens[index];
    if (token.info.trim() === "mermaid") {
      return `<Mermaid code="${encodeURIComponent(token.content)}" />`;
    }
    return fence(tokens, index, options, env, self);
  };
}

/** The first "# " heading of a Markdown file, for sidebar entries. */
function titleOf(file: string): string {
  const heading = fs
    .readFileSync(file, "utf8")
    .split(/\r?\n/)
    .find((line) => line.startsWith("# "));
  return heading ? heading.slice(2).trim() : path.basename(file, ".md");
}

/** Sidebar items for every Markdown file of a docs folder, README first, the rest by file name. */
function folderItems(folder: string): DefaultTheme.SidebarItem[] {
  const directory = path.join(docsDir, folder);
  const files = fs
    .readdirSync(directory)
    .filter((name) => name.endsWith(".md"))
    .sort((a, b) => (a === "README.md" ? -1 : b === "README.md" ? 1 : a.localeCompare(b)));
  return files.map((name) => ({
    text: name === "README.md" ? "Overview" : titleOf(path.join(directory, name)),
    link: `/${folder}/${name === "README.md" ? "" : name.replace(/\.md$/, "")}`,
  }));
}

function section(text: string, folder: string, collapsed = false): DefaultTheme.SidebarItem {
  return { text, collapsed, items: folderItems(folder) };
}

export default defineConfig({
  title: "FilmSearch docs",
  description: "A movie store with semantic search and an AI sales assistant: guide, build journal and handbook.",
  lang: "en",
  srcDir: docsDir,
  outDir: "dist",
  cacheDir: ".vitepress/cache",
  base: process.env.DOCS_BASE ?? "/ai-rag-film-search/",
  cleanUrls: true,
  // Every other dead link fails the build; localhost URLs are instructions for a local run.
  ignoreDeadLinks: "localhostLinks",
  rewrites: (id) => id.replace(/(^|\/)README\.md$/, "$1index.md"),
  head: [["meta", { name: "theme-color", content: "#3c8772" }]],
  markdown: {
    anchor: { slugify: githubSlug },
    config: (md) => {
      md.use(relativeLinksPlugin);
      md.use(mermaidPlugin);
    },
  },
  vite: {
    // The Markdown lives outside this folder (no node_modules next to it): resolve the packages its compiled pages
    // import from here, and let the dev server read it.
    plugins: [
      {
        name: "filmsearch-docs-packages",
        enforce: "pre",
        async resolveId(source, importer, options) {
          if (!importer || !/^(vue|vitepress|@vue\/)/.test(source)) return null;
          if (path.relative(docsDir, importer.split("?")[0]).startsWith("..")) return null;
          return this.resolve(source, path.join(siteDir, "package.json"), { ...options, skipSelf: true });
        },
      },
    ],
    server: { fs: { allow: [repoRoot] } },
  },
  themeConfig: {
    nav: [
      { text: "Guide", link: "/guide/", activeMatch: "^/guide/" },
      { text: "Journal", link: "/journal/", activeMatch: "^/journal/" },
      { text: "Handbook", link: "/handbook/", activeMatch: "^/handbook/" },
      {
        text: "Reference",
        items: [
          { text: "Spec", link: "/spec/00-overview" },
          { text: "ADRs", link: "/adr/" },
          { text: "Roadmap", link: "/plan/roadmap" },
          { text: "Runbook: observability", link: "/runbooks/observability" },
          { text: "Learning path", link: "/learning-path" },
          { text: "Prompt history", link: "/prompts/" },
        ],
      },
    ],
    sidebar: {
      "/guide/": [section("Guide", "guide"), section("Advanced", "guide/advanced")],
      "/journal/": [section("Journal", "journal")],
      "/handbook/": [section("Handbook", "handbook")],
      "/spec/": [section("Spec", "spec")],
      "/adr/": [section("ADRs", "adr")],
      "/plan/": [
        { text: "Plan", items: [{ text: "Roadmap", link: "/plan/roadmap" }] },
        section("Iterations", "plan/iterations"),
      ],
      "/prompts/": [
        { text: "Prompt history", items: [{ text: "Overview", link: "/prompts/" }] },
        section("Sessions", "prompts/sessions", true),
      ],
    },
    outline: { level: [2, 3] },
    search: { provider: "local" },
    socialLinks: [{ icon: "github", link: repo }],
    editLink: { pattern: `${repo}/edit/main/docs/:path`, text: "Edit this page on GitHub" },
    footer: { message: "A learning pet project. MIT license.", copyright: "FilmSearch" },
  },
});
