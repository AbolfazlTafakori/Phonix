<div align="center">

# 🔥 Phoenix Store

**A high-throughput, zero-trust e-commerce platform engineered for resilience.**

Next.js 16 · React 19 · ASP.NET Core 8 · Tailwind v4

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)](https://nextjs.org/)
[![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black)](https://react.dev/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind-v4-38BDF8?logo=tailwindcss&logoColor=white)](https://tailwindcss.com/)
[![Tests](https://img.shields.io/badge/tests-307%20passing-3fb950)](#)
[![License](https://img.shields.io/badge/license-Proprietary-red)](#-license)

</div>

---

## About

Phoenix Store is a complete, self-hosted shop for **digital goods** — subscription accounts, gift codes, licences, verification services and V2Ray subscriptions — covering everything from the storefront a customer browses to the back office the team fulfils orders from.

What makes selling digital goods different from selling physical ones is that the product *is* a credential. So the platform is built around that: inventory is a pool of ready-to-deliver accounts rather than a warehouse count, a paid order can fulfil itself the moment payment clears, one shared subscription is sold as numbered seats to several buyers at once, and every credential is encrypted at rest and revealed one at a time behind an audited endpoint. Where there is no pool to draw from — a V2Ray plan, say — the account is created on its panel at the moment of sale instead.

It ships as a **right-to-left Persian storefront** with a full admin panel: catalogue and plans, a wallet and manual receipt approval, discount codes, tiered identity verification, support tickets and live chat, a blog with SEO output, and Telegram bots that put receipt approval and order dispatch in a group chat. On a phone the storefront behaves like an app rather than a shrunken desktop site — bottom tab navigation, a sticky buy bar on a product, and an account area where each section opens as its own screen.

Operationally it is deliberately small: one binary and one SQLite file. There is no database server to run, no cache to warm and no message broker to babysit. A bare Ubuntu VPS becomes a working shop with a single interactive command, updates are zero-downtime with automatic rollback, and an optional second server can mirror the first for business continuity.

Two constraints shaped the design throughout — **every privileged action is treated as hostile until proven otherwise**, and **money movements are atomic**: a wallet debit, the stock it consumes and the audit record it produces all commit together or not at all.

---

## ✨ Core Highlights

### ⚡ Embedded High-Throughput Persistence
- **Single-file SQLite** in WAL mode — no external database server, no connection pool, backup-bot friendly.
- **ACID writes** through `IMMEDIATE` transactions, eliminating torn-write / partial-state corruption.
- **`IDataStore` abstraction** — persistence is swappable; a legacy JSON snapshot (`store.json`) is imported once to seed an empty database.
- **Lists that don't scale with history** — order and transaction pages are paged in SQL against indexed columns, and only the rows actually returned are decrypted, so a screen showing twenty rows costs the same in year three as on day one.
- Designed and validated against multi-threaded concurrency stress testing.

### 📦 Virtual Stock Pool & Automated Fulfillment
- **Per-product inventory of ready-to-deliver items** — account credentials, gift codes, licenses — loaded in bulk ahead of time.
- **Encrypted at rest**: pool contents get the same field-level encryption as sensitive customer inputs, and are revealed one item at a time behind an audited endpoint.
- **Auto-delivery on payment**: the moment an order's payment is confirmed, opted-in products fulfill each unit straight from the pool; anything the pool can't cover degrades gracefully to manual fulfillment.
- **Atomic reservation** inside the same `IMMEDIATE` transactions as wallet debits — two concurrent orders can never claim the same item.
- Full traceability: every delivered item records which order unit consumed it.

### 🧾 A Checkout That Quotes What It Charges
An order is always priced by the server when it is placed, so anything the basket shows has to agree with that or the buyer is quoted one figure and debited another.

- **Baskets re-price themselves** — a cart carrying last month's prices is brought up to the live catalogue before anything is totalled, and the change is stated rather than swapped in silently.
- **Discount codes stop counting when the basket changes** — a code is validated against the basket it was typed into, so editing that basket retires it with an explanation instead of leaving a stale discount on screen.
- **Cancelling returns the coupon** — a single-use code spent on an order that is later cancelled goes back to the buyer, floored so a replayed cancellation can't manufacture extra capacity.

### 🚚 Unit-Level Order Fulfillment
- Orders split into **per-account deliverable units**, so multiple staff can work the same order in parallel.
- Drafts, per-unit delivery with optional templated email, and automatic order completion when the last unit ships.
- **Cancel one account, not the order** — when a single product of a basket can't be supplied, it is cancelled on its own and the rest ships normally. The buyer is refunded exactly that account's share of what they paid: its price less its slice of the order discount, plus its slice of VAT and the gateway fee. The panel states the figure before the operator commits, and its stock goes back on the shelf.
- **16-digit invoice numbers** minted exactly at completion — an undelivered order never has an invoice.
- Customers browse deliveries per product from their dashboard: each order shows its product logos, and each logo opens only that service's delivered accounts.

### 🛰️ V2Ray Services — Provisioned on Demand
A second kind of inventory: instead of handing out a credential from a pool, the shop creates the account on a panel at the moment it is sold. Panels, their inbounds and the sellable plans live in their own catalogue, and an ordinary product links to it — so the whole product presentation (logo, gallery, description, FAQ) is reused and only the plan list differs.

- **Provisioned automatically on approval**, exactly like pool auto-delivery — the customer waits for nobody.
- **Never blocks an approval**: the panel is a network hop, so provisioning happens out of band and retries until it succeeds. A server that is briefly unreachable can't fail an order that has already been paid for.
- **Per-plan sales cap** claimed inside the order transaction, so two buyers can't take the last place; a cancelled order or a rejected account gives its place back.
- **A live status page per account** — the buyer opens it from the order, and may hand the link to whoever the service is actually for. Usage, remaining volume and days are read from the subscription link itself, the same source the customer's own app polls, so the numbers agree to the byte without a panel login.
- Subscription link and every config listed separately, each with its own copy button and a scannable QR.

### 🛡️ Zero-Trust Security Architecture
- **Triple-verify database restore** — a restore requires *all three*: the backup file, the `PHONIX_BACKUP_KEY` secret, **and** a valid TOTP 2FA code. No single compromised factor is sufficient.
- **PBKDF2** password hashing with per-credential salts.
- **Anti-brute-force tarpit** — progressively delays attackers to make credential stuffing economically infeasible.
- **Honeypot middleware** — traps and fingerprints automated probes before they reach business logic.
- **Field-level encryption** for sensitive checkout inputs and stock-pool payloads — plaintext never reaches disk or backups.

### 🔐 Advanced KYC & Authentication
- **Stateless, encrypted cookies** — no server-side session store to leak or exhaust.
- **Security stamps** — instantly invalidate all active sessions on credential or permission changes.
- **Verification that follows the address** — an email change never takes effect on its own; a one-time confirmation link goes to the *new* address, and the account keeps its old, already-verified email until that address proves itself, so the checkout's verified-email gate can never be bypassed. The old address gets a security notice the moment the change is requested — not after — so a hijacked session can't swap it out unnoticed.
- **Password reset that assumes nothing is remembered** — the emailed link is the only thing it asks for: a new password and its confirmation, never the forgotten one. The token is deleted the moment it is read, whatever the outcome, so a leaked or shared link cannot be replayed, and it expires within the hour. Completing a reset rotates the account's security stamp, signing out every session opened with the old password, and the owner is told by email that it happened — a reset driven by someone else is noticed rather than silent. Requesting one answers identically whether or not the address exists, and is CAPTCHA-gated so it cannot be turned into a mail cannon aimed at someone else's inbox.
- **Tamper-proof 2FA lifecycle** — an active second factor can only be removed or re-provisioned with its current TOTP code; a hijacked session cannot strip it.
- **One identity, one account** — signup settles uniqueness inside the transaction that inserts, so two simultaneous attempts on the same username or address can't both succeed. An address that identified two accounts would quietly break login-by-email and password reset for both.
- **Progressive 3-tier verification** — a strict, escalating KYC ladder gating sensitive actions by trust level; payment destinations stay hidden until the cart's required level is met.
- **Section-scoped staff permissions** — limited staff accounts see and reach only the admin sections an owner explicitly grants.

### 🌍 High Availability — Primary / Standby Cluster
Optional two-server clustering for **business continuity** (a datacenter or connectivity outage), not load balancing. Exactly one node is writable at a time; the other mirrors it continuously and stays read-only. A single-server install is unaffected — `standalone` is the default and behaves exactly as before.

- **Continuous mirroring** — every write is journaled to an outbox and pulled by the peer, so a healthy Standby is an exact copy of the Primary: same rows, same uploaded files, verified by checksum.
- **Automatic failover** — a Standby that loses its Primary for longer than the grace period (default 90s) promotes itself and keeps taking orders unattended. A node that has never completed a first sync never promotes: an empty server must not take charge of live traffic.
- **Survives a one-way link cut** — on a filtered route the Standby can stop reaching the Primary while the Primary still reaches it and keeps serving customers. Promoting there would make two Primaries and cost whichever side lost the argument its writes. A node-to-node request that passes HMAC verification is proof the peer is alive, and only the peer can produce one, so recent inbound contact outranks the outbound silence. A genuinely dead peer sends nothing, so real failover is unaffected.
- **Self-retiring outbox** — replicated history is pruned once the peer's own cursor confirms it, and only after it has aged. A Standby that is behind, offline or never configured never advances that cursor, so entries it still needs are never dropped; without this the journal grew for the lifetime of the shop and took the database, the bootstrap snapshot and every backup with it.
- **Manual failback** — a returning Primary comes back read-only (`Recovering`) and catches up; reclaiming the role is a deliberate click, never automatic, and only once it is fully caught up.
- **Attach to a populated Primary** — a fresh Standby pulls one full snapshot, pins its sync cursor, then transfers media. Neither server has to start empty.
- **Restore-aware re-sync** — a wholesale restore on the Primary rotates a data epoch the peer notices on its next pull. Incremental sync only ever describes changes, so without this a Standby silently keeps rows the restore deleted while every health signal reads clean.
- **Disjoint id bands** — the Standby reserves its own autoincrement range, so ids minted on both sides during a partition can never collide.
- **Isolated sync failures** — one bad event is dead-lettered and retried on its own; it can never wedge every later change behind it.
- **Encrypted, authenticated node link** — HMAC-SHA256 over method, path, timestamp and body, with a replay window. Plain HTTP between nodes is refused at startup.
- **Configurable from the admin panel** — enable clustering, set or rotate the peer URL and shared secret, and correct a misconfigured role, all live from *Cluster Management*, with no terminal and no restart. A rolling diagnostic log and failover/promote/demote history sit alongside the live status for troubleshooting.

> Public traffic still follows DNS. When a Standby promotes itself, point the domain at it — that switch is deliberately a human decision.

### 🔄 Screens That Keep Themselves Current
Operational screens refresh on their own, so nobody has to reload a page to find out whether something
changed. A shared polling hook handles the scheduling, and every screen that uses it pauses while the tab is
in the background and while an edit is in flight — so a refresh can never pull a row out from under someone
mid-action, and a backgrounded tab stops asking.

- **Fulfilment queues** — receipts awaiting approval, orders being prepared, order status and the stock pool
  pick up new work within seconds of it arriving.
- **Live price and stock on the product page** — a visitor sitting on a product sees a price or stock change
  without reloading. The page only re-renders when one of those figures actually moved, so nothing shifts
  under the reader when nothing changed.
- **Error backoff** — a degraded API is retried progressively more slowly instead of being hammered, and the
  last good figures stay on screen rather than collapsing into an error.

### 🤖 Telegram Automation
- **Receipt bot** — every card-to-card receipt lands in the admin chat with one-tap approve/reject.
- **Order bot** — confirmed orders are announced to the fulfillment team exactly once, with claim-based dedup across approval paths.
- **Backup bot** — encrypted database backups shipped to a private chat on schedule, with failure alerting. Archives are compressed, split under Telegram's per-file cap, and customer documents are always encrypted to the offline key before they leave the server. An uploads folder that has outgrown the box fails the backup with a message naming the limit instead of taking the API down with it (`PHONIX_BACKUP_MAX_MEDIA_MB`).

### 🔎 Search Presence & Content Round-Trip
Every page is server-rendered with its own title, description and canonical, and the catalogue drives the
machine-readable surface rather than a hand-maintained copy of it.

- **Sitemap built from the live catalogue** — active products, categories that actually resolve, and blog
  posts, so a delisted product leaves the sitemap the moment it is delisted.
- **`lastmod` only where the date is real.** Post dates are authored as Persian (Jalali) labels for readers,
  so they are converted to Gregorian for the sitemap; a label that doesn't parse omits the field instead of
  substituting today. An always-current `lastmod` is worse than none — it teaches a crawler to distrust the
  signal across the whole file.
- **Structured data per page type** — `Organization` and `WebSite` sitewide, `Product` with `AggregateOffer`,
  `BreadcrumbList`, and a per-product `FAQPage` built from the FAQ the panel already stores.
- **Private areas excluded** in `robots.txt` — account, checkout, cart, invoice, admin and the API.

**Content lives in the shop, not the repo.** Product copy and blog posts are authored in the admin panel, so
the live database is the source of truth. `scripts/sync-site-content.py` pulls that published copy back out
into `seo-content/` as Markdown — one file per product and post, in the same shape the panel's importers
read. That gives a reviewable, diffable snapshot of exactly what is public without making the repo a second
source of truth that can drift.

```bash
python scripts/sync-site-content.py [--url https://phoenixverify.com] [--out seo-content]
```

`seo-content/` is deliberately untracked: it is generated output, regenerate it whenever you want the
current state.

### 🚀 DevOps & Observability
- **Interactive Linux installer** (`install.sh`) — guided, one-command provisioning.
- **`p-ui` CLI** — zero-downtime hot updates with health-checked auto-rollback, plus domain fallback routing.
- **Rendering scaled to the machine** — server-rendering a page is the most expensive thing the app does, and
  Node renders on a single thread, so one process can never use more than one core however large the server
  is. The renderer therefore runs as several processes behind nginx. How many is derived from the machine —
  one per core, capped by memory so a box with many cores and little RAM does not thrash — and recalculated
  on every update, so resizing the server is enough to use the new capacity and shrinking it tidies up after
  itself. Override with `PHONIX_WEB_INSTANCES` if you want a specific number.
- **Serilog-powered audit pipeline** — structured, secure audit logging with a gated log-download facility.

---

## 🏷️ Topics

`ecommerce` · `digital-goods` · `subscription-management` · `storefront` · `admin-dashboard`
`dotnet` · `aspnetcore` · `csharp` · `nextjs` · `react` · `typescript` · `tailwindcss`
`sqlite` · `self-hosted` · `high-availability` · `zero-trust` · `two-factor-authentication` · `kyc`
`telegram-bot` · `rtl` · `persian` · `v2ray` · `xray` · `3x-ui`

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
├── scripts/
│   ├── dev.sh / dev.ps1       # Run API and storefront together for local work
│   ├── install.sh             # Container-based install path
│   ├── phonix.service         # systemd unit template
│   └── sync-site-content.py   # Pull published copy back out of the live shop (see below)
├── figma-export/              # Design-export tooling; downloaded assets stay untracked
├── seo-content/               # Output of sync-site-content.py — untracked, regenerate on demand
├── docker-compose.yml         # Containerised stack (API + storefront)
├── install.sh                 # One-line bootstrap that fetches and runs deploy/install.sh
├── DEPLOY.md                  # Deployment, configuration and HA cluster guide
├── PRODUCT.md                 # Product overview
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

Each node needs the other's base URL (`https://…`, no port and no `/api` — the app appends its own path), reachable from the opposite side. The Standby then bootstraps and mirrors on its own. The same setup — mode, peer URL, shared secret, and a manual role correction if a node was configured wrong — is also available live from the admin panel's *Cluster Management* page, with no terminal or restart required. Full walkthrough, environment variables and failover/failback procedure: **[DEPLOY.md](DEPLOY.md)**.

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
