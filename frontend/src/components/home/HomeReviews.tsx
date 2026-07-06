type Review = { name: string; text: string };

const reviews: Review[] = [
  { name: "سینا حسینی", text: "همه چیز عالی بود، پشتیبانی سریع و واقعاً پیشنهاد می‌کنم." },
  { name: "علی کریمی", text: "خدمات سایت بی‌نظیره، قیمت‌ها هم خیلی خوبن، ممنونم." },
  { name: "ندا احمدی", text: "شماره به‌موقع تحویل شد. پشتیبانی سریع و پاسخگو، عالیه!" },
  { name: "محمد رضایی", text: "از کیفیت و سرعت سرویس خیلی راضی‌ام، حتماً دوباره خرید می‌کنم." },
];

function Stars() {
  return (
    <div className="flex items-center gap-0.5 text-[#ffb020]">
      {Array.from({ length: 5 }).map((_, i) => (
        <svg key={i} viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor">
          <path d="M12 2l2.9 6 6.6.9-4.8 4.6 1.1 6.5L12 17.8 6.2 20l1.1-6.5L2.5 8.9 9 8z" />
        </svg>
      ))}
    </div>
  );
}

export default function HomeReviews() {
  return (
    <section className="mx-auto max-w-[1840px] px-16 py-16">
      <div className="mb-8 flex items-start gap-2">
        <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
        <div>
          <h2 className="text-[30px] font-black text-[var(--hl-ink)]">نظرات مشتریان</h2>
          <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">تجربه‌ی واقعی کاربرانی که از ما خرید کرده‌اند</p>
        </div>
      </div>

      <div className="grid grid-cols-4 gap-5">
        {reviews.map((r) => (
          <div key={r.name} className="hl-card flex flex-col gap-4 rounded-[20px] p-6">
            <Stars />
            <p className="flex-1 text-[16px] leading-[1.9] text-[var(--hl-ink-2)]">{r.text}</p>
            <div className="flex items-center gap-3 border-t border-[var(--hl-border)] pt-4">
              <span className="grid h-11 w-11 place-items-center rounded-full bg-gradient-to-br from-[#ef233c] to-[#ff5a1f] text-[17px] font-black text-white">
                {r.name.charAt(0)}
              </span>
              <div>
                <div className="text-[16px] font-bold text-[var(--hl-ink)]">{r.name}</div>
                <div className="text-[14px] text-[var(--hl-muted)]">مشتری</div>
              </div>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
