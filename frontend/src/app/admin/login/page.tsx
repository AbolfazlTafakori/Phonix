"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { setAdminUser, adminRoles } from "@/lib/adminAuth";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3a64f2]";

export default function AdminLoginPage() {
  const router = useRouter();
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const { user } = await api.auth.login({ identifier, password });
      if (!adminRoles.includes(user.role)) {
        await api.auth.logout().catch(() => {});
        setError("این حساب دسترسی به پنل مدیریت ندارد.");
        return;
      }
      setAdminUser({ id: user.id, name: user.name, username: user.username, role: user.role });
      router.replace("/admin");
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ورود");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid min-h-screen place-items-center bg-[#0b0b12] px-5">
      <div className="w-full max-w-sm rounded-2xl border border-white/10 bg-[#15151f] p-8 shadow-2xl">
        <div className="mb-6 flex flex-col items-center gap-2 text-center">
          <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-12 w-auto" />
          <h1 className="text-lg font-bold text-white">ورود به پنل مدیریت</h1>
          <p className="text-xs text-white/45">فقط مدیران مجاز به ورود هستند</p>
        </div>

        <form onSubmit={submit} className="grid gap-4">
          <div>
            <label className="mb-2 block text-sm text-white/80">نام کاربری</label>
            <input value={identifier} onChange={(e) => setIdentifier(e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
          </div>
          <div>
            <label className="mb-2 block text-sm text-white/80">گذرواژه</label>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} />
          </div>

          {error && <p className="text-sm text-rose-400">{error}</p>}

          <button
            type="submit"
            disabled={busy}
            className="mt-2 h-12 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {busy ? "در حال ورود..." : "ورود"}
          </button>
        </form>
      </div>
    </div>
  );
}
