"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { Card, PageHeader, Spinner, Field, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

export default function PlanTypesPage() {
  const [types, setTypes] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [newName, setNewName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState("");

  useEffect(() => {
    api.planTypes
      .list()
      .then(setTypes)
      .finally(() => setLoading(false));
  }, []);

  async function add() {
    const name = newName.trim();
    if (!name) return;
    setBusy(true);
    setError("");
    try {
      setTypes(await api.planTypes.add(name));
      setNewName("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در افزودن");
    } finally {
      setBusy(false);
    }
  }

  async function rename(oldName: string) {
    const next = draft.trim();
    if (!next || next === oldName) {
      setEditing(null);
      return;
    }
    setBusy(true);
    setError("");
    try {
      setTypes(await api.planTypes.rename(oldName, next));
      setEditing(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در تغییر نام");
    } finally {
      setBusy(false);
    }
  }

  async function remove(name: string) {
    if (!confirm(`نوع «${name}» حذف شود؟ (پلن‌هایی که قبلاً از آن استفاده کرده‌اند تغییری نمی‌کنند)`)) return;
    setTypes(await api.planTypes.remove(name));
  }

  return (
    <div>
      <PageHeader title="نوع سرویس (پلن‌ها)" desc="دسته‌بندی پلن‌ها مثل اشتراکی، اختصاصی، کرکی و... که در همه‌ی محصولات قابل استفاده است." />

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="p-6">
          <h3 className="mb-4 text-lg font-bold text-white">نوع‌های موجود</h3>
          {loading ? (
            <div className="grid h-24 place-items-center"><Spinner /></div>
          ) : types.length === 0 ? (
            <p className="py-6 text-center text-sm text-white/45">هنوز نوعی تعریف نشده است.</p>
          ) : (
            <div className="space-y-2">
              {types.map((t) => (
                <div key={t} className="flex items-center gap-2 rounded-xl border border-white/8 bg-white/[0.02] p-3">
                  {editing === t ? (
                    <>
                      <input value={draft} onChange={(e) => setDraft(e.target.value)} className={`${inputCls} h-10 flex-1`} autoFocus />
                      <button onClick={() => rename(t)} disabled={busy} className="grid h-10 w-10 place-items-center rounded-lg border border-emerald-500/40 text-emerald-400 transition hover:bg-emerald-500/10">
                        <AdminIcon name="check" className="h-4 w-4" />
                      </button>
                      <button onClick={() => setEditing(null)} className="grid h-10 w-10 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:bg-white/5">
                        <AdminIcon name="close" className="h-4 w-4" />
                      </button>
                    </>
                  ) : (
                    <>
                      <span className="flex-1 text-sm font-bold text-white">{t}</span>
                      <button onClick={() => { setEditing(t); setDraft(t); }} className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff]">
                        <AdminIcon name="edit" className="h-4 w-4" />
                      </button>
                      <button onClick={() => remove(t)} className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-rose-500/50 hover:text-rose-400">
                        <AdminIcon name="trash" className="h-4 w-4" />
                      </button>
                    </>
                  )}
                </div>
              ))}
            </div>
          )}
          {error && <p className="mt-3 text-sm text-rose-400">{error}</p>}
        </Card>

        <Card className="h-fit p-6">
          <h3 className="mb-4 text-lg font-bold text-white">افزودن نوع جدید</h3>
          <Field label="نام نوع">
            <input value={newName} onChange={(e) => setNewName(e.target.value)} onKeyDown={(e) => e.key === "Enter" && add()} className={inputCls} placeholder="مثلاً قانونی، کرکی، اشتراکی..." />
          </Field>
          <button onClick={add} disabled={busy || !newName.trim()} className="mt-4 flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
            {busy ? <Spinner /> : "افزودن"}
          </button>
          <p className="mt-4 text-xs leading-6 text-white/45">با تغییر نام یک نوع، همه‌ی پلن‌های محصولاتی که از آن استفاده کرده‌اند به‌صورت خودکار به‌روز می‌شوند.</p>
        </Card>
      </div>
    </div>
  );
}
