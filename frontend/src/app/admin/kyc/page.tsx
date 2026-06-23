"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { KycRequest, KycStatus } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, StatusBadge } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const statusLabel: Record<KycStatus, string> = { Pending: "در انتظار", Approved: "تایید شده", Rejected: "رد شده" };
type Filter = "Pending" | "Approved" | "Rejected" | "all";

export default function AdminKycPage() {
  const [items, setItems] = useState<KycRequest[]>([]);
  const [usernames, setUsernames] = useState<Record<number, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("Pending");
  const [busy, setBusy] = useState<number | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const [ks, us] = await Promise.all([api.kyc.list(), api.users.list()]);
        setItems(ks);
        setUsernames(Object.fromEntries(us.map((u) => [u.id, `${u.username} · ${u.code}`])));
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const counts = useMemo(
    () => ({
      Pending: items.filter((k) => k.status === "Pending").length,
      Approved: items.filter((k) => k.status === "Approved").length,
      Rejected: items.filter((k) => k.status === "Rejected").length,
    }),
    [items],
  );
  const shown = filter === "all" ? items : items.filter((k) => k.status === filter);

  async function approve(k: KycRequest) {
    setBusy(k.id);
    try {
      const updated = await api.kyc.approve(k.id);
      setItems((p) => p.map((x) => (x.id === k.id ? updated : x)));
    } finally {
      setBusy(null);
    }
  }
  async function reject(k: KycRequest) {
    const note = prompt("دلیل رد (اختیاری):") ?? "";
    setBusy(k.id);
    try {
      const updated = await api.kyc.reject(k.id, note);
      setItems((p) => p.map((x) => (x.id === k.id ? updated : x)));
    } finally {
      setBusy(null);
    }
  }

  const filters: { key: Filter; label: string; count?: number }[] = [
    { key: "Pending", label: "در انتظار", count: counts.Pending },
    { key: "Approved", label: "تایید شده", count: counts.Approved },
    { key: "Rejected", label: "رد شده", count: counts.Rejected },
    { key: "all", label: "همه" },
  ];

  return (
    <div>
      <PageHeader title="احراز هویت" desc="بررسی و تأیید مدارک هویتی کاربران" />

      <div className="mb-5 flex flex-wrap gap-2">
        {filters.map((f) => (
          <button
            key={f.key}
            onClick={() => setFilter(f.key)}
            className={`flex items-center gap-2 rounded-full border px-4 py-1.5 text-sm font-medium transition ${
              filter === f.key ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {f.label}
            {f.count ? <span className="rounded-full bg-[#e60053]/20 px-1.5 text-[11px] font-bold text-[#ff5a8a]">{formatNumber(f.count)}</span> : null}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : shown.length === 0 ? (
        <Card className="p-12 text-center text-white/40">موردی در این وضعیت وجود ندارد</Card>
      ) : (
        <div className="grid gap-5 lg:grid-cols-2">
          {shown.map((k) => (
            <Card key={k.id} className="p-5">
              <div className="mb-4 flex items-center justify-between gap-3">
                <div>
                  <p className="font-bold text-white">{k.fullName}</p>
                  <p className="text-xs text-white/45" dir="ltr">{usernames[k.userId] ?? `#${k.userId}`}</p>
                </div>
                <StatusBadge status={statusLabel[k.status]} />
              </div>

              <div className="mb-4 grid grid-cols-2 gap-3 text-sm">
                <p className="text-white/60">کد ملی: <span className="text-white" dir="ltr">{k.nationalId}</span></p>
                <p className="text-white/60">تاریخ تولد: <span className="text-white">{k.birthDate || "—"}</span></p>
                <p className="text-white/60">تاریخ ارسال: <span className="text-white">{k.date}</span></p>
              </div>

              <div className="mb-4 grid grid-cols-2 gap-3">
                <Doc label="کارت ملی" src={k.cardImage} />
                <Doc label="سلفی" src={k.selfieImage} />
              </div>

              {k.note && <p className="mb-3 rounded-lg bg-rose-500/10 px-3 py-2 text-xs text-rose-300">دلیل رد: {k.note}</p>}

              <div className="flex items-center gap-2 border-t border-white/8 pt-4">
                {k.status !== "Approved" && (
                  <button onClick={() => approve(k)} disabled={busy === k.id} className="flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg bg-emerald-500/15 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/25 active:scale-[0.98] lg:h-9">
                    {busy === k.id ? <Spinner /> : <><AdminIcon name="check" className="h-4 w-4" /> تأیید هویت</>}
                  </button>
                )}
                {k.status !== "Rejected" && (
                  <button onClick={() => reject(k)} disabled={busy === k.id} className="flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg bg-rose-500/15 text-xs font-bold text-rose-400 transition hover:bg-rose-500/25 active:scale-[0.98] lg:h-9">
                    <AdminIcon name="close" className="h-4 w-4" /> رد
                  </button>
                )}
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

function Doc({ label, src }: { label: string; src: string }) {
  // KYC images are streamed from the protected, staff-authorized download endpoint (never a public URL).
  const resolved = src ? api.kyc.imageSrc(src) : "";
  return (
    <div>
      <p className="mb-1.5 text-xs text-white/45">{label}</p>
      {src ? (
        <a href={resolved} target="_blank" rel="noreferrer" className="block overflow-hidden rounded-xl border border-white/10">
          <img src={resolved} alt={label} className="h-32 w-full object-cover transition hover:opacity-80" />
        </a>
      ) : (
        <div className="grid h-32 place-items-center rounded-xl border border-dashed border-white/10 text-xs text-white/30">ارسال نشده</div>
      )}
    </div>
  );
}
