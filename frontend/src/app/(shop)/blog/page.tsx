import Link from "next/link";
import { getBlogPosts, getSiteContent } from "@/lib/content";

export const dynamic = "force-dynamic";
export const metadata = { title: "بلاگ | Phoenix Verify" };

export default async function BlogPage() {
  const [posts, content] = await Promise.all([getBlogPosts(), getSiteContent()]);

  return (
    <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-10">
      <div className="relative mb-10 overflow-hidden rounded-3xl border border-white/10 bg-gradient-to-l from-[#6d28d9]/20 via-[#2a1330]/10 to-transparent px-8 py-12">
        <h1 className="text-3xl font-bold text-white sm:text-4xl">{content.sections.blogTitle}</h1>
        <p className="mt-3 max-w-xl text-sm leading-7 text-white/70">آخرین مقالات، آموزش‌ها و اخبار فونیکس ورفای.</p>
      </div>

      {posts.length === 0 ? (
        <p className="py-20 text-center text-white/45">هنوز مطلبی منتشر نشده است.</p>
      ) : (
        <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
          {posts.map((post) => (
            <Link
              key={post.id}
              href={`/blog/${post.slug}`}
              className="block overflow-hidden rounded-2xl border border-white/8 bg-[#15151f]/80 transition duration-300 hover:-translate-y-1 hover:border-white/20"
            >
              <img src={post.image} alt={post.title} className="h-48 w-full object-cover" />
              <div className="p-6 text-right">
                <p className="font-archivo text-sm text-white/65">{post.tag}</p>
                <h3 className="mt-3 text-lg font-bold leading-8 text-lilac-gradient">{post.title}</h3>
                {post.excerpt && <p className="mt-2 text-sm leading-7 text-white/60">{post.excerpt}</p>}
                <p className="mt-4 font-archivo text-sm text-white/55">{post.date}</p>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
