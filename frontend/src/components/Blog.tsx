import { getBlogPosts, getSiteContent } from "@/lib/content";
import BlogShowcase from "./BlogShowcase";

export default async function Blog() {
  const [posts, content] = await Promise.all([getBlogPosts(), getSiteContent()]);

  // Show the admin-selected posts; if none are flagged, fall back to the most recent so the section is
  // never empty. Capped at 5 (one featured + four beside).
  const featured = posts.filter((p) => p.featuredOnHome);
  const shown = (featured.length > 0 ? featured : posts).slice(0, 5);
  if (shown.length === 0) return null;

  return (
    <BlogShowcase
      posts={shown}
      autoplaySeconds={content.blogAutoplaySeconds ?? 5}
      title={content.sections.blogTitle}
    />
  );
}
