import Link from "next/link";
import { getBlogPosts, getSiteContent } from "@/lib/content";
import { absoluteUrl, jsonLdScript } from "@/lib/seo";
import Reveal from "@/components/Reveal";

// Rendered per request over a cached read, for the same reason as the category index.
export const dynamic = "force-dynamic";
export const metadata = {
  title: "بلاگ و مقالات آموزشی",
  description: "راهنمای خرید اکانت‌های پریمیوم قانونی، مقایسه ابزارهای هوش مصنوعی، وریفای حساب‌ها و اخبار خدمات دیجیتال در بلاگ فونیکس وریفای.",
};

export default async function BlogPage() {
  const [posts, content] = await Promise.all([getBlogPosts(), getSiteContent()]);
  const activePosts = posts.filter((p) => p.isActive);

  const breadcrumbLd = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      { "@type": "ListItem", position: 1, name: "صفحه اصلی", item: absoluteUrl("/") },
      { "@type": "ListItem", position: 2, name: "بلاگ", item: absoluteUrl("/blog") },
    ],
  };

  const blogLd = {
    "@context": "https://schema.org",
    "@type": "Blog",
    name: "بلاگ فونیکس وریفای",
    description: metadata.description,
    url: absoluteUrl("/blog"),
    blogPost: activePosts.map((post) => ({
      "@type": "BlogPosting",
      headline: post.title,
      description: post.excerpt,
      image: post.image ? absoluteUrl(post.image) : undefined,
      url: absoluteUrl(`/blog/${post.slug}`),
    })),
  };

  return (
    <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-10">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(breadcrumbLd) }} />
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(blogLd) }} />

      <div className="hero-anim-text relative mb-10 overflow-hidden rounded-3xl border border-[var(--hl-border)] bg-gradient-to-l from-[var(--hl-red)]/12 via-[var(--hl-orange)]/8 to-transparent px-8 py-12">
        <h1 className="text-3xl font-bold text-[var(--hl-ink)] sm:text-4xl">{content.sections.blogTitle}</h1>
        <p className="mt-3 max-w-xl text-sm leading-7 text-[var(--hl-ink-2)]">آخرین مقالات، آموزش‌ها و اخبار فونیکس وریفای.</p>
      </div>

      {activePosts.length === 0 ? (
        <p className="py-20 text-center text-[var(--hl-muted)]">هنوز مطلبی منتشر نشده است.</p>
      ) : (
        <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
          {activePosts.map((post, i) => (
            <Reveal key={post.id} delayMs={Math.min(i * 60, 240)}>
              <Link
                href={`/blog/${post.slug}`}
                className="block overflow-hidden rounded-2xl border border-[var(--hl-border)] hl-card transition duration-300 hover:-translate-y-1 hover:border-[var(--hl-red)]/40 hover:shadow-[0_20px_44px_-24px_rgba(239,35,60,0.3)]"
              >
                <img loading="lazy" decoding="async" src={post.image || undefined} alt={post.title} className="h-48 w-full object-cover" />
                <div className="p-6 text-right">
                  <span className="inline-block rounded-full bg-[var(--hl-red)]/10 px-3 py-1 font-archivo text-xs font-bold text-[var(--hl-red-text)]">{post.tag}</span>
                  <h3 className="mt-3 text-lg font-bold leading-8 text-[var(--hl-ink)]">{post.title}</h3>
                  {post.excerpt && <p className="mt-2 text-sm leading-7 text-[var(--hl-ink-2)]">{post.excerpt}</p>}
                  <p className="mt-4 font-archivo text-sm text-[var(--hl-ink-2)]">{post.date}</p>
                </div>
              </Link>
            </Reveal>
          ))}
        </div>
      )}
    </div>
  );
}
