"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import { setCurrentUser } from "@/lib/auth";
import { useCaptcha } from "./Captcha";

const GOOGLE_CLIENT_ID = process.env.NEXT_PUBLIC_GOOGLE_CLIENT_ID;

type Tab = "login" | "register";
type SessionUser = { id: number; name: string; username: string; email: string; avatar?: string };

// Minimal shape of the Google Identity Services global we touch.
type GsiId = {
  initialize: (o: { client_id: string; callback: (r: { credential: string }) => void }) => void;
  renderButton: (el: HTMLElement, o: Record<string, unknown>) => void;
};
type GsiWindow = Window & { google?: { accounts?: { id?: GsiId } } };

function randomUsername() {
  return "Phonix" + Math.random().toString(36).slice(2, 7);
}

/* ── icons ─────────────────────────────────────────────────────────────────── */
const ic = { fill: "none", stroke: "currentColor", strokeWidth: 1.7, strokeLinecap: "round", strokeLinejoin: "round" } as const;
const UserI = () => (<svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" {...ic}><circle cx="12" cy="8" r="4" /><path d="M4 20a8 8 0 0 1 16 0" /></svg>);
const AtI = () => (<svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" {...ic}><circle cx="12" cy="12" r="4" /><path d="M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8" /></svg>);
const MailI = () => (<svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" {...ic}><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m3 7 9 6 9-6" /></svg>);
const LockI = () => (<svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" {...ic}><rect x="4" y="11" width="16" height="10" rx="2" /><path d="M8 11V8a4 4 0 0 1 8 0v3" /></svg>);
const EyeI = ({ off }: { off?: boolean }) => (
  <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" {...ic}>
    {off ? <><path d="M9.9 5A9.8 9.8 0 0 1 12 5c6 0 10 7 10 7a15 15 0 0 1-3 3.5M6.1 6.1A15 15 0 0 0 2 12s4 7 10 7a9.8 9.8 0 0 0 4-.8" /><path d="M3 3l18 18" /></> : <><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></>}
  </svg>
);
const GoogleI = () => (
  <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]">
    <path fill="#EA4335" d="M12 10.2v3.9h5.5a4.7 4.7 0 0 1-2 3.1v2.6h3.3c1.9-1.8 3-4.4 3-7.5 0-.7-.1-1.4-.2-2.1H12z" />
    <path fill="#34A853" d="M12 22c2.7 0 5-.9 6.6-2.4l-3.3-2.6c-.9.6-2 1-3.3 1-2.6 0-4.7-1.7-5.5-4.1H3.1v2.6A10 10 0 0 0 12 22z" />
    <path fill="#FBBC05" d="M6.5 13.9a6 6 0 0 1 0-3.8V7.5H3.1a10 10 0 0 0 0 9l3.4-2.6z" />
    <path fill="#4285F4" d="M12 6.1c1.5 0 2.8.5 3.8 1.5l2.9-2.9A10 10 0 0 0 3.1 7.5l3.4 2.6C7.3 7.8 9.4 6.1 12 6.1z" />
  </svg>
);

/* ── field ─────────────────────────────────────────────────────────────────── */
function Field({ icon, value, onChange, placeholder, type = "text", dir, right }: {
  icon: ReactNode; value: string; onChange: (v: string) => void; placeholder: string;
  type?: string; dir?: "ltr" | "rtl"; right?: ReactNode;
}) {
  return (
    <div className="flex h-12 items-center gap-2.5 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3.5 transition focus-within:border-[#ff7a2e] focus-within:ring-2 focus-within:ring-[#ff7a2e]/15">
      <span className="shrink-0 text-[#9aa0ab]">{icon}</span>
      <input
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        dir={dir}
        autoComplete="off"
        className={`flex-1 bg-transparent text-[13.5px] text-[var(--chat-ink)] outline-none placeholder:text-[var(--chat-muted)] ${dir === "ltr" ? "text-left" : ""}`}
      />
      {right}
    </div>
  );
}

const gradBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#ef233c] to-[#ff7a2e] text-[14px] font-extrabold text-white shadow-[0_14px_30px_-12px_rgba(239,35,60,0.6)] transition hover:brightness-[1.06] disabled:cursor-not-allowed disabled:opacity-60";

export default function AuthTabs({ initial }: { initial: Tab }) {
  const router = useRouter();
  const [tab, setTab] = useState<Tab>(initial);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const captcha = useCaptcha();

  // login
  const [identifier, setIdentifier] = useState("");
  const [loginPassword, setLoginPassword] = useState("");
  const [remember, setRemember] = useState(true);
  const [showLoginPw, setShowLoginPw] = useState(false);

  // 2FA (admin accounts on the main site never hit this, but keep the flow intact)
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const [otp, setOtp] = useState("");

  // register
  const [name, setName] = useState("");
  const [username, setUsername] = useState(randomUsername);
  const [contact, setContact] = useState("");
  const [regPassword, setRegPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [showRegPw, setShowRegPw] = useState(false);
  const [agree, setAgree] = useState(false);
  const [referralCode, setReferralCode] = useState("");

  useEffect(() => {
    const ref = new URLSearchParams(window.location.search).get("ref");
    if (ref) setReferralCode(ref.trim());
  }, []);

  function switchTab(next: Tab) {
    if (next === tab) return;
    setTab(next);
    setError("");
    setChallengeToken(null);
    setOtp("");
    window.history.replaceState(null, "", next === "login" ? "/login" : "/signup");
  }

  function complete(user: SessionUser) {
    setCurrentUser({ id: user.id, name: user.name, username: user.username, email: user.email, avatar: user.avatar });
    router.push("/account");
  }

  async function submitLogin(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const res = await api.auth.login({ identifier, password: loginPassword, remember, captchaId: captcha.id, captchaText: captcha.text });
      if (res.requiresTwoFactor && res.challengeToken) setChallengeToken(res.challengeToken);
      else if (res.user) complete(res.user);
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
      if (res.user) complete(res.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : "کد تأیید نادرست است");
    } finally {
      setBusy(false);
    }
  }

  function validateRegister(): string {
    if (!username.trim()) return "نام کاربری را وارد کنید.";
    if (!contact.trim()) return "ایمیل یا شماره موبایل را وارد کنید.";
    if (contact.includes("@") && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contact.trim())) return "ایمیل واردشده معتبر نیست.";
    if (regPassword.length < 8) return "گذرواژه باید حداقل ۸ کاراکتر باشد.";
    if (!/[a-zA-Z]/.test(regPassword) || !/\d/.test(regPassword)) return "گذرواژه باید ترکیبی از حروف و اعداد باشد.";
    if (regPassword !== confirm) return "گذرواژه و تکرار آن یکسان نیستند.";
    if (!captcha.text.trim()) return "کد امنیتی تصویر را وارد کنید.";
    if (!agree) return "لطفاً قوانین و مقررات را بپذیرید.";
    return "";
  }

  async function submitRegister(e: React.FormEvent) {
    e.preventDefault();
    const v = validateRegister();
    if (v) { setError(v); return; }
    setBusy(true);
    setError("");
    const isEmail = contact.includes("@");
    try {
      const { user } = await api.auth.register({
        name: name.trim(),
        username: username.trim(),
        email: isEmail ? contact.trim() : "",
        phone: isEmail ? "" : contact.trim(),
        password: regPassword,
        referralCode: referralCode || undefined,
        captchaId: captcha.id,
        captchaText: captcha.text,
      });
      complete(user);
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ثبت‌نام");
      captcha.refresh();
    } finally {
      setBusy(false);
    }
  }

  const busyRef = useRef(false);
  busyRef.current = busy;

  // Google Identity Services — renders the official button when a client id is configured.
  const googleBox = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!GOOGLE_CLIENT_ID || challengeToken) return;
    let cancelled = false;
    async function handle(credential: string) {
      if (busyRef.current) return;
      setBusy(true);
      setError("");
      try {
        const { user } = await api.auth.google(credential);
        complete(user);
      } catch (err) {
        setError(err instanceof Error ? err.message : "خطا در ورود با گوگل");
      } finally {
        setBusy(false);
      }
    }
    function render() {
      const g = (window as unknown as GsiWindow).google;
      if (!g?.accounts?.id || !googleBox.current || cancelled) return;
      g.accounts.id.initialize({ client_id: GOOGLE_CLIENT_ID!, callback: (r) => handle(r.credential) });
      googleBox.current.innerHTML = "";
      g.accounts.id.renderButton(googleBox.current, { theme: "outline", size: "large", shape: "pill", text: tab === "login" ? "signin_with" : "signup_with", width: 320, locale: "fa" });
    }
    if ((window as unknown as GsiWindow).google?.accounts?.id) { render(); return () => { cancelled = true; }; }
    const existing = document.getElementById("gsi-script") as HTMLScriptElement | null;
    const script = existing ?? document.createElement("script");
    if (!existing) {
      script.src = "https://accounts.google.com/gsi/client";
      script.async = true;
      script.id = "gsi-script";
      document.head.appendChild(script);
    }
    script.addEventListener("load", render);
    return () => { cancelled = true; script.removeEventListener("load", render); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, challengeToken]);

  /* ── 2FA step ─────────────────────────────────────────────────────────────── */
  if (challengeToken) {
    return (
      <Shell>
        <h1 className="text-center text-[19px] font-extrabold text-[var(--chat-ink)]">تأیید دو‌مرحله‌ای</h1>
        <p className="mb-6 mt-2 text-center text-[12.5px] leading-7 text-[var(--chat-ink-2)]">کد ۶ رقمی برنامه‌ی احرازکننده (Google Authenticator) را وارد کنید.</p>
        <form onSubmit={verifyOtp}>
          <input
            value={otp}
            onChange={(e) => setOtp(e.target.value.replace(/\D/g, "").slice(0, 6))}
            inputMode="numeric"
            autoFocus
            dir="ltr"
            placeholder="------"
            className="h-12 w-full rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] text-center text-lg tracking-[0.5em] text-[var(--chat-ink)] outline-none focus:border-[#ff7a2e]"
          />
          {error && <p className="mt-4 text-[12.5px] text-[#ef233c]">{error}</p>}
          <button type="submit" disabled={busy || otp.length !== 6} className={`mt-6 ${gradBtn}`}>
            {busy ? "در حال بررسی..." : "تأیید و ورود"}
          </button>
          <button type="button" onClick={() => { setChallengeToken(null); setOtp(""); setError(""); }} className="mt-3 h-11 w-full rounded-xl border border-[var(--chat-border)] text-[13px] font-bold text-[var(--chat-ink-2)] transition hover:text-[var(--chat-ink)]">
            بازگشت
          </button>
        </form>
      </Shell>
    );
  }

  const eye = (show: boolean, toggle: () => void) => (
    <button type="button" onClick={toggle} tabIndex={-1} aria-label="نمایش رمز" className="shrink-0 text-[var(--chat-muted)] transition hover:text-[var(--chat-ink-2)]">
      <EyeI off={show} />
    </button>
  );

  return (
    <Shell>
      {/* tabs */}
      <div className="mb-6 flex rounded-xl bg-[var(--chat-surface-2)] p-1">
        {(["register", "login"] as Tab[]).map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => switchTab(t)}
            className={`flex-1 rounded-lg py-2.5 text-[13px] font-bold transition ${tab === t ? "border border-[#ffd9cf] bg-[var(--chat-surface)] text-[#ef233c] shadow-sm" : "text-[var(--chat-ink-2)] hover:text-[var(--chat-ink-2)]"}`}
          >
            {t === "register" ? "ثبت‌نام" : "ورود"}
          </button>
        ))}
      </div>

      {/* heading */}
      <div className="mb-5 text-center">
        <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">{tab === "register" ? "ایجاد حساب کاربری" : "ورود به حساب کاربری"}</h1>
        <p className="mt-1.5 text-[12px] text-[var(--chat-ink-2)]">{tab === "register" ? "در کمتر از یک دقیقه عضو شوید و از خدمات ما بهره‌مند شوید." : "خوش آمدید! اطلاعات خود را وارد کنید."}</p>
      </div>

      {referralCode && tab === "register" && (
        <div className="mb-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-[12px] text-emerald-700">
          با دعوت <span className="font-bold" dir="ltr">{referralCode}</span> در حال ثبت‌نام هستید.
        </div>
      )}

      {tab === "login" ? (
        <form onSubmit={submitLogin} className="flex flex-col gap-3">
          <Field icon={<UserI />} value={identifier} onChange={setIdentifier} placeholder="نام کاربری، ایمیل یا موبایل" />
          <Field icon={<LockI />} type={showLoginPw ? "text" : "password"} value={loginPassword} onChange={setLoginPassword} placeholder="رمز عبور" right={eye(showLoginPw, () => setShowLoginPw((s) => !s))} />
          <CaptchaRow captcha={captcha} />
          <div className="flex items-center justify-between">
            <label className="flex cursor-pointer items-center gap-2 text-[12px] text-[var(--chat-ink-2)]">
              <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} className="h-4 w-4 rounded border-[#d6d9e2] accent-[#ef233c]" />
              مرا به خاطر بسپار
            </label>
            <Link href="/forgot-password" className="text-[12px] font-bold text-[#ef233c] hover:underline">رمز عبور را فراموش کرده‌اید؟</Link>
          </div>
          {error && <p className="text-[12.5px] text-[#ef233c]">{error}</p>}
          <button type="submit" disabled={busy} className={`mt-1 ${gradBtn}`}>
            {busy ? "در حال ورود..." : "ورود به حساب"}
          </button>
        </form>
      ) : (
        <form onSubmit={submitRegister} className="flex flex-col gap-3">
          <Field icon={<UserI />} value={name} onChange={setName} placeholder="نام و نام خانوادگی" />
          <Field icon={<AtI />} value={username} onChange={setUsername} placeholder="نام کاربری" dir="ltr" right={
            <button type="button" onClick={() => setUsername(randomUsername())} tabIndex={-1} className="shrink-0 text-[11px] font-bold text-[#ef233c] hover:underline">جدید</button>
          } />
          <Field icon={<MailI />} value={contact} onChange={setContact} placeholder="ایمیل یا شماره موبایل" dir="ltr" />
          <Field icon={<LockI />} type={showRegPw ? "text" : "password"} value={regPassword} onChange={setRegPassword} placeholder="رمز عبور" right={eye(showRegPw, () => setShowRegPw((s) => !s))} />
          <Field icon={<LockI />} type={showRegPw ? "text" : "password"} value={confirm} onChange={setConfirm} placeholder="تکرار رمز عبور" />
          <CaptchaRow captcha={captcha} />
          <label className="flex items-start gap-2.5 text-[12px] leading-6 text-[var(--chat-ink-2)]">
            <input type="checkbox" checked={agree} onChange={(e) => setAgree(e.target.checked)} className="mt-0.5 h-4 w-4 shrink-0 rounded border-[#d6d9e2] accent-[#ef233c]" />
            <span><Link href="/terms" target="_blank" className="font-bold text-[#ef233c] hover:underline">قوانین و مقررات</Link> را مطالعه کرده و می‌پذیرم.</span>
          </label>
          {error && <p className="text-[12.5px] text-[#ef233c]">{error}</p>}
          <button type="submit" disabled={busy} className={`mt-1 ${gradBtn}`}>
            {busy ? "در حال ثبت..." : "ایجاد حساب کاربری"}
          </button>
        </form>
      )}

      {GOOGLE_CLIENT_ID && (
        <>
          <div className="my-5 flex items-center gap-3 text-[11px] text-[var(--chat-muted)]">
            <span className="h-px flex-1 bg-[var(--chat-border)]" />با<span className="h-px flex-1 bg-[var(--chat-border)]" />
          </div>
          <div ref={googleBox} className="flex justify-center [color-scheme:light]" />
        </>
      )}

      <p className="mt-6 text-center text-[12px] text-[var(--chat-ink-2)]">
        {tab === "register" ? "حساب دارید؟ " : "حساب ندارید؟ "}
        <button type="button" onClick={() => switchTab(tab === "register" ? "login" : "register")} className="font-bold text-[#ef233c] hover:underline">
          {tab === "register" ? "وارد شوید" : "ثبت‌نام کنید"}
        </button>
      </p>
    </Shell>
  );
}

