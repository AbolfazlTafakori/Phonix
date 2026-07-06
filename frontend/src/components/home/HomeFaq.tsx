"use client";

import { useState } from "react";

type Item = { q: string; a: string };

const items: Item[] = [
  { q: "چطور محصول یا شماره را بعد از خرید دریافت می‌کنم؟", a: "بعد از پرداخت موفق، محصول یا شماره بلافاصله در پنل کاربری یا ایمیل شما ارسال می‌شود." },
  { q: "زمان تحویل محصولات چقدر است؟", a: "بیشتر محصولات آنی تحویل داده می‌شوند و در موارد خاص در کوتاه‌ترین زمان ممکن." },
  { q: "آیا محصولات شما قانونی و اورجینال هستند؟", a: "بله، تمام محصولات از منابع معتبر و کاملاً اورجینال تهیه و ارائه می‌شوند." },
  { q: "آیا امکان بازگشت وجه وجود دارد؟", a: "در صورت بروز مشکل در تحویل، مبلغ پرداختی طبق قوانین سایت به شما بازگردانده می‌شود." },
  { q: "در صورت بروز مشکل چگونه پشتیبانی دریافت کنم؟", a: "از طریق تیکت، چت آنلاین یا کانال تلگرام به‌صورت شبانه‌روزی پاسخگوی شما هستیم." },
  { q: "آیا برای خرید نیاز به ثبت‌نام دارم؟", a: "برای پیگیری سفارش و دریافت پشتیبانی بهتر، ثبت‌نام توصیه می‌شود و بسیار سریع است." },
];

function Row({ item, open, onToggle }: { item: Item; open: boolean; onToggle: () => void }) {
  return (
    <div className={`hl-card overflow-hidden rounded-[16px] transition ${open ? "border-[#ff5a1f]" : ""}`}>
      <button onClick={onToggle} className="flex w-full items-center justify-between gap-3 px-5 py-4 text-right">
        <span className="text-[15px] font-bold text-[var(--hl-ink)]">{item.q}</span>
        <span className={`grid h-7 w-7 shrink-0 place-items-center rounded-lg text-[18px] font-bold transition ${open ? "bg-gradient-to-br from-[#ef233c] to-[#ff5a1f] text-white" : "bg-[#fff6f2] text-[var(--hl-red)]"}`}>
          {open ? "−" : "+"}
        </span>
      </button>
      {open && <p className="px-5 pb-5 text-[14px] leading-[1.9] text-[var(--hl-ink-2)]">{item.a}</p>}
    </div>
  );
}

export default function HomeFaq() {
  const [openIdx, setOpenIdx] = useState(0);
  const cols = [items.slice(0, 3), items.slice(3)];

  return (
    <section className="mx-auto max-w-[1600px] px-16 py-16">
      <div className="mb-8 flex items-start gap-2">
        <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
        <div>
          <h2 className="text-[30px] font-black text-[var(--hl-ink)]">سوالات متداول</h2>
          <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">پاسخ پرتکرارترین سوال‌های شما</p>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-5">
        {cols.map((col, c) => (
          <div key={c} className="flex flex-col gap-4">
            {col.map((item) => {
              const idx = c * 3 + col.indexOf(item);
              return <Row key={item.q} item={item} open={openIdx === idx} onToggle={() => setOpenIdx((v) => (v === idx ? -1 : idx))} />;
            })}
          </div>
        ))}
      </div>
    </section>
  );
}
