# GitServer

> **Your code. Your server. Your rules.**
> A fast, lightweight, self-hosted Git platform — completely free and open source.

GitServer gives you everything you need to host your own Git repositories without sending your code to the cloud, paying monthly fees, or trusting a third party with your intellectual property. Deploy it on a Raspberry Pi, a VPS, or your own hardware in minutes.

---

## Why GitServer?

Because your code doesn't belong to anyone else.

- **100% free** — no plans, no tiers, no credit card required. Ever.
- **Open source** — read every line, modify anything, contribute back.
- **Self-hosted** — runs anywhere .NET runs. Linux, Windows, macOS, ARM.
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
- User registration and authentication
- Secure password hashing via ASP.NET Core Identity
- OAuth-ready architecture
- Admin panel for user management — promote to admin, remove users
- Per-user profile pages with bio and avatar

### Internationalization
- Ships with **10 languages** out of the box: English, Dutch, German, French, Spanish, Portuguese, Russian, Chinese, Japanese, Arabic
- Language switcher in the navbar — preference stored in a cookie
- **Extend with your own language** by dropping a single JSON file into the `Localization/` folder — no recompile needed

### Security
- CSRF protection on all forms
- Secure HTTP-only cookies with configurable expiry
- Git push/pull protected by Basic Authentication
- Data Protection API for antiforgery key persistence across restarts

---

## Screenshots

> *Browse repositories, view code, manage issues — all from a clean dark-themed UI.*

---

## Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0 or later |
| Git | Any recent version |
| OS | Windows, Linux, macOS |

That's it. No Docker required. No Postgres. No Redis. No message queue.

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
    "RepositoriesPath": "/var/git/repos",
    "GitExecutable": "/usr/bin/git",
    "AllowRegistration": true
  },
  "ConnectionStrings": {
    "Default": "Data Source=gitserver.db"
  }
}
```

| Setting | Description |
|---------|-------------|
| `RepositoriesPath` | Where bare Git repositories are stored on disk |
| `GitExecutable` | Path to the `git` binary |
| `AllowRegistration` | Set to `false` to lock down new sign-ups |

### 3. Run

```bash
cd src/GitServer
dotnet run
```

The database is created and migrated automatically on first start. Open your browser at `http://localhost:5000`.

The **first user to register becomes admin** automatically.

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
└── wwwroot/            # Razor Pages + static assets
    ├── Auth/           # Login, Register
    ├── Repo/           # Repository browser, commits, branches, issues
    ├── User/           # Profile, settings
    └── Admin/          # User management
```

**Stack:**
- ASP.NET Core 10 Razor Pages
- Entity Framework Core with SQLite
- ASP.NET Core Identity
- LibGit2Sharp-style process-based Git execution (no native library deps)
- Zero JavaScript frameworks — vanilla JS only

---

## Configuration Reference

| Key | Default | Description |
|-----|---------|-------------|
| `GitServer:RepositoriesPath` | *(required)* | Directory where bare repos are stored |
| `GitServer:GitExecutable` | *(required)* | Path to `git` binary |
| `GitServer:AllowRegistration` | `true` | Whether new users can self-register |
| `ConnectionStrings:Default` | `Data Source=gitserver.db` | SQLite connection string |

---

## Roadmap

- SSH key authentication
- Repository access control (per-user read/write permissions)
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

MIT License — do whatever you want with it. See [LICENSE](LICENSE) for details.

---

<p align="center">
  Built with .NET 10 &nbsp;·&nbsp; Open Source &nbsp;·&nbsp; Free Forever
</p>
