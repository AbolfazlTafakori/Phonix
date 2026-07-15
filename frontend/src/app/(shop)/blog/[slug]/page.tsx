import Link from "next/link";
import { notFound } from "next/navigation";
import { getBlogPosts } from "@/lib/content";
import { absoluteUrl, jsonLdScript, plainExcerpt } from "@/lib/seo";
import RichText from "@/components/RichText";

export const dynamic = "force-dynamic";

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const post = (await getBlogPosts()).find((p) => p.slug === slug);
  if (!post) return { title: "بلاگ" };
  const description = plainExcerpt(post.excerpt || post.content);
  const canonical = `/blog/${post.slug}`;
  return {
    title: `${post.title} | بلاگ`,
    description,
    alternates: { canonical },
    openGraph: {
      type: "article",
      title: `${post.title} | Phoenix Verify`,
      description,
      url: canonical,
      images: post.image ? [{ url: post.image, alt: post.title }] : undefined,
    },
    twitter: {
      card: "summary_large_image",
      title: `${post.title} | Phoenix Verify`,
      description,
      images: post.image ? [post.image] : undefined,
    },
  };
}

export default async function ArticlePage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const posts = await getBlogPosts();
  const post = posts.find((p) => p.slug === slug);
  if (!post) notFound();

  const articleLd = {
    "@context": "https://schema.org",
    "@type": "BlogPosting",
    headline: post.title,
    description: plainExcerpt(post.excerpt || post.content),
    image: post.image ? absoluteUrl(post.image) : undefined,
    url: absoluteUrl(`/blog/${post.slug}`),
    inLanguage: "fa",
    // post.date is a free-form Persian label; only emit it when it's already ISO.
    ...(/^\d{4}-\d{2}-\d{2}/.test(post.date) && { datePublished: post.date }),
    author: { "@type": "Organization", name: "Phoenix Verify" },
    publisher: {
      "@type": "Organization",
      name: "Phoenix Verify",
      logo: { "@type": "ImageObject", url: absoluteUrl("/figma/logo-phoenix.png") },
    },
    mainEntityOfPage: absoluteUrl(`/blog/${post.slug}`),
  };

  return (
    <article className="mx-auto max-w-[820px] px-5 pb-20 pt-8">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(articleLd) }} />
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
        <span className="rounded-full bg-[var(--hl-red)]/10 px-3 py-1 font-bold text-[var(--hl-red)]">{post.tag}</span>
        <span>{post.date}</span>
      </div>

      <h1 className="mt-4 text-3xl font-bold leading-snug text-[var(--hl-ink)] sm:text-4xl">{post.title}</h1>

      {/* Markdown body: ## headings, lists, links and inline images all render, so long-form
          SEO articles stay readable instead of one wall of text. */}
      <div className="mt-6">
        <RichText content={post.content} className="text-[15px] leading-9" />
      </div>

      <div className="mt-12 border-t border-[var(--hl-border)] pt-6">
        <Link href="/blog" className="text-sm font-bold text-[var(--hl-red)] hover:underline">→ بازگشت به بلاگ</Link>
      </div>
    </article>
  );
}
