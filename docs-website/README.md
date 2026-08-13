# mRemoteNG documentation site

The mRemoteNG documentation site, built with [Docusaurus](https://docusaurus.io/).
Published to GitHub Pages at <https://robertpopa22.github.io/mRemoteNG/>.

## Local development

This project uses [pnpm](https://pnpm.io/). The version is pinned by the
`packageManager` field in [`package.json`](package.json), so `corepack enable` is
enough to get the right one.

```sh
pnpm install
pnpm start
```

`pnpm start` serves the site at <http://localhost:3000/mRemoteNG/> with hot reload.

## Build

```sh
pnpm run build      # static output in ./build
pnpm run serve      # serve the built output locally
pnpm run typecheck  # type check the config/sidebars
```

## Content

Documentation pages live in [`docs/`](docs) as Markdown, with their screenshots in
[`docs/images/`](docs/images) referenced by relative path, so a wrong path fails the
build. Ordering is controlled by [`sidebars.ts`](sidebars.ts).

The site runs in **docs-only mode** — `docs/introduction.md` has `slug: /` and is served
as the site root, so there is no separate landing page under `src/pages`.

`markdown.format` is set to `detect` in [`docusaurus.config.ts`](docusaurus.config.ts), so
`.md` files are parsed as CommonMark rather than MDX. The pages migrated from Sphinx contain
literals such as `<user@domain>` and PowerShell hash tables that MDX would reject as JSX.
Use a `.mdx` extension for any page that genuinely needs components.

These pages were converted from the Sphinx sources in `mRemoteNGDocumentation/`. That tree
is still the upstream copy; edits belong here now.

## Publishing

[`.github/workflows/docs.yml`](../.github/workflows/docs.yml) type checks and builds the
site on every push to `main` that touches `docs-website/`, then deploys it to GitHub Pages.
Pull requests run the same type check and build without deploying.

One-time repository setup: **Settings → Pages → Build and deployment → Source: GitHub
Actions**.
