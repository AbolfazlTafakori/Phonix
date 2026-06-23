"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { StaffMember, PermissionInfo, UserRole } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, Drawer, DataTable, inputCls, type Column } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const roleLabels: Record<UserRole, string> = { Customer: "کاربر", Support: "پشتیبان (محدود)", Admin: "مدیر کل" };

type Draft = {
  name: string;
  username: string;
  email: string;
  role: Exclude<UserRole, "Customer">;
  blocked: boolean;
  permissions: string[];
};

const emptyDraft: Draft = { name: "", username: "", email: "", role: "Support", blocked: false, permissions: [] };

export default function AdminStaffPage() {
  const [staff, setStaff] = useState<StaffMember[]>([]);
  const [catalog, setCatalog] = useState<PermissionInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [panel, setPanel] = useState<StaffMember | "new" | null>(null);

  async function load() {
    try {
      const [s, c] = await Promise.all([api.staff.list(), api.staff.permissions()]);
      setStaff(s);
      setCatalog(c);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری کارکنان");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const columns: Column<StaffMember>[] = [
    {
      header: "کارمند",
      primary: true,
      cell: (u) => (
        <div className="flex items-center gap-3">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
            {u.name.charAt(0)}
          </span>
          <div>
            <p className="font-medium">{u.name}</p>
            <p className="font-mono text-xs text-white/40" dir="ltr">@{u.username}</p>
          </div>
        </div>
      ),
    },
    { header: "نقش", cell: (u) => <StatusBadge status={roleLabels[u.role]} /> },
    {
      header: "دسترسی‌ها",
      cell: (u) => (u.role === "Admin" ? <span className="text-emerald-400">کامل</span> : <span className="text-white/70">{u.permissions.length} بخش</span>),
    },
    { header: "وضعیت", cell: (u) => <StatusBadge status={u.blocked ? "مسدود" : "فعال"} /> },
    {
      header: "عملیات",
      full: true,
      cell: (u) => (
        <button
          onClick={() => setPanel(u)}
          className="w-full rounded-lg border border-white/10 px-3 py-2 text-xs font-medium text-white/75 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff] lg:w-auto lg:py-1.5"
        >
          مدیریت
        </button>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="مدیریت کارکنان و نقش‌ها"
        desc="یک حساب موجود را با نام کاربری به کارمند ارتقا دهید و دقیقاً مشخص کنید به کدام بخش‌های پنل دسترسی داشته باشد."
        action={
          <button
            onClick={() => setPanel("new")}
            className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            <AdminIcon name="plus" className="h-4 w-4" />
            افزودن کارمند
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <Card className="overflow-hidden">
          <DataTable columns={columns} rows={staff} rowKey={(u) => u.id} minWidth={720} empty="هنوز کارمندی اضافه نشده است" />
        </Card>
      )}

      <StaffDrawer
        target={panel}
        catalog={catalog}
        onClose={() => setPanel(null)}
        onSaved={(saved, isNew) => {
          setStaff((prev) => (isNew ? [saved, ...prev] : prev.map((s) => (s.id === saved.id ? saved : s))));
          setPanel(null);
        }}
        onDeleted={(id) => {
          setStaff((prev) => prev.filter((s) => s.id !== id));
          setPanel(null);
        }}
      />
    </div>
  );
}

function StaffDrawer({
  target,
  catalog,
  onClose,
  onSaved,
  onDeleted,
}: {
  target: StaffMember | "new" | null;
  catalog: PermissionInfo[];
  onClose: () => void;
  onSaved: (s: StaffMember, isNew: boolean) => void;
  onDeleted: (id: number) => void;
}) {
  const isNew = target === "new";
  const existing = target && target !== "new" ? target : null;
  const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [newPassword, setNewPassword] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [done, setDone] = useState("");
  const [twoFaOff, setTwoFaOff] = useState(false);

  useEffect(() => {
    setError("");
    setDone("");
    setNewPassword("");
    setTwoFaOff(false);
    if (isNew) setDraft(emptyDraft);
    else if (existing)
      setDraft({
        name: existing.name,
        username: existing.username,
        email: existing.email,
        role: existing.role === "Admin" ? "Admin" : "Support",
        blocked: existing.blocked,
        permissions: existing.permissions,
      });
  }, [target]);

  const groups = useMemo(() => {
    const map = new Map<string, PermissionInfo[]>();
    for (const p of catalog) {
      if (!map.has(p.group)) map.set(p.group, []);
      map.get(p.group)!.push(p);
    }
    return [...map.entries()];
  }, [catalog]);

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }));
  const togglePerm = (key: string) =>
    setDraft((d) => ({ ...d, permissions: d.permissions.includes(key) ? d.permissions.filter((k) => k !== key) : [...d.permissions, key] }));

  async function save() {
    setSaving(true);
    setError("");
    try {
      if (isNew) {
        const created = await api.staff.create({
          username: draft.username.trim(),
          role: draft.role,
          permissions: draft.permissions,
        });
        onSaved(created, true);
      } else if (existing) {
        const updated = await api.staff.update(existing.id, {
          name: draft.name,
          email: draft.email,
          role: draft.role,
          blocked: draft.blocked,
          permissions: draft.permissions,
        });
        onSaved(updated, false);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در ذخیره");
    } finally {
      setSaving(false);
    }
  }

  async function resetPassword() {
    if (!existing) return;
    setSaving(true);
    setError("");
    setDone("");
    try {
      await api.staff.resetPassword(existing.id, newPassword);
      setNewPassword("");
      setDone("گذرواژه تغییر کرد و نشست‌های قبلی این کارمند باطل شد.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در تغییر گذرواژه");
    } finally {
      setSaving(false);
    }
  }

  async function disable2fa() {
    if (!existing) return;
    if (!confirm(`تأیید دو‌مرحله‌ای «${existing.name}» غیرفعال شود؟ این کار برای زمانی است که کارمند دسترسی به برنامه‌ی احرازکننده‌اش را از دست داده باشد.`)) return;
    setSaving(true);
    setError("");
    setDone("");
    try {
      await api.staff.disableTwoFactor(existing.id);
      setTwoFaOff(true);
      setDone("تأیید دو‌مرحله‌ای غیرفعال شد. کارمند در ورود بعدی باید دوباره آن را راه‌اندازی کند.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در غیرفعال‌سازی");
    } finally {
      setSaving(false);
    }
  }

  async function remove() {
    if (!existing) return;
    if (!confirm(`کارمند «${existing.name}» حذف شود؟`)) return;
    setSaving(true);
    try {
      await api.staff.remove(existing.id);
      onDeleted(existing.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در حذف");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Drawer open={target !== null} onClose={onClose} title={isNew ? "افزودن کارمند" : "مدیریت کارمند"}>
      <div className="space-y-5">
        {isNew ? (
          <label>
            <span className="mb-2 block text-sm text-white/70">نام کاربری حساب</span>
            <input value={draft.username} onChange={(e) => set("username", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="username" />
            <p className="mt-2 text-[11px] leading-5 text-white/40">
              نام کاربری یک حساب <b>موجود</b> را وارد کنید؛ همان حساب به کارمند ارتقا می‌یابد. ایمیل و گذرواژه‌ای نمی‌سازیم — کاربر از حساب خودش وارد می‌شود.
            </p>
          </label>
        ) : (
          <>
            <label>
              <span className="mb-2 block text-sm text-white/70">نام و نام خانوادگی</span>
              <input value={draft.name} onChange={(e) => set("name", e.target.value)} className={inputCls} />
            </label>
            <label>
              <span className="mb-2 block text-sm text-white/70">ایمیل</span>
              <input value={draft.email} onChange={(e) => set("email", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </label>
          </>
        )}

        <div>
          <span className="mb-2 block text-sm text-white/80">سطح دسترسی</span>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => set("role", "Support")}
              className={`flex-1 rounded-lg border px-3 py-2.5 text-xs font-bold transition ${draft.role === "Support" ? "border-[#3a64f2]/60 bg-[#3a64f2]/15 text-[#9db4ff]" : "border-white/10 text-white/55 hover:text-white"}`}
            >
              دسترسی محدود (پشتیبان)
            </button>
            <button
              type="button"
              onClick={() => set("role", "Admin")}
              className={`flex-1 rounded-lg border px-3 py-2.5 text-xs font-bold transition ${draft.role === "Admin" ? "border-emerald-500/60 bg-emerald-500/15 text-emerald-300" : "border-white/10 text-white/55 hover:text-white"}`}
            >
              دسترسی کامل (مدیر کل)
            </button>
          </div>
        </div>

        {draft.role === "Support" ? (
          <div className="rounded-xl border border-white/8 p-4">
            <p className="mb-3 text-sm font-bold text-white">بخش‌های قابل دسترسی</p>
            <div className="space-y-4">
              {groups.map(([group, items]) => (
                <div key={group}>
                  <p className="mb-2 text-xs font-medium text-white/45">{group}</p>
                  <div className="grid gap-1.5">
                    {items.map((p) => (
                      <label key={p.key} className="flex cursor-pointer items-center justify-between rounded-lg px-2 py-2 transition hover:bg-white/[0.03]">
                        <span className="text-sm text-white/80">{p.title}</span>
                        <Toggle checked={draft.permissions.includes(p.key)} onChange={() => togglePerm(p.key)} />
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <p className="mt-3 text-[11px] leading-5 text-white/40">داشبورد همیشه در دسترس است. بخش‌های سیستمی (پشتیبان‌گیری، تنظیمات، همین صفحه) فقط برای مدیر کل باز می‌ماند.</p>
          </div>
        ) : (
          <p className="rounded-xl border border-emerald-500/20 bg-emerald-500/[0.06] p-3 text-xs leading-6 text-emerald-300/90">
            مدیر کل به تمام بخش‌های پنل دسترسی کامل دارد.
          </p>
        )}

        {!isNew && (
          <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-3 py-3">
            <span className="text-sm text-white/80">حساب مسدود است</span>
            <Toggle checked={draft.blocked} onChange={(v) => set("blocked", v)} />
          </label>
        )}

        {error && <p className="text-sm text-rose-400">{error}</p>}
        {done && <p className="text-sm text-emerald-400">{done}</p>}

        <button
          onClick={save}
          disabled={saving}
          className="flex h-11 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
        >
          {saving ? <Spinner /> : isNew ? "ارتقا به کارمند" : "ذخیره تغییرات"}
        </button>

        {!isNew && existing && (
          <div className="space-y-3 border-t border-white/8 pt-5">
            <p className="text-sm font-bold text-white">تغییر گذرواژه</p>
            <div className="flex items-center gap-2">
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                dir="ltr"
                placeholder="گذرواژه جدید"
                className={`${inputCls} h-10 flex-1 text-left`}
              />
              <button
                onClick={resetPassword}
                disabled={saving || newPassword.length < 8}
                className="grid h-10 shrink-0 place-items-center rounded-xl bg-white/10 px-4 text-sm font-bold text-white transition hover:bg-white/15 disabled:opacity-40"
              >
                تنظیم
              </button>
            </div>
            <div className="flex items-center justify-between rounded-xl bg-white/[0.03] px-3 py-3">
              <div className="min-w-0">
                <p className="text-sm text-white/80">تأیید دو‌مرحله‌ای</p>
                <p className="text-[11px] text-white/40">{existing.twoFactorEnabled && !twoFaOff ? "فعال است" : "هنوز فعال نشده"}</p>
              </div>
              {existing.twoFactorEnabled && !twoFaOff && (
                <button
                  onClick={disable2fa}
                  disabled={saving}
                  className="shrink-0 rounded-lg border border-amber-500/40 px-3 py-1.5 text-xs font-bold text-amber-300 transition hover:bg-amber-500/10 disabled:opacity-50"
                >
                  غیرفعال‌سازی
                </button>
              )}
            </div>

            <button
              onClick={remove}
              className="flex h-10 w-full items-center justify-center gap-2 rounded-xl border border-rose-500/40 text-sm font-bold text-rose-400 transition hover:bg-rose-500/10"
            >
              <AdminIcon name="trash" className="h-4 w-4" />
              حذف کارمند
            </button>
          </div>
        )}
      </div>
    </Drawer>
  );
}
