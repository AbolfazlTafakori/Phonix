"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatToman, formatNumber } from "@/lib/format";
import type { ReferralReport } from "@/lib/types";
import { PageTitle, Panel, StatCard } from "@/components/account/Panel";

export default function ReferralPage() {
  const { user } = useAuth();
  const [report, setReport] = useState<ReferralReport | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!user) return;
    api.account
      .referrals()
      .then(setReport)
      .catch(() => setReport({ totalEarned: 0, referredCount: 0, earnings: [] }))
      .finally(() => setLoading(false));
  }, [user]);

  const earnings = report?.earnings ?? [];

  return (
    <div>
      <PageTitle title="گزارش درآمد معرف" desc="درآمد حاصل از معرفی دوستان خود را دنبال کنید." />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <StatCard label="مجموع درآمد" value={formatToman(report?.totalEarned ?? 0)} accent="#e60053" />
        <StatCard label="تعداد معرفی" value={`${formatNumber(report?.referredCount ?? 0)} نفر`} accent="#3a64f2" />
        <StatCard label="سفارش پورسانت‌دار" value={`${formatNumber(earnings.length)} سفارش`} accent="#f59e0b" />
      </div>

      <Panel className="overflow-x-auto p-0">
        {loading ? (
          <div className="grid h-32 place-items-center">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.2)] border-t-[#FF5A1F]" />
          </div>
        ) : earnings.length === 0 ? (
          <p className="py-12 text-center text-sm" style={{ color: "var(--ac-muted)" }}>
            هنوز پورسانتی ثبت نشده است. با لینک دعوت خود دوستانتان را به خرید دعوت کنید.
          </p>
        ) : (
          <table className="w-full min-w-[680px] text-right">
            <thead>
              <tr className="border-b border-[color:var(--ac-panel-border)] text-sm" style={{ color: "var(--ac-muted)" }}>
                <th className="px-6 py-4 font-medium">کاربر معرفی‌شده</th>
                <th className="px-6 py-4 font-medium">شماره سفارش</th>
                <th className="px-6 py-4 font-medium">مبلغ سفارش</th>
                <th className="px-6 py-4 font-medium">میزان پورسانت</th>
                <th className="px-6 py-4 font-medium">تاریخ</th>
              </tr>
            </thead>
            <tbody>
              {earnings.map((r, i) => (
                <tr key={i} className="border-b border-[color:var(--ac-divider)] text-sm transition hover:bg-[color:var(--ac-menu-hover)]" style={{ color: "var(--ac-title)" }}>
                  <td className="px-6 py-4">{r.referredName}</td>
                  <td className="px-6 py-4 font-mono" style={{ color: "var(--ac-muted)" }}>{r.orderCode}</td>
                  <td className="px-6 py-4">{formatToman(r.orderAmount)}</td>
                  <td className="px-6 py-4 font-bold text-emerald-600">{formatToman(r.commission)}</td>
                  <td className="px-6 py-4" style={{ color: "var(--ac-muted)" }}>{r.date}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </div>
  );
}
