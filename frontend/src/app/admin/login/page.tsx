"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { setAdminUser, adminRoles } from "@/lib/adminAuth";
import { useCaptcha, CaptchaField } from "@/components/auth/Captcha";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3a64f2]";

type AdminLoginUser = NonNullable<Awaited<ReturnType<typeof api.auth.login>>["user"]>;

export default function AdminLoginPage() {
  const router = useRouter();
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const [otp, setOtp] = useState("");
  const captcha = useCaptcha();

  async function complete(user: AdminLoginUser) {
    if (!adminRoles.includes(user.role)) {
      await api.auth.logout().catch(() => {});
      setChallengeToken(null);
      setError("این حساب دسترسی به پنل مدیریت ندارد.");
      return;
    }
    setAdminUser({ id: user.id, name: user.name, username: user.username, role: user.role });
    router.replace("/admin");
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const res = await api.auth.login({ identifier, password, captchaId: captcha.id, captchaText: captcha.text, admin: true });
      if (res.requiresTwoFactor && res.challengeToken) {
        setChallengeToken(res.challengeToken);
      } else if (res.user) {
        await complete(res.user);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ورود");
      captcha.refresh();
    } finally {
      setBusy(false);
    }
  }

  async function verifyOtp(e: React.FormEvent) {
    e.preventDefault();
    if (!challengeToken) return;
    setBusy(true);
    setError("");
    try {
      const res = await api.auth.verifyTwoFactor(challengeToken, otp.trim());
      if (res.user) await complete(res.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : "کد تأیید نادرست است");
    } finally {
      setBusy(false);
    }
  }

  if (challengeToken) {
    return (
      <div className="grid min-h-screen place-items-center bg-[#0b0b12] px-5">
        <div className="w-full max-w-sm rounded-2xl border border-white/10 bg-[#15151f] p-8 shadow-2xl">
          <div className="mb-6 flex flex-col items-center gap-2 text-center">
            <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-12 w-auto" />
            <h1 className="text-lg font-bold text-white">تأیید دو‌مرحله‌ای</h1>
            <p className="text-xs text-white/45">کد ۶ رقمی برنامه‌ی احرازکننده را وارد کنید</p>
          </div>
          <form onSubmit={verifyOtp} className="grid gap-4">
            <input
              value={otp}
              onChange={(e) => setOtp(e.target.value.replace(/\D/g, "").slice(0, 6))}
              inputMode="numeric"
              autoFocus
              dir="ltr"
              placeholder="------"
              className={`${inputCls} text-center text-lg tracking-[0.5em]`}
            />
            {error && <p className="text-sm text-rose-400">{error}</p>}
            <button
              type="submit"
              disabled={busy || otp.length !== 6}
              className="mt-2 h-12 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
            >
              {busy ? "در حال بررسی..." : "تأیید و ورود"}
            </button>
            <button
              type="button"
              onClick={() => { setChallengeToken(null); setOtp(""); setError(""); }}
              className="h-11 rounded-xl border border-white/10 text-sm font-medium text-white/60 transition hover:text-white"
            >
              بازگشت
            </button>
          </form>
        </div>
      </div>
    );
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

          <CaptchaField captcha={captcha} />

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
