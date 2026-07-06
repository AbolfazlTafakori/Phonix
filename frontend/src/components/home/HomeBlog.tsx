import Link from "next/link";

type Post = { title: string; date: string; views: string; image: string };

const posts: Post[] = [
  { title: "بهترین VPN برای دور زدن محدودیت‌های اینترنت در ۱۴۰۳", date: "۱۵ خرداد ۱۴۰۳", views: "۱۲.۵K", image: "/figma/blog-1.png" },
  { title: "راهنمای کامل خرید گیفت کارت از سایت‌های معتبر", date: "۱۲ خرداد ۱۴۰۳", views: "۹.۸K", image: "/figma/blog-2.png" },
  { title: "نکات طلایی برای انتخاب اکانت‌های اشتراکی و اختصاصی در سرویس‌های استریم", date: "۹ خرداد ۱۴۰۳", views: "۷.۶K", image: "/figma/blog-3.png" },
];

export default function HomeBlog() {
  return (
    <section className="mx-auto max-w-[1840px] px-16 py-16">
      <div className="mb-8 flex items-end justify-between">
        <div className="flex items-start gap-2">
          <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
          <div>
            <h2 className="text-[30px] font-black text-[var(--hl-ink)]">آخرین مطالب وبلاگ</h2>
            <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">راهنماها و مقالات کاربردی دنیای دیجیتال</p>
          </div>
        </div>
        <Link
          href="/blog"
          className="shrink-0 rounded-xl border border-[var(--hl-border)] bg-white px-4 py-2 text-[16px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff6f2]"
        >
          مشاهده همه
        </Link>
      </div>

      <div className="grid grid-cols-3 gap-5">
        {posts.map((p) => (
          <Link
            key={p.title}
            href="/blog"
            className="hl-card group flex items-center gap-4 rounded-[18px] p-4 transition duration-200 hover:-translate-y-1 hover:shadow-[0_18px_40px_-16px_rgba(20,20,20,0.14)]"
          >
            <div className="flex-1">
              <h3 className="line-clamp-2 text-[15px] font-bold leading-[1.7] text-[var(--hl-ink)] transition group-hover:text-[var(--hl-red)]">
                {p.title}
              </h3>
              <div className="mt-3 flex items-center gap-4 text-[12px] text-[var(--hl-muted)]">
                <span className="flex items-center gap-1">
                  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7-11-7-11-7z" /><circle cx="12" cy="12" r="3" /></svg>
                  {p.views}
                </span>
                <span>{p.date}</span>
              </div>
            </div>
            <img src={p.image} alt={p.title} className="h-24 w-32 shrink-0 rounded-xl object-cover" />
          </Link>
        ))}
      </div>
    </section>
  );
}
