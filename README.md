# GitServer

[![Version](https://img.shields.io/github/v/tag/alphons/GitServer?label=version&sort=semver&color=blue)](https://github.com/alphons/GitServer/tags)
[![License](https://img.shields.io/badge/license-MIT-orange)](LICENSE)
[![CI](https://github.com/alphons/GitServer/actions/workflows/ci.yml/badge.svg)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)
[![Tests](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/alphons/348dbf4f9472d6069955373db1ef6670/raw/gitserver-tests.json)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/alphons/348dbf4f9472d6069955373db1ef6670/raw/gitserver-coverage.json)](https://github.com/alphons/GitServer/actions/workflows/ci.yml)

> **Your code. Your server. Your rules.**
> A fast, lightweight, self-hosted Git platform — completely free and open source.

**Current version: 1.18.0**

GitServer gives you everything you need to host your own Git repositories without sending your code to the cloud, paying monthly fees, or trusting a third party with your intellectual property. Deploy it on a Windows VPS or your own hardware in minutes.

---

## Why GitServer?

Because your code doesn't belong to anyone else.

- **100% free** — no plans, no tiers, no credit card required. Ever.
- **Open source** — read every line, modify anything, contribute back.
- **Self-hosted** — a single Windows machine or VPS, nothing else.
- **Lightweight** — a single binary and a single SQLite database, zero external dependencies. Prefer SQL Server? Switch with one setting (see [Choosing a database](#choosing-a-database)).
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
- Account area with three tabs: **Account settings**, **API keys** and **Access tokens**
- **Access tokens** — use a personal token instead of your password for git over HTTPS; create several, set an expiry, revoke any time
- **API keys** — call the [JSON API](#json-api) as yourself with an `X-Api-Key` header; create as many as you like, enable/disable or delete each one, and they expire automatically (90 days by default, set by the admin). A key can be **read-only**: it may only read, every request that changes something is refused
- Accounts lock temporarily after too many wrong passwords, on the web and over git (configurable)
- User and group names cannot clash with the site's own URLs — see [Reserved names](#reserved-names)

### Admin Panel
- **Users** — search/filter/paginate all accounts, create users directly, edit username/display name/email/password/role, enable or disable an account, and validate a pending (unconfirmed) registration — all from one modal, no page reloads
- **Blocked emails** — wildcard email-pattern blocklist for sign-ups (e.g. `*@spam.net`)
- **Audit log** — who did what: administrative actions, API key changes and (at most once an hour per key) API key use, filterable and paged
- **Reserved names** — the user and group names nobody may take (see [Reserved names](#reserved-names))
- **Git version** — the built-in Git engine updater (see [Git engine](#git-engine))
- **Settings** — site-wide settings, editable at runtime with no restart required:
  - Allow user registration
  - Allow user repository creation
  - Allow push to create repositories (auto-create on first `git push`)
  - Allow push for anonymous (unauthenticated) repositories
  - Show commit author avatar
  - API key lifetime in days (default 90) — applies to newly created keys

### Site layout
- Everything you click on lives under **`/dashboard`** (`/dashboard/Explore`, `/dashboard/User/<name>`, `/dashboard/Admin/Users`, …)
- Repositories keep their natural URLs: **`/<owner>/<repo>`** (also the git clone URL)
- The JSON API lives under **`/api`**
- The optional `GitPathPrefix` only moves the git Smart HTTP endpoints, never the API

### Reserved names
A user or group name may never collide with the first URL segment the site itself uses. The list has two parts:
- **Built in (read-only):** `api`, `dashboard`, the git path prefix and every folder in `wwwroot` (`css`, `js`, …) — computed, so nothing to maintain
- **Editable wildcard patterns** under **Admin → Reserved names** (`*` = any characters, `?` = one character, case-insensitive), pre-filled with `user*` and `admin*`

The check applies when a name is created or changed; existing accounts and groups are left alone.

### JSON API
Everything the dashboard does through JavaScript is a plain JSON API under `/api`, usable from scripts too.

- **Authentication:** send an API key in the `X-Api-Key` header (create one under **Dashboard → User → API keys**). The key acts as its owner, with the owner's rights — except that a key cannot manage API keys. A wrong, disabled or expired key always gets `401`. A **read-only** key may only use `GET`; anything else gets `403`.
- **Limits:** per IP address, 300 requests a minute by default (`ApiRequestsPerMinute`); beyond that the API answers `429` with a `Retry-After` header.
- **Verbs:** only `GET` and `POST` are used (`POST /api/…/{id}/update`, `…/{id}/delete`).
- **Documentation:** the OpenAPI description, with typed request/response shapes and per-endpoint summaries, is served at **`/api/openapi.json`**.

```bash
curl -H "X-Api-Key: gsk_..." https://git.yourdomain.com/api/users/alice/repos
```

### Internationalization
- Ships with **10 languages** out of the box: English, Dutch, German, French, Spanish, Portuguese, Russian, Chinese, Japanese, Arabic
- Language switcher in the navbar — preference stored in a cookie
- Transactional emails (registration, password reset) are localized HTML templates, not just translated strings
- **Extend with your own language** by adding a folder under `Localization/` — no recompile needed (see [Adding a Language](#adding-a-language))

### Security
- CSRF protection on all forms and on every state-changing API call made from the site (API-key calls carry no cookie, so they need none)
- Secure HTTP-only cookies with configurable expiry
- Git push/pull protected by Basic Authentication (password or access token)
- API keys and access tokens are stored as hashes only and shown once, when created
- Temporary lockout after repeated failed logins (`MaxFailedLoginAttempts`, `LoginLockoutMinutes`)
- Per-IP rate limits on the JSON API and on the sign-in, registration and password-reset forms (`ApiRequestsPerMinute`, `AuthRequestsPerMinute`); rejected requests get `429` with `Retry-After`
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
| Database | SQLite (built in, default) or SQL Server 2016+ / Express / LocalDB |

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
    "MaxFailedLoginAttempts": 5,
    "LoginLockoutMinutes": 15,
    "ApiRequestsPerMinute": 300,
    "AuthRequestsPerMinute": 10,
    "TrustForwardedHeaders": false,
    "AuditLogRetentionDays": 365,
    "DatabaseProvider": "Sqlite",
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
    "Default": "Data Source=gitserver.db",
    "SqlServer": "Server=.\\SQLEXPRESS;Database=GitServer;Trusted_Connection=True;TrustServerCertificate=True"
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
| `GitServer:MaxFailedLoginAttempts` / `LoginLockoutMinutes` | Wrong passwords in a row (web login and git over HTTPS) before an account is locked, and for how long |
| `GitServer:DefaultPrivateOnAutoCreate` | Visibility of repositories auto-created on first push |
| `GitServer:MaxPushSizeMb` | Max request body size (MB) for a push; `null`/omitted = unlimited |
| `GitServer:ExploreRepoPageSize` / `ExploreUserPageSize` | Items per page on the public `/dashboard/Explore` listings |
| `GitServer:ProfileRepoPageSize` | Items per page on a user's profile repository list |
| `GitServer:AdminUsersPageSize` | Items per page on the **Admin → Users** listing |
| `GitServer:IndexRecentReposCount` | Repos shown in the home page's "recent repositories" list |
| `GitServer:GitReleasesApiUrl` | GitHub Releases API endpoint the admin Git updater checks (see [Git engine](#git-engine)) |
| `GitServer:GitReleaseAssetPattern` | Regex used to pick the right release asset (64-bit MinGit) from that endpoint |
| `GitServer:GitExecutableInstallRoot` | Where downloaded MinGit versions are extracted; relative paths resolve against the app's content root |
| `Authentication:KeysPath` | Folder where Data Protection keys are persisted (antiforgery tokens, auth cookies) |
| `Authentication:ProtectKeysWithDpapi` | Encrypt the keys at rest using Windows DPAPI |
| `GitServer:DatabaseProvider` | `Sqlite` (default) or `SqlServer` — see [Choosing a database](#choosing-a-database) |
| `GitServer:ApiRequestsPerMinute` / `AuthRequestsPerMinute` | Requests per minute per IP address to the JSON API, and form posts per minute to sign-in / registration / password reset. `0` = no limit |
| `GitServer:TrustForwardedHeaders` | Use `X-Forwarded-For` / `X-Forwarded-Proto` from a reverse proxy, so limits and the audit log see the visitor instead of the proxy. Enable only when the app is reachable exclusively through that proxy |
| `GitServer:AuditLogRetentionDays` | Audit log entries older than this are pruned. `0` = keep forever. Entries can be downloaded as CSV from **Admin → Audit log** before they age out |
| `ConnectionStrings:Sqlite` / `ConnectionStrings:SqlServer` | Connection string for the chosen provider; `ConnectionStrings:Default` is the fallback for either |
| `EmailService:*` | SMTP settings used to send registration and password-reset emails; leave `SmtpHost` empty to disable outgoing email (registration links then just won't be delivered) |

> Everything above except `EmailService` and the identity/DB plumbing is a startup-time default. The settings under **Admin → Settings** (registration, user repo creation, push-to-create, anonymous push, commit avatars, API key lifetime) and the reserved-name patterns live in the database instead, so an admin can change them from the browser without editing config or restarting the app.

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
        # With "TrustForwardedHeaders": true GitServer uses these two headers for rate limits and the audit log
        # Required for git push/pull streaming
        proxy_request_buffering off;
        proxy_buffering off;
    }
}
```

---

## Choosing a database

SQLite is the default and needs nothing. To use **SQL Server** (full, Express or LocalDB) instead, set the provider and a connection string in `appsettings.json` (or `appsettings.Production.json`):

```json
"GitServer": { "DatabaseProvider": "SqlServer" },
"ConnectionStrings": {
  "SqlServer": "Server=.\\SQLEXPRESS;Database=GitServer;Trusted_Connection=True;TrustServerCertificate=True"
}
```

- The connection string is an ordinary SQL Server one: `Server=.\SQLEXPRESS`, `Server=(localdb)\MSSQLLocalDB`, `Server=db.example.com;User Id=…;Password=…`, and so on. The database is created and migrated on first start.
- Each provider has **its own migrations** (`Data/Migrations` for SQLite, `Data/MigrationsSqlServer` for SQL Server), applied automatically at startup. Names stay case-insensitive on both.
- Switching provider normally starts an empty database. To carry existing data over instead, first put a `ConnectionStrings:SqlServer` in `appsettings.json` pointing at a fresh, empty SQL Server database (while `DatabaseProvider` is still `Sqlite`), then use **Admin → Database migration**: test the connection, then run the copy. It applies the SQL Server migrations and copies every row across, preserving ids and relationships, and refuses to touch a target that already has migrations applied. Only afterward flip `DatabaseProvider` to `SqlServer` and restart the app — a running process can't swap its own database provider, so that part is never automated.
- Changing the model means one migration per provider:

```bash
dotnet ef migrations add MyChange --context AppDbContext --project src/GitServer
dotnet ef migrations add MyChange --context SqlServerAppDbContext -o Data/MigrationsSqlServer --project src/GitServer
```

  A test fails if either model has changes missing from its migrations.

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

The suite in `tests/GitServer.Tests` needs `git` on the PATH (or `GIT_TEST_EXECUTABLE` pointing at one) — every test runs against a real temporary SQLite database and real bare repositories, never mocks. The browser tests also need Chromium, installed once per machine after the first build:

```bash
pwsh tests/GitServer.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

(Windows PowerShell works too: `powershell -File …/playwright.ps1 install chromium`.)

The whole suite can also run against **SQL Server** instead of SQLite — every test then gets its own throwaway database, dropped afterwards:

```bash
GITSERVER_TEST_DB=sqlserver dotnet test                                   # uses (localdb)\MSSQLLocalDB
GITSERVER_TEST_DB=sqlserver GITSERVER_TEST_SQLSERVER="Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True" dotnet test
```

The suite has five layers:

| Layer | What it proves |
|---|---|
| **Policy** (`AccessPolicyTests`, `GitAccessDecisionTests`) | Every read / write / administer / delete rule, the read-only override, group ownership, and the full clone-and-push decision matrix, as pure questions to `AccessPolicy` |
| **Services & data** (`RepositoryServiceTests`, `NamingConstraintTests`, `MigrationTests`, `GitProcessServiceTests`) | Case-insensitive lookup with canonical names, paging/search/counts, the NOCASE unique indexes, and that all EF migrations apply (also on top of existing data) and match the model |
| **Localization** (`LocalizationFilesTests`, `LocalizationServiceTests`) | Every language has every key English has (and no extras), placeholders match, every `L["key"]` used in code exists, no dead strings, cultures and countries |
| **End to end** (`GitAuthMiddlewareTests`, `GitSmartHttpEndToEndTests`, `WebPagesEndToEndTests`, `AccountAndAdminEndToEndTests`, `ApiKeysEndToEndTests`, `HostSmokeTests`, …) | The real app hosted in-process: HTTP Basic auth, real `git` pack negotiation for push and clone, sign-in through the real login form, what each kind of visitor (anonymous, owner, group member, stranger, admin) can see and do, and the API with cookies and with API keys |
| **API contract** (`OpenApiEndToEndTests`) | The OpenAPI document exists, describes the `X-Api-Key` scheme and documents every `/api` endpoint with a summary — and fails if an endpoint is added without documentation or uses a verb other than `GET`/`POST` |
| **Browser** (`BrowserTests`) | The JavaScript-driven pages in a real headless Chromium (Playwright) against the same app on a real port: profile lists and paging, API keys, access tokens, reserved names, group detail, admin users |

All authorization decisions live in `Services/AccessPolicy.cs`; pages and middleware ask it instead of comparing `OwnerId` or `IsAdmin` themselves. CI runs the full suite (both the SQLite and SQL Server matrix jobs) on a version tag push (`vX.Y`) or a manual run — see the badges at the top of this file.

---

## Adding a Language

Each language is a folder under `src/GitServer/Localization/`. To add a new one:

1. Copy `src/GitServer/Localization/en/` to e.g. `src/GitServer/Localization/ko/`
2. In `ko/strings.json`, set `"__name__"` to the native language name (e.g. `"한국어"`) and translate all the values
3. Translate the HTML files under `ko/emails/` (registration and password-reset emails); they share the layout in `Localization/_email-layout.html` and use `{{placeholder}}` tokens
4. Restart the server — your language appears in the navbar dropdown automatically

To pin a language to a specific position in the dropdown, add `"__order__": "3"` (lower numbers appear first; English is `1`, Dutch is `2`). A key missing from a translation falls back to English at runtime, but the test suite (`LocalizationFilesTests`) fails on any missing or extra key, so new UI text must be added to every language.

---

## Architecture

GitServer is a single ASP.NET Core 10 application built on Razor Pages.

```
src/GitServer/
├── Controllers/        # Git HTTP protocol (upload-pack, receive-pack)
│   └── Api/            # The JSON API under /api (typed contracts in ApiContracts.cs)
├── Data/               # EF Core DbContext; migrations per provider (Migrations = SQLite, MigrationsSqlServer)
├── Extensions/         # Startup wiring: identity, API-key authentication, OpenAPI, rate limiting, git route prefix
├── Localization/       # One folder per language: strings.json + emails/*.html
├── Middleware/         # Git Basic Auth middleware, API-key guard (read-only keys), site-settings enforcement
├── Models/             # Domain models (User, Repository, Issue, Group, ApiKey, AccessToken, AuditEntry, SiteSettings, …)
├── Services/           # Business logic (Git, Repository, AccessPolicy, ApiKey, Audit, ReservedNames, UserSearch, TimeZone, …)
├── Pages/              # Razor Pages, all served under /dashboard (except the home page and repository pages)
│   ├── Auth/           # Login, Register, password reset
│   ├── Repo/           # Repository browser, commits, branches, tags, issues
│   ├── User/           # Profile, account settings, API keys, access tokens, groups
│   └── Admin/          # Users, blocked emails, reserved names, audit log, Git version updater, site settings
└── wwwroot/            # Static assets only (css, js, favicon)
tests/GitServer.Tests/   # xUnit: policy, services, migrations, localization, end-to-end, API contract and browser tests
```

**Stack:**
- ASP.NET Core 10 Razor Pages
- Entity Framework Core with SQLite (default) or SQL Server
- ASP.NET Core Identity
- A JSON API described with OpenAPI (`Microsoft.AspNetCore.OpenApi`); the pages render lists client-side from that API
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

Code style is set in `.editorconfig`: tab indentation for C#, Razor, JavaScript and CSS, LF line endings. Keep the API to `GET` and `POST`, document every new `/api` endpoint with an XML summary, and add UI text to every language (see [Adding a Language](#adding-a-language)).

---

## License

MIT License — do whatever you want with it.

---

<p align="center">
  Built with .NET 10 &nbsp;·&nbsp; Open Source &nbsp;·&nbsp; Free Forever
</p>
