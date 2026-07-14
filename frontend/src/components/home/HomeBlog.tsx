import Link from "next/link";
import type { BlogPost } from "@/lib/types";
import Img from "@/components/ui/Img";

export default function HomeBlog({ posts, title }: { posts: BlogPost[]; title: string }) {
  // Show the admin-selected posts; if none are flagged, fall back to the most recent so the section is
  // never empty. Capped at 3 to match the grid.
  const featured = posts.filter((p) => p.featuredOnHome);
  const shown = (featured.length > 0 ? featured : posts).slice(0, 3);
  if (shown.length === 0) return null;

  return (
    <section className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16 py-16">
      <div className="mb-8 flex items-end justify-between">
        <div className="flex items-start gap-2">
          <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
          <div>
            <h2 className="text-[22px] sm:text-[26px] xl:text-[30px] font-black text-[var(--hl-ink)]">{title}</h2>
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

      <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {shown.map((p) => (
          <Link
            key={p.id}
            href={`/blog/${p.slug}`}
            className="hl-card group flex items-center gap-4 rounded-[18px] p-4 transition duration-200 hover:-translate-y-1 hover:shadow-[0_18px_40px_-16px_rgba(20,20,20,0.14)]"
          >
            <div className="flex-1">
              <h3 className="line-clamp-2 text-[15px] font-bold leading-[1.7] text-[var(--hl-ink)] transition group-hover:text-[var(--hl-red)]">
                {p.title}
              </h3>
              <div className="mt-3 flex items-center gap-4 text-[12px] text-[var(--hl-muted)]">
                {p.tag && <span className="rounded-full bg-[var(--hl-red)]/10 px-2 py-0.5 font-bold text-[var(--hl-red)]">{p.tag}</span>}
                {p.date && <span>{p.date}</span>}
              </div>
            </div>
            <Img src={p.image} alt={p.title} className="h-24 w-32 shrink-0 rounded-xl object-cover" sizes="256px" />
          </Link>
        ))}
      </div>
    </section>
  );
}
