import Link from "next/link";

type Cat = { title: string; logo: string; href: string };

// Ordered so each row reads right-to-left matching the reference:
// row 1 (R→L): VPN · گیفت کارت · استریم · موزیک · نتفلیکس
// row 2 (R→L): شبکه‌های اجتماعی · نرم‌افزارها · بازی · شماره مجازی · وریفای
const categories: Cat[] = [
  { title: "فیلترشکن / VPN", logo: "/figma/cat-vpn.png", href: "/products" },
  { title: "گیفت کارت", logo: "/figma/cat-giftcard.png", href: "/products" },
  { title: "اکانت‌های استریم", logo: "/figma/cat-stream.png", href: "/products" },
  { title: "اپل موزیک و اسپاتیفای", logo: "/figma/cat-music.png", href: "/products" },
  { title: "نتفلیکس", logo: "/figma/cat-netflix.png", href: "/products" },
  { title: "شبکه‌های اجتماعی", logo: "/figma/cat-social.png", href: "/products" },
  { title: "نرم‌افزارها", logo: "/figma/cat-software.png", href: "/products" },
  { title: "بازی و سرگرمی", logo: "/figma/cat-game.png", href: "/products" },
  { title: "شماره مجازی", logo: "/figma/cat-number.png", href: "/products" },
  { title: "تایید و وریفای حساب", logo: "/figma/cat-verify.png", href: "/products" },
];

export default function HomeCategories() {
  return (
    <section className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16 py-20">
      <div className="mb-8 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="h-2.5 w-2.5 rounded-full bg-[var(--hl-red)]" />
          <h2 className="text-[22px] sm:text-[26px] xl:text-[30px] font-black text-[var(--hl-ink)]">دسته‌بندی خدمات و محصولات</h2>
        </div>
        <Link
          href="/products"
          className="shrink-0 rounded-xl border border-[var(--hl-border)] bg-white px-4 py-2 text-[16px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff6f2]"
        >
          مشاهده همه
        </Link>
      </div>

      <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-5">
        {categories.map((c) => (
          <Link
            key={c.title}
            href={c.href}
            className="group flex flex-col items-center gap-4 rounded-[20px] border border-[var(--hl-border)] bg-[#fafbfc] p-6 transition duration-200 hover:-translate-y-1 hover:border-[#ff5a1f] hover:bg-white hover:shadow-[0_18px_40px_-16px_rgba(239,35,60,0.22)]"
          >
            <div className="flex h-32 items-center justify-center">
              <img src={c.logo} alt={c.title} className="max-h-32 w-auto object-contain transition duration-200 group-hover:scale-105" />
            </div>
            <h3 className="text-center text-[20px] font-bold text-[var(--hl-ink)]">{c.title}</h3>
          </Link>
        ))}
      </div>
    </section>
  );
}
