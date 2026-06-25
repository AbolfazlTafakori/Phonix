"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { User } from "@/lib/types";
import { useAuth, setCurrentUser } from "@/lib/auth";
import { levelBadge } from "@/lib/useMe";
import { PageTitle, Panel } from "@/components/account/Panel";

function CameraIcon({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
      <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
      <circle cx="12" cy="13" r="4" />
    </svg>
  );
}

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] disabled:opacity-60";

export default function ProfilePage() {
  const { user } = useAuth();
  const [data, setData] = useState<User | null>(null);
  const [name, setName] = useState("");
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [avatar, setAvatar] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState("");
  const [avatarBusy, setAvatarBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  // Uploads a new profile picture (public image) and stages it on the avatar; it is persisted when the
  // user submits the form (same as the rest of the profile fields).
  async function uploadAvatar(file: File) {
    setAvatarBusy(true);
    setError("");
    try {
      setAvatar(await api.media.upload(file));
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در آپلود تصویر");
    } finally {
      setAvatarBusy(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  }

  useEffect(() => {
    if (!user) return;
    (async () => {
      try {
        const u = await api.account.me();
        setData(u);
        setName(u.name);
        setUsername(u.username);
        setEmail(u.email);
        setPhone(u.phone);
        setAvatar(u.avatar);
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری اطلاعات");
      } finally {
        setLoading(false);
      }
    })();
  }, [user]);

  // instant KYC propagation: refresh the displayed level when the tab regains focus, without disturbing
  // any edits in progress (only the read-only `data` snapshot is replaced, not the controlled inputs).
  useEffect(() => {
    const sync = () => api.account.me().then(setData).catch(() => {});
    window.addEventListener("focus", sync);
    document.addEventListener("visibilitychange", sync);
    return () => {
      window.removeEventListener("focus", sync);
      document.removeEventListener("visibilitychange", sync);
    };
  }, []);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (!data) return;
    setSaving(true);
    setSaved(false);
    setError("");
    try {
      const updated = await api.account.updateMe({ name, email, phone, username: username.trim(), avatar });
      setData(updated);
      setUsername(updated.username);
      setCurrentUser({ id: updated.id, name: updated.name, username: updated.username, email: updated.email, avatar: updated.avatar });
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
            <div className="mb-8 flex flex-wrap items-center gap-4">
              <button
                type="button"
                onClick={() => !avatarBusy && fileRef.current?.click()}
                aria-label="تغییر تصویر پروفایل"
                className="group relative h-20 w-20 shrink-0 cursor-pointer rounded-full outline-none"
              >
                {avatar ? (
                  <img src={avatar} alt={data.name || data.username} className="h-20 w-20 rounded-full object-cover transition group-hover:brightness-75" />
                ) : (
                  <div className="grid h-20 w-20 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-2xl font-bold text-white transition group-hover:brightness-110">
                    {(data.name || data.username).charAt(0)}
                  </div>
                )}
                {/* camera badge — bottom-right overlay; triggers the hidden file input via the wrapping button */}
                <span className="absolute -bottom-0.5 -right-0.5 grid h-7 w-7 place-items-center rounded-full border-2 border-[#0d0d15] bg-[#3a64f2] text-white shadow-lg transition group-hover:brightness-110">
                  {avatarBusy ? (
                    <span className="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                  ) : (
                    <CameraIcon className="h-3.5 w-3.5" />
                  )}
                </span>
              </button>
              <input
                ref={fileRef}
                type="file"
                accept="image/*"
                className="hidden"
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) uploadAvatar(f);
                }}
              />
              <div className="min-w-0 flex-1 text-left">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="truncate text-lg font-bold text-white">{data.name || data.username}</p>
                  <span className={`rounded-md px-2 py-0.5 text-[11px] font-bold ${levelBadge(data.verificationLevel).cls}`}>
                    احراز هویت: {levelBadge(data.verificationLevel).label}
                  </span>
                </div>
                <p className="mt-0.5 text-sm text-white/50">عضو از {data.joinedAt} · کد {data.code}</p>
              </div>
            </div>

            <form onSubmit={save} className="grid gap-5 sm:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm text-white/80">نام و نام خانوادگی</label>
                <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
              </div>
              <div>
                <label className="mb-2 block text-sm text-white/80">نام کاربری</label>
                <input value={username} onChange={(e) => setUsername(e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="username" />
                <p className="mt-1.5 text-xs text-white/40">فقط حروف و اعداد انگلیسی (بدون فاصله و خط تیره). همین، کد معرف شما هم هست.</p>
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
