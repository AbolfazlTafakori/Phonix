"use client";

import Link from "next/link";
import { useState } from "react";

type Item = { q: string; a: string };

const items: Item[] = [
  { q: "چطور می‌توانم دسته‌بندی مناسب را انتخاب کنم؟", a: "کافیست از میان دسته‌بندی‌های بالای همین صفحه، نیاز موردنظر خود را انتخاب کنید تا محصولات مرتبط با آن دسته را مشاهده کنید." },
  { q: "تفاوت بین شماره مجازی و واقعی چیست؟", a: "شماره مجازی صرفاً برای دریافت پیامک تایید و فعال‌سازی حساب‌ها استفاده می‌شود و مانند سیم‌کارت فیزیکی نیست." },
  { q: "آیا خدمات شما ضمانت بازگشت دارد؟", a: "بله، در صورت بروز هرگونه مشکل در تحویل یا کیفیت سرویس، طبق قوانین سایت مبلغ پرداختی به شما بازگردانده می‌شود." },
  { q: "مدت زمان تحویل سفارش‌ها چقدر است؟", a: "بیشتر محصولات به‌صورت آنی و خودکار پس از پرداخت تحویل داده می‌شوند و در موارد خاص در کوتاه‌ترین زمان ممکن." },
];

function Row({ item, open, onToggle }: { item: Item; open: boolean; onToggle: () => void }) {
  return (
    <div className={`hl-card overflow-hidden rounded-[16px] transition ${open ? "border-[#ff5a1f]" : ""}`}>
      <button onClick={onToggle} className="flex w-full items-center justify-between gap-3 px-5 py-4 text-right">
        <span className="text-[15px] font-bold text-[var(--hl-ink)]">{item.q}</span>
        <svg viewBox="0 0 24 24" className={`h-5 w-5 shrink-0 text-[var(--hl-red)] transition ${open ? "rotate-180" : ""}`} fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M6 9l6 6 6-6" />
        </svg>
      </button>
      {open && <p className="px-5 pb-5 text-[14px] leading-[1.9] text-[var(--hl-ink-2)]">{item.a}</p>}
    </div>
  );
}

export default function CategoriesFaq() {
  const [openIdx, setOpenIdx] = useState(-1);

  return (
    <section className="mx-auto max-w-[1840px] px-4 py-16 sm:px-8 xl:px-16">
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_320px]">
        {/* right: questions */}
        <div>
          <h2 className="mb-6 text-right text-[24px] font-black text-[var(--hl-ink)] sm:text-[28px]">سوالات متداول</h2>
          <div className="flex flex-col gap-4">
            {items.map((item, i) => (
              <Row key={item.q} item={item} open={openIdx === i} onToggle={() => setOpenIdx((v) => (v === i ? -1 : i))} />
            ))}
          </div>
        </div>

        {/* left: support card */}
        <div className="hl-card flex flex-col items-center justify-center rounded-[22px] p-6 text-center">
          <h3 className="text-[19px] font-black text-[var(--hl-ink)]">هنوز سوالی داری؟ 👋</h3>
          <p className="mt-2 text-[14px] leading-7 text-[var(--hl-muted)]">تیم پشتیبانی ما آماده پاسخگویی به سوالات شماست.</p>
          <img src="/figma/catpage-faq-support.png" alt="" className="my-5 h-36 w-36 object-contain" />
          <Link
            href="/account/tickets"
            className="flex w-full items-center justify-center gap-2 rounded-xl py-3.5 text-[16px] font-bold text-white"
            style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
          >
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="currentColor"><path d="M12 3a9 9 0 00-9 9v4a3 3 0 003 3h1v-7H5v-0a7 7 0 0114 0v0h-2v7h2a3 3 0 003-3v-4a9 9 0 00-9-9z" /></svg>
            تماس با پشتیبانی
          </Link>
        </div>
      </div>
    </section>
  );
}
