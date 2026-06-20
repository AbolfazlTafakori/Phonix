"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Transaction, TxStatus } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { formatToman, parseNumber } from "@/lib/format";
import { PageTitle, Panel, StatCard } from "@/components/account/Panel";

const statusLabel: Record<TxStatus, string> = { Pending: "در انتظار", Approved: "تایید شده", Rejected: "رد شده" };

export default function WalletPage() {
  const { user } = useAuth();
  const [balance, setBalance] = useState(0);
  const [txs, setTxs] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [amount, setAmount] = useState("");
  const [charging, setCharging] = useState(false);
  const [message, setMessage] = useState("");

  async function load() {
    if (!user) return;
    try {
      const [u, mine] = await Promise.all([api.account.me(), api.account.transactions()]);
      setBalance(u.wallet);
      setTxs(mine);
    } catch {
      // keep current values if the wallet can't be refreshed
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
  }, [user]);

  const totals = useMemo(() => {
    const charged = txs.filter((t) => t.status === "Approved" && t.type === "شارژ کیف پول").reduce((s, t) => s + t.amount, 0);
    const referral = txs.filter((t) => t.status === "Approved" && t.type === "پورسانت").reduce((s, t) => s + t.amount, 0);
    return { charged, referral };
  }, [txs]);

  async function charge() {
    const value = parseNumber(amount);
    if (!user || value <= 0) {
      setMessage("مبلغ معتبر وارد کنید.");
      return;
    }
    setCharging(true);
    setMessage("");
    try {
      const tx = await api.transactions.create({ type: "شارژ کیف پول", amount: value, method: "درخواست کاربر" });
      setTxs((prev) => [tx, ...prev]);
      setAmount("");
      setMessage("درخواست شارژ ثبت شد و پس از تأیید پشتیبانی به کیف پول شما اضافه می‌شود.");
    } finally {
      setCharging(false);
    }
  }

  return (
    <div>
      <PageTitle title="کیف پول" desc="موجودی و تراکنش‌های حساب خود را مدیریت کنید." />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <StatCard label="موجودی فعلی" value={formatToman(balance)} accent="#3a64f2" />
        <StatCard label="مجموع شارژ" value={formatToman(totals.charged)} accent="#22c55e" />
        <StatCard label="درآمد معرفی" value={formatToman(totals.referral)} accent="#e60053" />
      </div>

      <Panel className="mb-6">
        <h2 className="mb-4 text-lg font-bold text-white">افزایش موجودی</h2>
        <div className="flex flex-col gap-3 sm:flex-row">
          <input
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            inputMode="numeric"
            placeholder="مبلغ مورد نظر (تومان)"
            className="h-12 flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] placeholder:text-white/35"
          />
          <button
            onClick={charge}
            disabled={charging}
            className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {charging ? "در حال ثبت..." : "ثبت درخواست شارژ"}
          </button>
        </div>
        {message && <p className="mt-3 text-sm text-emerald-400">{message}</p>}
      </Panel>

      <Panel>
        <h2 className="mb-4 text-lg font-bold text-white">تراکنش‌های اخیر</h2>
        {loading ? (
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
          </div>
        ) : txs.length === 0 ? (
          <p className="py-8 text-center text-sm text-white/45">هنوز تراکنشی ندارید.</p>
        ) : (
          <ul className="divide-y divide-white/8">
            {txs.map((t) => (
              <li key={t.id} className="flex items-center justify-between gap-3 py-4">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-white">{t.type}</p>
                  <p className="text-xs text-white/45">
                    {t.date} · {statusLabel[t.status]}
                  </p>
                </div>
                <span className={`shrink-0 text-sm font-bold ${t.amount >= 0 ? "text-emerald-400" : "text-rose-400"}`}>
                  {t.amount >= 0 ? "+" : "−"}
                  {formatToman(Math.abs(t.amount))}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}
