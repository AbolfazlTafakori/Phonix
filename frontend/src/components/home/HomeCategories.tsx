import Link from "next/link";
import { getHomeCategories } from "@/lib/content";

// Tiles come from the admin panel's home-categories editor. They used to be a hardcoded list here, so
// every edit made in the panel — titles, ordering, uploaded icons, links — was silently discarded.
// getHomeCategories already filters to active, sorts, and falls back to the static set if the API is down.
export default async function HomeCategories() {
  const categories = await getHomeCategories();

  return (
    <section className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16 py-20">
      <div className="mb-8 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="h-2.5 w-2.5 rounded-full bg-[var(--hl-red)]" />
          <h2 className="text-[22px] sm:text-[26px] xl:text-[30px] font-black text-[var(--hl-ink)]">دسته‌بندی خدمات و محصولات</h2>
        </div>
        <Link
          href="/products"
          className="shrink-0 rounded-xl border border-[var(--hl-border)] bg-white px-4 py-2 text-[16px] font-bold text-[var(--hl-red-text)] transition hover:bg-[#fff6f2]"
        >
          مشاهده همه
        </Link>
      </div>

      <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-5">
        {categories.map((c) => (
          <Link
            key={c.id}
            href={c.href}
            style={{ boxShadow: "var(--cat-shadow)" }}
            className="group flex flex-col items-center gap-4 rounded-[20px] border border-[var(--hl-border)] bg-white p-6 transition duration-200 hover:-translate-y-1.5 hover:border-[#ff5a1f]/60"
          >
            <div className="flex h-32 items-center justify-center">
              {/* iconClass carries the per-tile nudge the panel stores for artwork that sits off-centre. */}
              <img
                loading="lazy"
                decoding="async"
                src={c.icon}
                alt={c.title}
                className={`max-h-32 w-auto object-contain transition duration-200 group-hover:scale-105 ${c.iconClass}`}
              />
            </div>
            <h3 className="text-center text-[20px] font-bold text-[var(--hl-ink)]">{c.title}</h3>
          </Link>
        ))}
      </div>
    </section>
  );
}
