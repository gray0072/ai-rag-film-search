# Documentation site

> [Docs](../../docs/README.md) › Documentation site

The website at **https://gray0072.github.io/ai-rag-film-search/** is the Markdown in [docs/](../../docs/README.md),
rendered by [VitePress](https://vitepress.dev/) with navigation, local search, Mermaid diagrams and dark mode. Nothing
is written twice: GitHub stays the first view of the same files (I21, [plan](../../docs/plan/iterations/I21-documentation.md)).

## Commands

Node 24, in this folder:

```bash
npm install            # once
npm run dev            # live preview at http://localhost:5173/ai-rag-film-search/
npm run build          # static site in dist/ (fails on a link to a page that does not exist)
npm run preview        # serve dist/
npm run check-links    # every relative link and anchor in all Markdown of the repository (no install needed)
```

The [Docs workflow](../../.github/workflows/docs.yml) runs the link check and the build on every change to Markdown
and publishes the site to GitHub Pages on pushes to `main` (repository settings → Pages → source "GitHub Actions").

## How the Markdown is mapped

The pages keep GitHub's conventions; [.vitepress/config.mts](.vitepress/config.mts) adapts the site to them:

| On GitHub | On the site |
|---|---|
| `README.md` is a folder's page | rewritten to `index.md` (`rewrites`); links to `README.md` or to a folder with one follow |
| A relative link to a file outside `docs/` (code, `evals/`, `deploy/`, the root README) | a link to that file on GitHub |
| A link to a folder without a README, or to a file that is not Markdown | a link to it on GitHub |
| Heading anchors (`## I05 — AI provider layer` → `#i05--ai-provider-layer`) | the same slugs (GitHub's rule instead of VitePress's) |
| ` ```mermaid ` code blocks | rendered in the browser by the `Mermaid` component, again on a theme switch |

The sidebar is built from the files: one group per folder, entries titled by each file's first heading.

## Pitfalls

- The Markdown lives outside this folder, so there is no `node_modules` next to it. A small resolver plugin in the
  config resolves `vue` and `vitepress` imports of the compiled pages from here.
- Paths are real paths (`fs.realpathSync.native`): with a lower-case drive letter in the working directory (the VS Code
  terminal on Windows) VitePress could not match pages to their chunks ("Cannot read properties of undefined (reading
  'imports')").
- Vue compiles every page: a raw tag in prose such as `<Title>` (outside code) breaks the build. Write placeholders
  without angle brackets or in backticks; GitHub hides such tags too.
- `localhost` links are allowed (local run instructions); every other dead page link fails the build.

---

**See also:** [Docs hub](../../docs/README.md) · [I21 plan](../../docs/plan/iterations/I21-documentation.md)
