<div align="center">

# 🔥 Phoenix Store

**A high-throughput, zero-trust e-commerce platform engineered for resilience.**

Next.js 16 · React 19 · ASP.NET Core 8 · Tailwind v4

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)](https://nextjs.org/)
[![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black)](https://react.dev/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind-v4-38BDF8?logo=tailwindcss&logoColor=white)](https://tailwindcss.com/)
[![Tests](https://img.shields.io/badge/tests-190%20passing-3fb950)](#)
[![License](https://img.shields.io/badge/license-Proprietary-red)](#-license)

</div>

---

## About

Phoenix Store is a complete, self-hosted shop for **digital goods** — subscription accounts, gift codes, licences and verification services — covering everything from the storefront a customer browses to the back office the team fulfils orders from.

What makes selling digital goods different from selling physical ones is that the product *is* a credential. So the platform is built around that: inventory is a pool of ready-to-deliver accounts rather than a warehouse count, a paid order can fulfil itself the moment payment clears, one shared subscription is sold as numbered seats to several buyers at once, and every credential is encrypted at rest and revealed one at a time behind an audited endpoint.

It ships as a **right-to-left Persian storefront** with a full admin panel: catalogue and plans, a wallet and manual receipt approval, discount codes, tiered identity verification, support tickets and live chat, a blog with SEO output, and Telegram bots that put receipt approval and order dispatch in a group chat.

Operationally it is deliberately small: one binary and one SQLite file. There is no database server to run, no cache to warm and no message broker to babysit. A bare Ubuntu VPS becomes a working shop with a single interactive command, updates are zero-downtime with automatic rollback, and an optional second server can mirror the first for business continuity.

Two constraints shaped the design throughout — **every privileged action is treated as hostile until proven otherwise**, and **money movements are atomic**: a wallet debit, the stock it consumes and the audit record it produces all commit together or not at all.

---

## ✨ Core Highlights

### ⚡ Embedded High-Throughput Persistence
- **Single-file SQLite** in WAL mode — no external database server, no connection pool, backup-bot friendly.
- **ACID writes** through `IMMEDIATE` transactions, eliminating torn-write / partial-state corruption.
- **`IDataStore` abstraction** — persistence is swappable; a legacy JSON snapshot (`store.json`) is imported once to seed an empty database.
- Designed and validated against multi-threaded concurrency stress testing.

### 📦 Virtual Stock Pool & Automated Fulfillment
- **Per-product inventory of ready-to-deliver items** — account credentials, gift codes, licenses — loaded in bulk ahead of time.
- **Encrypted at rest**: pool contents get the same field-level encryption as sensitive customer inputs, and are revealed one item at a time behind an audited endpoint.
- **Auto-delivery on payment**: the moment an order's payment is confirmed, opted-in products fulfill each unit straight from the pool; anything the pool can't cover degrades gracefully to manual fulfillment.
- **Atomic reservation** inside the same `IMMEDIATE` transactions as wallet debits — two concurrent orders can never claim the same item.
- Full traceability: every delivered item records which order unit consumed it.

### 🚚 Unit-Level Order Fulfillment
- Orders split into **per-account deliverable units**, so multiple staff can work the same order in parallel.
- Drafts, per-unit delivery with optional templated email, and automatic order completion when the last unit ships.
- **16-digit invoice numbers** minted exactly at completion — an undelivered order never has an invoice.
- Customers browse deliveries per product from their dashboard: each order shows its product logos, and each logo opens only that service's delivered accounts.

### 🛡️ Zero-Trust Security Architecture
- **Triple-verify database restore** — a restore requires *all three*: the backup file, the `PHONIX_BACKUP_KEY` secret, **and** a valid TOTP 2FA code. No single compromised factor is sufficient.
- **PBKDF2** password hashing with per-credential salts.
- **Anti-brute-force tarpit** — progressively delays attackers to make credential stuffing economically infeasible.
- **Honeypot middleware** — traps and fingerprints automated probes before they reach business logic.
- **Field-level encryption** for sensitive checkout inputs and stock-pool payloads — plaintext never reaches disk or backups.

### 🔐 Advanced KYC & Authentication
- **Stateless, encrypted cookies** — no server-side session store to leak or exhaust.
- **Security stamps** — instantly invalidate all active sessions on credential or permission changes.
- **Verification that follows the address** — changing an account's email drops its verified status until the new address is re-proven, so the checkout's verified-email gate can never be bypassed.
- **Tamper-proof 2FA lifecycle** — an active second factor can only be removed or re-provisioned with its current TOTP code; a hijacked session cannot strip it.
- **Progressive 3-tier verification** — a strict, escalating KYC ladder gating sensitive actions by trust level; payment destinations stay hidden until the cart's required level is met.
- **Section-scoped staff permissions** — limited staff accounts see and reach only the admin sections an owner explicitly grants.

### 🌍 High Availability — Primary / Standby Cluster
Optional two-server clustering for **business continuity** (a datacenter or connectivity outage), not load balancing. Exactly one node is writable at a time; the other mirrors it continuously and stays read-only. A single-server install is unaffected — `standalone` is the default and behaves exactly as before.

- **Continuous mirroring** — every write is journaled to an outbox and pulled by the peer, so a healthy Standby is an exact copy of the Primary: same rows, same uploaded files, verified by checksum.
- **Automatic failover** — a Standby that loses its Primary for longer than the grace period (default 90s) promotes itself and keeps taking orders unattended. A node that has never completed a first sync never promotes: an empty server must not take charge of live traffic.
- **Manual failback** — a returning Primary comes back read-only (`Recovering`) and catches up; reclaiming the role is a deliberate click, never automatic, and only once it is fully caught up.
- **Attach to a populated Primary** — a fresh Standby pulls one full snapshot, pins its sync cursor, then transfers media. Neither server has to start empty.
- **Restore-aware re-sync** — a wholesale restore on the Primary rotates a data epoch the peer notices on its next pull. Incremental sync only ever describes changes, so without this a Standby silently keeps rows the restore deleted while every health signal reads clean.
- **Disjoint id bands** — the Standby reserves its own autoincrement range, so ids minted on both sides during a partition can never collide.
- **Isolated sync failures** — one bad event is dead-lettered and retried on its own; it can never wedge every later change behind it.
- **Encrypted, authenticated node link** — HMAC-SHA256 over method, path, timestamp and body, with a replay window. Plain HTTP between nodes is refused at startup.

> Public traffic still follows DNS. When a Standby promotes itself, point the domain at it — that switch is deliberately a human decision.

### 🤖 Telegram Automation
- **Receipt bot** — every card-to-card receipt lands in the admin chat with one-tap approve/reject.
- **Order bot** — confirmed orders are announced to the fulfillment team exactly once, with claim-based dedup across approval paths.
- **Backup bot** — encrypted database backups shipped to a private chat on schedule, with failure alerting.

### 🚀 DevOps & Observability
- **Interactive Linux installer** (`install.sh`) — guided, one-command provisioning.
- **`p-ui` CLI** — zero-downtime hot updates with health-checked auto-rollback, plus domain fallback routing.
- **Serilog-powered audit pipeline** — structured, secure audit logging with a gated log-download facility.

---

## 🏷️ Topics

`ecommerce` · `digital-goods` · `subscription-management` · `storefront` · `admin-dashboard`
`dotnet` · `aspnetcore` · `csharp` · `nextjs` · `react` · `typescript` · `tailwindcss`
`sqlite` · `self-hosted` · `high-availability` · `zero-trust` · `two-factor-authentication` · `kyc`
`telegram-bot` · `rtl` · `persian`

---

## 🧱 Tech Stack

| Layer        | Technology                                   |
|--------------|----------------------------------------------|
| Frontend     | Next.js 16, React 19, Tailwind CSS v4        |
| Backend      | ASP.NET Core 8 (C# 12)                       |
| Persistence  | Embedded SQLite (WAL) via `IDataStore`       |
| Logging      | Serilog (structured audit + app logs)        |
| Ops          | `install.sh` installer · `p-ui` CLI · Docker Compose |
| Availability | Optional Primary/Standby cluster (outbox sync) |

---

## 📂 Repository Structure

```
Phonix/
├── backend/
│   ├── src/Phonix.Api/        # ASP.NET Core 8 API, controllers, security middleware
│   └── tests/                 # Integration, concurrency and security test suites
├── frontend/                  # Next.js 16 storefront & admin
├── deploy/
│   ├── install.sh             # Bare-metal installer (systemd + nginx + certbot)
│   └── p-ui                   # Operations CLI installed to /usr/local/bin
├── scripts/                   # Docker-based install and local dev helpers
├── docker-compose.yml         # Containerised stack (API + storefront)
├── install.sh                 # One-line bootstrap that fetches and runs deploy/install.sh
├── DEPLOY.md                  # Deployment, configuration and HA cluster guide
└── README.md
```

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS) + your package manager of choice

### Backend
```bash
cd backend/src/Phonix.Api
dotnet restore
dotnet run
```

### Frontend
```bash
cd frontend
npm install
npm run dev
```

### Production (single command)

Provision a bare Ubuntu VPS in one line — no clone required:

```bash
bash <(curl -Ls https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh)
```

Already cloned the repo? Run the installer directly:

```bash
sudo bash install.sh
```

### Two-server HA (optional)

Install both servers normally, each on its own reachable HTTPS hostname, then pair them from `p-ui` → *High-availability cluster setup*. Order matters: configure the **Primary first** so the Standby has something to sync from.

```
Server A (Primary)   p-ui → 4 → Primary   # prints the shared secret once — copy it
Server B (Standby)   p-ui → 4 → Standby   # paste that same secret
```

Each node needs the other's base URL (`https://…`, no port and no `/api` — the app appends its own path), reachable from the opposite side. The Standby then bootstraps and mirrors on its own. Full walkthrough, environment variables and failover/failback procedure: **[DEPLOY.md](DEPLOY.md)**.

---

## 🔬 Load-Test Diagnostics

Phoenix ships a temporary, flag-gated telemetry endpoint for concurrency stress testing. It is **disabled by default** and returns `404` unless explicitly enabled:

```bash
export PHONIX_ENABLE_DIAGNOSTICS=true
```

```http
GET /api/diagnostics/stress
```

Exposes aggregate runtime counters only — in-flight requests, thread-pool occupancy & starvation detection, pending/completed work items, and GC/memory pressure — for watching thread-pool starvation and allocation churn under load. No business data is ever returned.

---

## 🔧 Operations — `p-ui`

```bash
p-ui
```

- **Zero-downtime hot updates** with snapshot + health-checked auto-rollback — the tool updates itself in the same run, so new menu options arrive automatically.
- **Domain fallback routing** for resilient public access.
- **Secure log download** of Serilog audit and application logs.
- **HA cluster setup** — pick Primary or Standby and it asks only for what that role needs, then wires up and syncs the two servers.

Updates work on restricted networks too: the tool falls back from the git protocol to an HTTPS source archive, and builds from a local package cache when the NuGet feed is unreachable.

---

## 🔒 Security

Security is the core design constraint, not a feature bolted on afterward. If you discover a vulnerability, please disclose it responsibly to the maintainers rather than opening a public issue.

---

## 📜 License

Proprietary — © Phoenix Store. All rights reserved.
