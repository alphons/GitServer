# GitServer

[![Version](https://img.shields.io/badge/version-1.1.0-blue)](https://github.com/alphons/GitServer/releases)
[![License](https://img.shields.io/badge/license-MIT-orange)](LICENSE)
[![CI](https://github.com/alphons/GitServer/actions/workflows/ci.yml/badge.svg)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)
[![Tests](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/alphons/348dbf4f9472d6069955373db1ef6670/raw/gitserver-tests.json)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/alphons/348dbf4f9472d6069955373db1ef6670/raw/gitserver-coverage.json)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)

> **Your code. Your server. Your rules.**
> A fast, lightweight, self-hosted Git platform — completely free and open source.

GitServer gives you everything you need to host your own Git repositories without sending your code to the cloud, paying monthly fees, or trusting a third party with your intellectual property. Deploy it on a Windows VPS or your own hardware in minutes.

---

## Why GitServer?

Because your code doesn't belong to anyone else.

- **100% free** — no plans, no tiers, no credit card required. Ever.
- **Open source** — read every line, modify anything, contribute back.
- **Self-hosted** — a single Windows machine or VPS, nothing else.
- **Lightweight** — a single binary, a single SQLite database, zero external dependencies.
- **No telemetry** — your repositories never leave your machine.

---

## Features

### Repository Management
- Create public and private Git repositories
- Full HTTP/HTTPS Git protocol support — clone, push, pull with any standard Git client
- Browse the file tree, view files and diffs directly in the browser
- README rendering with full Markdown support
- Download any branch as a ZIP archive
- Branch and tag overview

### Commit History
- Paginated commit log per branch
- Detailed commit view with syntax-highlighted diffs
- Changed file summary per commit

### Issue Tracker
- Built-in issue tracker per repository
- Open and close issues, add comments
- Markdown support in issue bodies and comments

### User Management
- User registration and authentication via ASP.NET Core Identity (email confirmation, password reset)
- Groups with members, usable as a unit when granting repository access
- Per-repository access control — grant individual users or whole groups **Read** or **Write** access to private repos
- Per-user profile pages with bio, company, country and avatar (via Gravatar or a custom URL)

### Admin Panel
- **Users** — search/filter/paginate all accounts, create users directly, edit username/display name/email/password/role, enable or disable an account, and validate a pending (unconfirmed) registration — all from one modal, no page reloads
- **Blocked emails** — wildcard email-pattern blocklist for sign-ups (e.g. `*@spam.net`)
- **Git version** — the built-in Git engine updater (see [Git engine](#git-engine))
- **Settings** — site-wide toggles, editable at runtime with no restart required:
  - Allow user registration
  - Allow user repository creation
  - Allow push to create repositories (auto-create on first `git push`)
  - Allow push for anonymous (unauthenticated) repositories
  - Show commit author avatar

### Internationalization
- Ships with **10 languages** out of the box: English, Dutch, German, French, Spanish, Portuguese, Russian, Chinese, Japanese, Arabic
- Language switcher in the navbar — preference stored in a cookie
- Transactional emails (registration, password reset) are localized HTML templates, not just translated strings
- **Extend with your own language** by adding a folder under `Localization/` — no recompile needed (see [Adding a Language](#adding-a-language))

### Security
- CSRF protection on all forms
- Secure HTTP-only cookies with configurable expiry
- Git push/pull protected by Basic Authentication
- Data Protection API keys persisted to disk, so sessions and tokens survive app restarts

---

## Screenshots

> *Browse repositories, view code, manage issues — all from a clean dark-themed UI.*

---

## Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0 or later |
| OS | Windows (win-x64) |

No system-wide Git install needed — see [Git engine](#git-engine) below. No Docker required. No Postgres. No Redis. No message queue.

---

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/yourname/gitserver.git
cd gitserver
```

### 2. Configure

Edit `src/GitServer/appsettings.json`:

```json
{
  "GitServer": {
    "RepositoriesPath": "D:\\GitRepos",
    "AllowRegistration": true,
    "GitPathPrefix": "",
    "DefaultPrivateOnAutoCreate": true,
    "MaxPushSizeMb": 2048,
    "ExploreRepoPageSize": 20,
    "ExploreUserPageSize": 20,
    "ProfileRepoPageSize": 20,
    "AdminUsersPageSize": 10,
    "IndexRecentReposCount": 5,
    "GitReleasesApiUrl": "https://api.github.com/repos/git-for-windows/git/releases",
    "GitReleaseAssetPattern": "^MinGit-[\\d.]+-64-bit\\.zip$",
    "GitExecutableInstallRoot": "App_Data\\git"
  },
  "Authentication": {
    "KeysPath": "C:\\Data\\DataProtection\\Keys",
    "ApplicationName": "GitServer",
    "ProtectKeysWithDpapi": false
  },
  "ConnectionStrings": {
    "Default": "Data Source=gitserver.db"
  },
  "EmailService": {
    "ClientId": "",
    "ClientSecret": "",
    "SmtpHost": "",
    "SmtpPort": 25,
    "EnableSsl": false,
    "FromEmail": ""
  }
}
```

| Setting | Description |
|---------|-------------|
| `GitServer:RepositoriesPath` | Where bare Git repositories are stored on disk |
| `GitServer:AllowRegistration` | Seeds the DB-backed "Allow user registration" toggle the first time the app runs; after that it's live-editable under **Admin → Settings** |
| `GitServer:GitPathPrefix` | URL path segment in front of Git Smart HTTP endpoints (e.g. `/git`); empty serves at the root |
| `GitServer:DefaultPrivateOnAutoCreate` | Visibility of repositories auto-created on first push |
| `GitServer:MaxPushSizeMb` | Max request body size (MB) for a push; `null`/omitted = unlimited |
| `GitServer:ExploreRepoPageSize` / `ExploreUserPageSize` | Items per page on the public `/explore` listings |
| `GitServer:ProfileRepoPageSize` | Items per page on a user's profile repository list |
| `GitServer:AdminUsersPageSize` | Items per page on the **Admin → Users** listing |
| `GitServer:IndexRecentReposCount` | Repos shown in the home page's "recent repositories" list |
| `GitServer:GitReleasesApiUrl` | GitHub Releases API endpoint the admin Git updater checks (see [Git engine](#git-engine)) |
| `GitServer:GitReleaseAssetPattern` | Regex used to pick the right release asset (64-bit MinGit) from that endpoint |
| `GitServer:GitExecutableInstallRoot` | Where downloaded MinGit versions are extracted; relative paths resolve against the app's content root |
| `Authentication:KeysPath` | Folder where Data Protection keys are persisted (antiforgery tokens, auth cookies) |
| `Authentication:ProtectKeysWithDpapi` | Encrypt the keys at rest using Windows DPAPI |
| `ConnectionStrings:Default` | SQLite connection string |
| `EmailService:*` | SMTP settings used to send registration and password-reset emails; leave `SmtpHost` empty to disable outgoing email (registration links then just won't be delivered) |

> Everything above except `EmailService` and the identity/DB plumbing is a startup-time default. The five toggles under **Admin → Settings** (registration, user repo creation, push-to-create, anonymous push, commit avatars) live in the database instead, so an admin can flip them from the browser without editing config or restarting the app.

### 3. Run

```bash
cd src/GitServer
dotnet run
```

The database is created and migrated automatically on first start. Open your browser at `http://localhost:5000`.

The **first user to register becomes admin** automatically. Sign in and visit **Admin → Git version** to install a Git engine — see [Git engine](#git-engine) below.

### 4. Production deployment

```bash
dotnet publish -c Release -o ./publish
./publish/GitServer
```

Reverse-proxy with nginx or Caddy for HTTPS — GitServer itself speaks plain HTTP and lets your proxy handle TLS.

**Example nginx config:**

```nginx
server {
    listen 443 ssl;
    server_name git.yourdomain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        # Required for git push/pull streaming
        proxy_request_buffering off;
        proxy_buffering off;
    }
}
```

---

## Git engine

GitServer doesn't require a system-wide Git install. Under **Admin → Git version** an admin can:

- Check GitHub for the latest [Git for Windows](https://github.com/git-for-windows/git) release and download the portable **MinGit** distribution with a live progress bar
- Activate it immediately — no app restart needed, the running server switches to it right away
- The very first version installed activates itself automatically
- Each install is trimmed down to just `mingw64\bin` (git.exe, its runtime DLLs and the actual subcommand binaries) plus `mingw64\share\licenses` for attribution — everything else (docs, the interactive GUI credential manager, submodule/mergetool scripts never used by a server) is stripped to save space
- Browse the kept license files for the current install right from the same page
- Old or manually-deleted installs are detected and cleaned up automatically

Until an admin installs a version, Git operations simply report "not installed" instead of failing — the rest of the app keeps working.

---

## Testing

```bash
dotnet test
```

`tests/GitServer.Tests` covers the Git process wrapper (init, log, tree, upload-pack/receive-pack) against a real temporary repository, and the repository read/write access-control logic (owner, direct user access, group access) against a real SQLite database. CI runs the full suite on every push and pull request — see the badge at the top of this file.

---

## Adding a Language

Each language is a folder under `src/GitServer/Localization/`. To add a new one:

1. Copy `src/GitServer/Localization/en/` to e.g. `src/GitServer/Localization/ko/`
2. In `ko/strings.json`, set `"__name__"` to the native language name (e.g. `"한국어"`) and translate all the values
3. Translate the HTML files under `ko/emails/` (registration and password-reset emails); they share the layout in `Localization/_email-layout.html` and use `{{placeholder}}` tokens
4. Restart the server — your language appears in the navbar dropdown automatically

To pin a language to a specific position in the dropdown, add `"__order__": "3"` (lower numbers appear first; English is `1`, Dutch is `2`). A key missing from a translation falls back to English automatically, so a partial translation still works.

---

## Architecture

GitServer is a single ASP.NET Core 10 application built on Razor Pages.

```
src/GitServer/
├── Controllers/        # Git HTTP protocol (upload-pack, receive-pack)
├── Data/               # EF Core DbContext + SQLite migrations
├── Localization/       # One folder per language: strings.json + emails/*.html
├── Middleware/         # Git Basic Auth middleware, site-settings enforcement
├── Models/             # Domain models (User, Repository, Issue, Comment, Group, SiteSettings)
├── Services/           # Business logic (Git, Repository, Markdown, Localization, SiteSettings)
├── Pages/              # Razor Pages
│   ├── Auth/           # Login, Register, password reset
│   ├── Repo/           # Repository browser, commits, branches, tags, issues
│   ├── User/           # Profile, settings, groups
│   └── Admin/          # Users, blocked emails, Git version updater, site settings
└── wwwroot/            # Static assets only (css, js, favicon)
tests/GitServer.Tests/   # Integration tests (xUnit)
```

**Stack:**
- ASP.NET Core 10 Razor Pages
- Entity Framework Core with SQLite
- ASP.NET Core Identity
- Git operations run as plain `git.exe` subprocesses (`Process.Start`) — no native Git library dependency
- Zero JavaScript frameworks — vanilla JS only

---

## Roadmap

- SSH key authentication
- Webhook support
- Organization/team accounts
- Git LFS support
- Repository forking

---

## Contributing

Pull requests are welcome. For major changes, open an issue first to discuss what you'd like to change.

1. Fork the repo
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push and open a pull request

---

## License

MIT License — do whatever you want with it.

---

<p align="center">
  Built with .NET 10 &nbsp;·&nbsp; Open Source &nbsp;·&nbsp; Free Forever
</p>
