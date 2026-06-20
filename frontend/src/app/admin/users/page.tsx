"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { User, UserRole } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, Drawer, DataTable, inputCls, type Column } from "@/components/admin/ui";
import { Pagination, usePaged } from "@/components/admin/Pagination";
import AdminIcon from "@/components/admin/AdminIcon";

const roleLabels: Record<UserRole, string> = { Customer: "کاربر", Support: "پشتیبانی", Admin: "مدیر" };
const roleOptions: UserRole[] = ["Customer", "Support", "Admin"];

type RoleFilter = UserRole | "all";
type StatusFilter = "all" | "active" | "blocked";

type Draft = { name: string; email: string; phone: string; role: UserRole; verified: boolean; blocked: boolean; note: string };

export default function AdminUsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState<RoleFilter>("all");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  const [selected, setSelected] = useState<User | null>(null);

  async function load() {
    try {
      setUsers(await api.users.list());
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری کاربران");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const stats = useMemo(
    () => ({
      total: users.length,
      active: users.filter((u) => !u.blocked).length,
      blocked: users.filter((u) => u.blocked).length,
      verified: users.filter((u) => u.verified).length,
    }),
    [users],
  );

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return users.filter((u) => {
      if (roleFilter !== "all" && u.role !== roleFilter) return false;
      if (statusFilter === "active" && u.blocked) return false;
      if (statusFilter === "blocked" && !u.blocked) return false;
      if (term) {
        const hay = `${u.name} ${u.email} ${u.phone} ${u.code}`.toLowerCase();
        if (!hay.includes(term)) return false;
      }
      return true;
    });
  }, [users, search, roleFilter, statusFilter]);

  const { page, setPage, totalPages, slice, total, pageSize } = usePaged(filtered, 15);

  function applyUser(updated: User) {
    setUsers((prev) => prev.map((u) => (u.id === updated.id ? updated : u)));
    setSelected((prev) => (prev && prev.id === updated.id ? updated : prev));
  }

  async function removeUser(u: User) {
    if (!confirm(`کاربر «${u.name}» حذف شود؟`)) return;
    await api.users.remove(u.id);
    setUsers((prev) => prev.filter((x) => x.id !== u.id));
    setSelected(null);
  }

  const columns: Column<User>[] = [
    {
      header: "کاربر",
      primary: true,
      cell: (u) => (
        <div className="flex items-center gap-3">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
            {u.name.charAt(0)}
          </span>
          <div>
            <p className="flex items-center gap-1.5 font-medium">
              {u.name}
              {u.verified && <span className="text-emerald-400" title="احراز هویت‌شده">✓</span>}
            </p>
            <p className="font-mono text-xs text-white/40">{u.code}</p>
          </div>
        </div>
      ),
    },
    {
      header: "تماس",
      cell: (u) => (
        <div className="leading-tight">
          <p className="text-white/65" dir="ltr">{u.email}</p>
          <p className="text-xs text-white/40" dir="ltr">{u.phone}</p>
        </div>
      ),
    },
    { header: "نقش", cell: (u) => <StatusBadge status={roleLabels[u.role]} /> },
    { header: "سفارش‌ها", td: "text-white/70", cell: (u) => formatNumber(u.orders) },
    { header: "کیف پول", cell: (u) => formatToman(u.wallet) },
    { header: "وضعیت", cell: (u) => <StatusBadge status={u.blocked ? "مسدود" : "فعال"} /> },
    {
      header: "عملیات",
      full: true,
      cell: (u) => (
        <button
          onClick={() => setSelected(u)}
          className="w-full rounded-lg border border-white/10 px-3 py-2 text-xs font-medium text-white/75 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff] md:w-auto md:py-1.5"
        >
          مدیریت
        </button>
      ),
    },
  ];

  const statCards = [
    { label: "کل کاربران", value: stats.total, icon: "users", accent: "#3a64f2" },
    { label: "فعال", value: stats.active, icon: "shield", accent: "#22c55e" },
    { label: "مسدود", value: stats.blocked, icon: "shield", accent: "#f43f5e" },
    { label: "احراز هویت‌شده", value: stats.verified, icon: "shield", accent: "#a855f7" },
  ];

  return (
    <div>
      <PageHeader title="مدیریت کاربران" desc="مشاهده و کنترل کامل حساب‌های کاربری" />

      <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {statCards.map((s) => (
          <Card key={s.label} className="flex items-center gap-4 p-5">
            <div className="grid h-11 w-11 place-items-center rounded-xl" style={{ background: `${s.accent}1f`, color: s.accent }}>
              <AdminIcon name={s.icon} className="h-5 w-5" />
            </div>
            <div>
              <p className="text-2xl font-bold text-white">{formatNumber(s.value)}</p>
              <p className="text-sm text-white/50">{s.label}</p>
            </div>
          </Card>
        ))}
      </div>

      <div className="mb-5 flex flex-wrap items-center gap-3">
        <div className="flex h-11 min-w-[220px] flex-1 items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-4 text-white/50">
          <AdminIcon name="search" className="h-4 w-4" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="جستجوی نام، ایمیل، شماره..."
            className="w-full bg-transparent text-sm text-white placeholder:text-white/40 focus:outline-none"
          />
        </div>

        <div className="flex rounded-xl border border-white/10 bg-white/5 p-1">
          {([["all", "همه نقش‌ها"], ["Customer", "کاربر"], ["Support", "پشتیبانی"], ["Admin", "مدیر"]] as [RoleFilter, string][]).map(
            ([key, label]) => (
              <button
                key={key}
                onClick={() => setRoleFilter(key)}
                className={`rounded-lg px-3.5 py-1.5 text-sm font-medium transition ${
                  roleFilter === key ? "bg-white/10 text-white" : "text-white/55 hover:text-white"
                }`}
              >
                {label}
              </button>
            ),
          )}
        </div>

        <div className="flex rounded-xl border border-white/10 bg-white/5 p-1">
          {([["all", "همه"], ["active", "فعال"], ["blocked", "مسدود"]] as [StatusFilter, string][]).map(([key, label]) => (
            <button
              key={key}
              onClick={() => setStatusFilter(key)}
              className={`rounded-lg px-3.5 py-1.5 text-sm font-medium transition ${
                statusFilter === key ? "bg-white/10 text-white" : "text-white/55 hover:text-white"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <>
          <Card className="overflow-hidden">
            <DataTable columns={columns} rows={slice} rowKey={(u) => u.id} minWidth={860} empty="کاربری یافت نشد" />
          </Card>
          <Pagination page={page} totalPages={totalPages} total={total} pageSize={pageSize} onPage={setPage} />
        </>
      )}

      <UserDrawer user={selected} onClose={() => setSelected(null)} onApply={applyUser} onDelete={removeUser} />
    </div>
  );
}

function UserDrawer({
  user,
  onClose,
  onApply,
  onDelete,
}: {
  user: User | null;
  onClose: () => void;
  onApply: (u: User) => void;
  onDelete: (u: User) => void;
}) {
  const [draft, setDraft] = useState<Draft | null>(null);
  const [saving, setSaving] = useState(false);
  const [walletAmount, setWalletAmount] = useState(0);
  const [walletBusy, setWalletBusy] = useState(false);

  useEffect(() => {
    if (user) {
      setDraft({
        name: user.name,
        email: user.email,
        phone: user.phone,
        role: user.role,
        verified: user.verified,
        blocked: user.blocked,
        note: user.note ?? "",
      });
      setWalletAmount(0);
    } else {
      setDraft(null);
    }
  }, [user]);

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((prev) => (prev ? { ...prev, [key]: value } : prev));

  async function save() {
    if (!user || !draft) return;
    setSaving(true);
    try {
      onApply(await api.users.update(user.id, draft));
    } finally {
      setSaving(false);
    }
  }

  async function adjust(sign: 1 | -1) {
    if (!user || walletAmount <= 0) return;
    setWalletBusy(true);
    try {
      onApply(await api.users.adjustWallet(user.id, { amount: sign * walletAmount }));
      setWalletAmount(0);
    } finally {
      setWalletBusy(false);
    }
  }

  return (
    <Drawer open={!!user} onClose={onClose} title="مدیریت کاربر">
      {user && draft && (
        <div className="space-y-6">
          <div className="flex items-center gap-3">
            <span className="grid h-14 w-14 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-lg font-bold text-white">
              {user.name.charAt(0)}
            </span>
            <div>
              <p className="text-lg font-bold text-white">{user.name}</p>
              <p className="font-mono text-xs text-white/40">{user.code} · عضویت {user.joinedAt}</p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="rounded-xl bg-white/[0.03] p-3">
              <p className="text-xs text-white/40">مجموع خرید</p>
              <p className="mt-1 text-sm font-bold text-white">{formatToman(user.totalSpent)}</p>
            </div>
            <div className="rounded-xl bg-white/[0.03] p-3">
              <p className="text-xs text-white/40">تعداد سفارش</p>
              <p className="mt-1 text-sm font-bold text-white">{formatNumber(user.orders)}</p>
            </div>
          </div>

          <div className="grid gap-4">
            <label>
              <span className="mb-2 block text-sm text-white/70">نام</span>
              <input value={draft.name} onChange={(e) => set("name", e.target.value)} className={inputCls} />
            </label>
            <label>
              <span className="mb-2 block text-sm text-white/70">ایمیل</span>
              <input value={draft.email} onChange={(e) => set("email", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </label>
            <label>
              <span className="mb-2 block text-sm text-white/70">شماره تماس</span>
              <input value={draft.phone} onChange={(e) => set("phone", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </label>
            <label>
              <span className="mb-2 block text-sm text-white/70">نقش</span>
              <select value={draft.role} onChange={(e) => set("role", e.target.value as UserRole)} className={inputCls}>
                {roleOptions.map((r) => (
                  <option key={r} value={r} className="bg-[#15151f]">{roleLabels[r]}</option>
                ))}
              </select>
            </label>
          </div>

          <div className="grid gap-1 rounded-xl bg-white/[0.03] p-2">
            <label className="flex cursor-pointer items-center justify-between px-2 py-2.5">
              <span className="text-sm text-white/80">احراز هویت تأیید شده</span>
              <Toggle checked={draft.verified} onChange={(v) => set("verified", v)} />
            </label>
            <label className="flex cursor-pointer items-center justify-between px-2 py-2.5">
              <span className="text-sm text-white/80">حساب مسدود است</span>
              <Toggle checked={draft.blocked} onChange={(v) => set("blocked", v)} />
            </label>
          </div>

          <div className="rounded-xl border border-white/8 p-4">
            <p className="mb-3 text-sm font-bold text-white">کیف پول · موجودی فعلی {formatToman(user.wallet)}</p>
            <div className="flex items-center gap-2">
              <input
                type="number"
                dir="ltr"
                value={walletAmount}
                onChange={(e) => setWalletAmount(Math.max(0, Number(e.target.value)))}
                placeholder="مبلغ"
                className={`${inputCls} h-10 flex-1 text-left`}
              />
              <button
                onClick={() => adjust(1)}
                disabled={walletBusy || walletAmount <= 0}
                className="grid h-10 w-20 place-items-center rounded-xl bg-emerald-500/15 text-sm font-bold text-emerald-400 transition hover:bg-emerald-500/25 disabled:opacity-40"
              >
                {walletBusy ? <Spinner /> : "شارژ"}
              </button>
              <button
                onClick={() => adjust(-1)}
                disabled={walletBusy || walletAmount <= 0}
                className="grid h-10 w-20 place-items-center rounded-xl bg-rose-500/15 text-sm font-bold text-rose-400 transition hover:bg-rose-500/25 disabled:opacity-40"
              >
                کسر
              </button>
            </div>
          </div>

          <label>
            <span className="mb-2 block text-sm text-white/70">یادداشت داخلی</span>
            <textarea
              rows={3}
              value={draft.note}
              onChange={(e) => set("note", e.target.value)}
              className={`${inputCls} h-auto py-3`}
              placeholder="یادداشت برای تیم پشتیبانی..."
            />
          </label>

          <div className="flex gap-3">
            <button
              onClick={save}
              disabled={saving}
              className="flex h-11 flex-1 items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110"
            >
              {saving ? <Spinner /> : "ذخیره تغییرات"}
            </button>
            <button
              onClick={() => onDelete(user)}
              className="grid h-11 w-11 place-items-center rounded-xl border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
            >
              <AdminIcon name="trash" className="h-5 w-5" />
            </button>
          </div>
        </div>
      )}
    </Drawer>
  );
}