// Split card: promotional visual on the left, the form on the right. A fixed desktop height keeps the
// frame (and the image) the same size whether the login or the taller register form is shown — only the
// form content changes. On small screens the visual is dropped and only the form shows.
function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="grid w-full max-w-[980px] overflow-hidden rounded-[26px] border-2 border-[#ff5a1f]/45 bg-[var(--chat-surface)] shadow-[0_30px_80px_-35px_rgba(239,35,60,0.35)] lg:h-[760px] lg:grid-cols-2">
      {/* form panel (right in RTL) — scrolls inside if content ever exceeds the fixed height */}
      <div className="flex items-center overflow-y-auto px-6 py-8 sm:px-10">
        <div className="mx-auto w-full max-w-[380px]">{children}</div>
      </div>
      {/* promo panel (left in RTL) — visual only, size never changes with the active tab */}
      <div className="relative hidden border-r border-[var(--chat-border)] bg-[#fdf1ec] lg:block">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src="/figma/auth-hero.png" alt="فونیکس وریفای — دنیای خدمات دیجیتال" className="absolute inset-0 h-full w-full object-cover" />
      </div>
    </div>
  );
}

function CaptchaRow({ captcha }: { captcha: ReturnType<typeof useCaptcha> }) {
  return (
    <div className="flex items-center gap-2.5">
      <div className="grid h-12 w-[120px] shrink-0 place-items-center overflow-hidden rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface-2)]">
        {captcha.loading || !captcha.image ? <span className="text-xs text-[var(--chat-muted)]">…</span> : (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={captcha.image} alt="کد امنیتی" className="h-full w-full object-cover" />
        )}
      </div>
      <button type="button" onClick={captcha.refresh} title="تصویر جدید" tabIndex={-1} className="grid h-12 w-10 shrink-0 place-items-center rounded-xl border border-[var(--chat-border)] text-[var(--chat-ink-2)] transition hover:text-[var(--chat-ink)]">↻</button>
      <input value={captcha.text} onChange={(e) => captcha.setText(e.target.value)} dir="ltr" autoComplete="off" placeholder="کد تصویر" className="h-12 flex-1 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3 text-center text-[13px] tracking-[0.2em] text-[var(--chat-ink)] outline-none transition focus:border-[#ff7a2e]" />
    </div>
  );
}
