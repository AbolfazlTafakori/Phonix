"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { User } from "@/lib/types";
import { useAuth, setCurrentUser } from "@/lib/auth";
import { PageTitle, Panel } from "@/components/account/Panel";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] disabled:opacity-60";

export default function ProfilePage() {
  const { user } = useAuth();
  const [data, setData] = useState<User | null>(null);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!user) return;
    (async () => {
      try {
        const u = await api.users.get(user.id);
        setData(u);
        setName(u.name);
        setEmail(u.email);
        setPhone(u.phone);
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری اطلاعات");
      } finally {
        setLoading(false);
      }
    })();
  }, [user]);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (!data) return;
    setSaving(true);
    setSaved(false);
    setError("");
    try {
      const updated = await api.users.update(data.id, { name, email, phone });
      setData(updated);
      setCurrentUser({ id: updated.id, name: updated.name, username: updated.username, email: updated.email });
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ذخیره");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <PageTitle title="پروفایل من" desc="اطلاعات حساب کاربری خود را مشاهده و ویرایش کنید." />

      <Panel>
        {loading || !data ? (
          <div className="grid h-40 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
          </div>
        ) : (
          <>
            <div className="mb-8 flex items-center gap-4">
              <div className="grid h-20 w-20 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-2xl font-bold text-white">
                {(data.name || data.username).charAt(0)}
              </div>
              <div className="min-w-0">
                <p className="truncate text-lg font-bold text-white">{data.name || data.username}</p>
                <p className="text-sm text-white/50">عضو از {data.joinedAt} · کد {data.code}</p>
              </div>
            </div>

            <form onSubmit={save} className="grid gap-5 sm:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm text-white/80">نام و نام خانوادگی</label>
                <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
              </div>
              <div>
                <label className="mb-2 block text-sm text-white/80">نام کاربری</label>
                <input value={data.username} dir="ltr" disabled className={`${inputCls} text-left`} />
              </div>
              <div>
                <label className="mb-2 block text-sm text-white/80">ایمیل</label>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
              </div>
              <div>
                <label className="mb-2 block text-sm text-white/80">شماره موبایل</label>
                <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="—" />
              </div>

              <div className="flex items-center gap-3 sm:col-span-2">
                <button
                  type="submit"
                  disabled={saving}
                  className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-10 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
                >
                  {saving ? "در حال ذخیره..." : "ذخیره تغییرات"}
                </button>
                {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
                {error && <span className="text-sm text-rose-400">{error}</span>}
              </div>
            </form>
          </>
        )}
      </Panel>
    </div>
  );
}
