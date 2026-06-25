<div align="center">

# 🔥 Phoenix Store

**A zero-trust, high-throughput digital storefront — built on a lock-free single-file core, hardened end to end, and shipped with its own Linux installer and management CLI.**

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)
![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black)
![Tailwind CSS](https://img.shields.io/badge/Tailwind-v4-06B6D4?logo=tailwindcss&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript&logoColor=white)
![Tests](https://img.shields.io/badge/tests-60%20passing-3fb950)
![License](https://img.shields.io/badge/license-Proprietary-red)

</div>

---

## Overview

Phoenix Store is a full-stack e-commerce platform for selling verified digital accounts and subscriptions. It pairs a **Next.js 16 / React 19** storefront and admin panel with an **ASP.NET Core 8** API, and deliberately replaces the usual database tier with a **single-file JSON store** engineered for atomic durability and lock-free read scaling — keeping the entire data layer trivially portable, backup-friendly, and dependency-free.

The result is a system that boots from a single binary + one `store.json`, survives restarts with sessions intact, and can be deployed to a bare Ubuntu VPS with one interactive command.

---

## ✨ Core Highlights

### 🗄️ Persistence — single file, serious engineering
- **Atomic writes:** every flush serializes to a unique temp file and is swapped in with an atomic `File.Move`, so a crash mid-write can never corrupt or truncate `store.json`.
- **Lock-free copy-on-write reads:** the hot anonymous catalog (products & categories) is served from immutable array snapshots published through `volatile` references and rebuilt under the write lock only when the set changes — storefront reads never contend on the mutation lock.
- **O(1) dirty-version flushing:** a monotonic version counter lets the periodic background flusher skip serialization entirely while idle, with a safety re-hash backstop and an unconditional shutdown save.
- **Isolated high-volume trails:** the audit log lives in its own `audit_store.json` so high-frequency writes never bloat the primary snapshot.

### 🛡️ Zero-Trust Security
- **Triple-verify database restore:** the single most destructive action requires three independent factors — the backup file, manual re-entry of the server's `PHONIX_BACKUP_KEY`, and a fresh TOTP 2FA code — validated with constant-time comparison and **fail-closed** on any missing or invalid factor. Every attempt (success or denial) is written to the audit log.
- **Encrypted backups:** AES-256-GCM with per-file salt + nonce, key derived via **PBKDF2** (100k iterations, SHA-256).
- **PBKDF2 password hashing**, **mandatory 2FA** for staff, **image CAPTCHA**, **anti-brute-force tarpit**, and **honeypot middleware** that traps and bans scanners before they reach the app.
- **Double-submit CSRF**, per-IP rate limiting, strict security headers, and trusted-proxy-only forwarded-header handling to keep client IPs unspoofable.

### 🔐 Advanced Auth & KYC
- **Stateless encrypted sessions:** claims are sealed into an httpOnly cookie via a persisted Data Protection key ring — no server-side session table, and logins survive restarts.
- **Security stamps:** a password change or admin action rotates the stamp and instantly invalidates every outstanding session everywhere.
- **Scoped admin sessions:** elevated roles are only granted to panel logins (password + 2FA); the same admin browsing the public site is treated as an ordinary customer.
- **Progressive 3-tier identity verification:** users advance through a strict Level 0 → 1 → 2 KYC ladder (bank-card approval, document/selfie review), with permanent, never-downgraded upgrades and identity images streamed only through authenticated, ownership-checked endpoints.

### 📈 DevOps & Observability
- **`install.sh`** — an interactive Ubuntu/Debian installer that provisions .NET 8, Node.js, Nginx and Certbot, configures the reverse proxy, auto-issues a Let's Encrypt certificate (forced HTTPS), seeds the production owner with enforced password complexity, and generates a high-entropy backup key.
- **`p-ui`** — a categorized management CLI for **zero-downtime hot updates** (build to a staging release, flip the `current` symlink, graceful reload), **primary/fallback domain routing**, and **owner credential rotation** — with the backup key kept structurally **invisible and immutable**.
- **Serilog-powered observability:** structured console + rolling JSON logs, an automatic admin **audit trail** of every mutating staff action, and a secure in-panel **log viewer / search / downloader** (single file or full ZIP) hardened against path traversal.
- **Live server metrics** dashboard widget backed by a background CPU sampler for lock-free reads.

---

## 🧱 Tech Stack

| Layer | Technology |
| --- | --- |
| Frontend | Next.js 16 (App Router), React 19, TypeScript 5, Tailwind CSS v4 |
| Backend | ASP.NET Core 8 (.NET 8), C# 12 |
| Persistence | Single-file JSON (`store.json`) with atomic swaps & lock-free read views |
| Auth | ASP.NET Data Protection, TOTP (RFC 6238), PBKDF2 |
| Logging | Serilog (console + rolling CompactJSON) |
| Delivery | Nginx reverse proxy, Let's Encrypt (Certbot), systemd |

---

## 🚀 Deployment (Production)

On a fresh Ubuntu/Debian server, download and run the installer.

**As root:**

```bash
curl -fsSLo phoenix-install.sh https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh
bash phoenix-install.sh
```

**With sudo (non-root user):**

```bash
curl -fsSLo phoenix-install.sh https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh
sudo bash phoenix-install.sh
```

> Download-then-run keeps the installer interactive for its prompts and avoids the `/dev/fd/63: No such file or directory` failure that `sudo bash <(…)` triggers.

The installer will interactively prompt for the domain, owner credentials, and Let's Encrypt email, then install dependencies, build, wire up systemd, secure the site with HTTPS, and print your `PHONIX_BACKUP_KEY` **once** — store it offline immediately.

Day-two operations run through the management CLI:

```bash
sudo p-ui
```

```
1) Change / update owner credentials
2) Manage domains (switch primary or add a fallback)
3) Zero-downtime hot update
```

---

## 🧑‍💻 Local Development

**Prerequisites:** .NET 8 SDK, Node.js 20+.

```bash
# Backend  →  http://localhost:5228
cd backend/src/Phonix.Api
dotnet run

# Frontend →  http://localhost:3000
cd frontend
npm install
npm run dev
```

Useful environment toggles for local runs:

| Variable | Purpose |
| --- | --- |
| `PHONIX_REQUIRE_CAPTCHA=false` | Disable the image CAPTCHA |
| `PHONIX_DISABLE_TARPIT=true` | Skip the failed-login delay |
| `PHONIX_BACKUP_KEY=<key>` | Enable encrypted backups & secure restore |
| `PHONIX_ENABLE_DIAGNOSTICS=true` | Mount the temporary `/api/diagnostics/stress` telemetry endpoint |

---

## 🗂️ Project Structure

```
.
├── backend/
│   └── src/Phonix.Api/        ASP.NET Core 8 API
│       ├── Controllers/       REST endpoints (auth, store, admin, backup, logs…)
│       ├── Data/              Single-file store: persistence, snapshots, domain logic
│       ├── Security/          Sessions, TOTP, CSRF, hashing, honeypot
│       └── Services/          Email, Telegram, metrics, log access
├── frontend/
│   └── src/                   Next.js 16 App Router (storefront + admin panel)
└── deploy/
    ├── install.sh             Interactive production installer
    └── p-ui                   Management CLI
```

---

## ✅ Quality

- **60 backend integration & unit tests** covering auth, finance, KYC, notifications, and persistence round-trips.
- Strict TypeScript across the entire frontend.
- Concurrency-safe persistence verified under live multi-session load.

---

## 📄 License

Proprietary — all rights reserved. Unauthorized copying, distribution, or use of this codebase is prohibited.
