import Link from "next/link";
import { notFound } from "next/navigation";
import { getBlogPosts } from "@/lib/content";

export const dynamic = "force-dynamic";

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const post = (await getBlogPosts()).find((p) => p.slug === slug);
  return { title: post ? `${post.title} | بلاگ` : "بلاگ" };
}

export default async function ArticlePage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const posts = await getBlogPosts();
  const post = posts.find((p) => p.slug === slug);
  if (!post) notFound();

  const paragraphs = post.content.split("\n").filter((p) => p.trim().length > 0);

  return (
    <article className="mx-auto max-w-[820px] px-5 pb-20 pt-8">
      <nav className="mb-6 flex items-center gap-2 text-sm text-[var(--hl-muted)]">
        <Link href="/" className="hover:text-[var(--hl-ink)]">خانه</Link>
        <span>/</span>
        <Link href="/blog" className="hover:text-[var(--hl-ink)]">بلاگ</Link>
        <span>/</span>
        <span className="text-[var(--hl-ink-2)]">{post.title}</span>
      </nav>

      <div className="overflow-hidden rounded-3xl border border-[var(--hl-border)]">
        <img loading="lazy" decoding="async" src={post.image} alt={post.title} className="h-64 w-full object-cover sm:h-80" />
      </div>

      <div className="mt-6 flex items-center gap-3 text-sm text-[var(--hl-ink-2)]">
        <span className="rounded-full bg-[#6d28d9]/15 px-3 py-1 font-medium text-[#c98bff]">{post.tag}</span>
        <span>{post.date}</span>
      </div>

      <h1 className="mt-4 text-3xl font-bold leading-snug text-[var(--hl-ink)] sm:text-4xl">{post.title}</h1>

      <div className="mt-6 space-y-5">
        {paragraphs.map((p, i) => (
          <p key={i} className="text-[15px] leading-9 text-[var(--hl-ink-2)]">{p}</p>
        ))}
      </div>

      <div className="mt-12 border-t border-[var(--hl-border)] pt-6">
        <Link href="/blog" className="text-sm font-bold text-[#e60053] hover:underline">→ بازگشت به بلاگ</Link>
      </div>
    </article>
  );
}
