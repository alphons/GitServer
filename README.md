# GitServer

[![CI](https://github.com/alphons/GitServer/actions/workflows/ci.yml/badge.svg)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)

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
- Admin panel for user management, blocked email-pattern configuration for sign-ups, and the built-in Git engine updater (see [Git engine](#git-engine))
- Per-user profile pages with bio and avatar (via Gravatar)

### Internationalization
- Ships with **10 languages** out of the box: English, Dutch, German, French, Spanish, Portuguese, Russian, Chinese, Japanese, Arabic
- Language switcher in the navbar — preference stored in a cookie
- **Extend with your own language** by dropping a single JSON file into the `Localization/` folder — no recompile needed

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
  }
}
```

| Setting | Description |
|---------|-------------|
| `GitServer:RepositoriesPath` | Where bare Git repositories are stored on disk |
| `GitServer:AllowRegistration` | Set to `false` to lock down new sign-ups |
| `GitServer:GitPathPrefix` | URL path segment in front of Git Smart HTTP endpoints (e.g. `/git`); empty serves at the root |
| `GitServer:DefaultPrivateOnAutoCreate` | Visibility of repositories auto-created on first push |
| `GitServer:MaxPushSizeMb` | Max request body size (MB) for a push; `null`/omitted = unlimited |
| `GitServer:ExploreRepoPageSize` / `ExploreUserPageSize` | Items per page on the public `/explore` listings |
| `GitServer:ProfileRepoPageSize` | Items per page on a user's profile repository list |
| `GitServer:IndexRecentReposCount` | Repos shown in the home page's "recent repositories" list |
| `GitServer:GitReleasesApiUrl` | GitHub Releases API endpoint the admin Git updater checks (see [Git engine](#git-engine)) |
| `GitServer:GitReleaseAssetPattern` | Regex used to pick the right release asset (64-bit MinGit) from that endpoint |
| `GitServer:GitExecutableInstallRoot` | Where downloaded MinGit versions are extracted; relative paths resolve against the app's content root |
| `Authentication:KeysPath` | Folder where Data Protection keys are persisted (antiforgery tokens, auth cookies) |
| `Authentication:ProtectKeysWithDpapi` | Encrypt the keys at rest using Windows DPAPI |
| `ConnectionStrings:Default` | SQLite connection string |

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

GitServer uses plain JSON files for translations. To add a new language:

1. Copy `src/GitServer/Localization/en.json` to e.g. `src/GitServer/Localization/ko.json`
2. Set `"__name__"` to the native language name (e.g. `"한국어"`)
3. Translate all the values
4. Restart the server — your language appears in the navbar dropdown automatically

To pin a language to a specific position in the dropdown, add `"__order__": "3"` (lower numbers appear first; English is `1`, Dutch is `2`).

---

## Architecture

GitServer is a single ASP.NET Core 10 application built on Razor Pages.

```
src/GitServer/
├── Controllers/        # Git HTTP protocol (upload-pack, receive-pack)
├── Data/               # EF Core DbContext + SQLite migrations
├── Localization/       # JSON translation files (one per language)
├── Middleware/         # Git Basic Auth middleware
├── Models/             # Domain models (User, Repository, Issue, Comment)
├── Services/           # Business logic (Git, Repository, Markdown, Localization)
├── Pages/              # Razor Pages
│   ├── Auth/           # Login, Register
│   ├── Repo/           # Repository browser, commits, branches, issues
│   ├── User/           # Profile, settings
│   └── Admin/          # User management, Git version updater
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
