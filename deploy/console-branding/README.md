# Console branding (Gayvelopers) for Nakama 3.42

The Nakama console UI is **compiled into the Go binary** (Vite bundle served from `:7351`), so there is no
theme file to edit and no supported way to drop in a logo without rebuilding the server. This folder brands
the console **at the nginx layer**: static assets are served from disk and two tags are injected into the
console's HTML, while a small script keeps the logo/copy in place across the SPA's re-renders.

```
deploy/console-branding/
├── icon.svg                 # square mark (favicon)
├── logo.svg                 # wordmark for light backgrounds (login page)
├── logo-dark.svg            # wordmark for the dark sidebar
├── banner.svg               # right-hand hero panel on the login screen
├── brand.css                # flat skin: paper/ink/lime, inputs, button, badge
├── brand.js                 # title, favicon, logo swap, copy, route flag
├── nginx-console-branding.conf
├── install.sh
└── README.md
```

## How it works

| Layer | What it does |
|---|---|
| `nginx` `sub_filter` | rewrites `<title>Nakama</title>` and appends `brand.css` + `brand.js` before `</head>` |
| `location /branding/` | serves the SVGs/CSS/JS straight from `/var/www/nakama-branding` |
| `brand.js` | swaps the data-URI logos (`img.h-10` on login, `img.h-7` in the sidebar), replaces the H1, adds the “private server” badge, sets the favicon, and marks `documentElement.dataset.gvRoute` |
| `brand.css` | every rule is scoped to `[data-gv-route="login"]`, so the console's own screens are untouched apart from the sidebar wordmark |

Palette is deliberately flat — ink `#0E0F12`, paper `#F1F1EC`, lime `#C8F560`. No gradients anywhere.

## Install on a server that runs the Docker stack

```bash
sudo mkdir -p /var/www/nakama-branding
sudo cp icon.svg logo.svg logo-dark.svg banner.svg brand.css brand.js /var/www/nakama-branding/
sudo chmod -R a+r /var/www/nakama-branding

# paste nginx-console-branding.conf into the *console* server block
# (the one that proxies to 127.0.0.1:7351), then:
sudo nginx -t && sudo systemctl reload nginx

curl -s https://<console-host>/ | grep -E 'brand\.(css|js)'
curl -s -o /dev/null -w '%{http_code}\n' https://<console-host>/branding/icon.svg   # 200
```

`install.sh` does the copy for you; the nginx edits are intentionally left visible (they touch the server
block that also fronts the API, so they should be reviewed before reloading).

## Gotchas that cost time

- **`proxy_set_header Accept-Encoding "";` is mandatory.** Nakama's gateway gzips the SPA HTML; nginx cannot
  `sub_filter` a gzipped body, so without this the injection silently does nothing.
- **`sub_filter_once off`** — there are two replacements (title + `</head>`), the default `on` would only apply
  the first one.
- **Button colours are inline CSS variables.** The console ships `style="--x-button-bg: rgb(107 89 238)"` on the
  sign-in button, so overriding them from a stylesheet needs `!important` on the custom property itself:
  `--x-button-bg: #0E0F12 !important;`. Overriding `background-color` does nothing.
- **The logos are inline `data:image/svg+xml` URIs**, not files: one on the login page (`img.h-10`) and one in
  the sidebar (`img.h-7`, inside `a[href="#/"]`). `brand.js` rewrites `src` and marks the node so re-renders
  do not fight it.
- **The sidebar is dark**: the light wordmark disappears on it, hence `logo-dark.svg`. Pick the variant by
  context, not by theme class — the sidebar is dark even in light mode.
- **It is a Vue SPA.** Anything you patch can be re-rendered at any moment, so every change is re-applied from
  a debounced `MutationObserver`; the stylesheet is scoped by `data-gv-route`, which the script sets.
- **Cache-bust on every change**: bump `brand.css?v=` / `brand.js?v=` in the nginx snippet, otherwise browsers
  keep the old skin and it looks like nothing happened.
- The right-hand panel only exists at `lg` and up; on phones the login page is a single column and the banner
  is intentionally not shown.

## Re-branding for another product

1. Replace the four SVGs (keep the same file names and roughly the same viewBoxes: wordmark `340×64`,
   banner `1200×1500`).
2. Edit the palette at the top of `brand.css` and the strings in `brand.js` (title, H1, subtitle, badge).
3. Bump the `?v=` numbers in the nginx snippet, reload nginx, hard-refresh once.

## Verification used here

- `curl` shows the injected tags and `Gayvelopers · Nakama Console` as the document title.
- Real browser at 1440×900: banner panel renders, button is ink with lime label, badge shows
  “Gayvelopers · private server · online”, no console errors.
- Sign-in with the real admin account still lands on the dashboard (`#/`), title becomes
  `Gayvelopers · Nakama Console`, sidebar wordmark is the light variant.
- 390 px viewport: no horizontal scroll, form usable, no banner.
