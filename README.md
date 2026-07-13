<div align="center">

# 🔥 Phoenix Store

**A high-throughput, zero-trust e-commerce platform engineered for resilience.**

Next.js 16 · React 19 · ASP.NET Core 8 · Tailwind v4

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)](https://nextjs.org/)
[![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black)](https://react.dev/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind-v4-38BDF8?logo=tailwindcss&logoColor=white)](https://tailwindcss.com/)
[![Tests](https://img.shields.io/badge/tests-71%20passing-3fb950)](#)
[![License](https://img.shields.io/badge/license-Proprietary-red)](#-license)

</div>

---

## Overview

Phoenix Store is a production-grade storefront and back-office platform built around a single guiding principle: **maximum throughput with minimum trust assumptions.** It pairs a modern Next.js 16 / React 19 frontend with a hardened ASP.NET Core 8 API, backed by an embedded SQLite engine (WAL, serialized writes) reached through a single `IDataStore` abstraction.

The result is a system that boots from a single binary plus one SQLite file, deploys to a bare Ubuntu VPS in minutes with one interactive command, scales to high request volumes on commodity hardware, and treats every privileged operation as hostile until cryptographically proven otherwise.

---

## ✨ Core Highlights

### ⚡ Embedded High-Throughput Persistence
- **Single-file SQLite** in WAL mode — no external database server, no connection pool, backup-bot friendly.
- **ACID writes** through `IMMEDIATE` transactions, eliminating torn-write / partial-state corruption.
- **`IDataStore` abstraction** — persistence is swappable; a legacy JSON snapshot (`store.json`) is imported once to seed an empty database.
- Designed and validated against multi-threaded concurrency stress testing.

### 🛡️ Zero-Trust Security Architecture
- **Triple-verify database restore** — a restore requires *all three*: the backup file, the `PHONIX_BACKUP_KEY` secret, **and** a valid TOTP 2FA code. No single compromised factor is sufficient.
- **PBKDF2** password hashing with per-credential salts.
- **Anti-brute-force tarpit** — progressively delays attackers to make credential stuffing economically infeasible.
- **Honeypot middleware** — traps and fingerprints automated probes before they reach business logic.

### 🔐 Advanced KYC & Authentication
- **Stateless, encrypted cookies** — no server-side session store to leak or exhaust.
- **Security stamps** — instantly invalidate all active sessions on credential or permission changes.
- **Progressive 3-tier verification** — a strict, escalating KYC ladder gating sensitive actions by trust level.

### 🚀 DevOps & Observability
- **Interactive Linux installer** (`install.sh`) — guided, one-command provisioning.
- **`p-ui` CLI** — zero-downtime hot updates with health-checked auto-rollback, plus domain fallback routing.
- **Serilog-powered audit pipeline** — structured, secure audit logging with a gated log-download facility.

---

## 🧱 Tech Stack

| Layer        | Technology                                   |
|--------------|----------------------------------------------|
| Frontend     | Next.js 16, React 19, Tailwind CSS v4        |
| Backend      | ASP.NET Core 8 (C# 12)                       |
| Persistence  | Embedded SQLite (WAL) via `IDataStore`       |
| Logging      | Serilog (structured audit + app logs)        |
| Ops          | `install.sh` installer · `p-ui` CLI          |

---

## 📂 Repository Structure

```
Phonix/
├── backend/
│   └── src/Phonix.Api/        # ASP.NET Core 8 API, controllers, security middleware
├── frontend/                  # Next.js 16 storefront & admin
├── install.sh                # Interactive Linux installer
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

- **Zero-downtime hot updates** with snapshot + health-checked auto-rollback.
- **Domain fallback routing** for resilient public access.
- **Secure log download** of Serilog audit and application logs.

---

## 🔒 Security

Security is the core design constraint, not a feature bolted on afterward. If you discover a vulnerability, please disclose it responsibly to the maintainers rather than opening a public issue.

---

## 📜 License

Proprietary — © Phoenix Store. All rights reserved.
