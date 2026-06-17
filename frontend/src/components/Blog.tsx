import Link from "next/link";
import { getBlogPosts, getSiteContent } from "@/lib/content";
import SectionHeading from "./SectionHeading";

export default async function Blog() {
  const [posts, content] = await Promise.all([getBlogPosts(), getSiteContent()]);

  return (
    <section className="mx-auto mt-16 max-w-[1320px] px-5 sm:mt-24">
      <SectionHeading title={content.sections.blogTitle} />

      <div className="mt-12 grid grid-cols-1 gap-6 md:grid-cols-3">
        {posts.map((post) => (
          <Link
            key={post.id}
            href={`/blog/${post.slug}`}
            className="block overflow-hidden rounded-2xl border border-white/8 bg-[#15151f]/80 transition duration-300 hover:-translate-y-1 hover:border-white/20"
          >
            <img src={post.image} alt="" className="h-48 w-full object-cover" />
            <div className="p-6 text-right">
              <p className="font-archivo text-sm text-white/65">{post.tag}</p>
              <h3 className="mt-3 text-lg font-bold leading-8 text-lilac-gradient">{post.title}</h3>
              {post.excerpt && <p className="mt-2 text-sm leading-7 text-white/60">{post.excerpt}</p>}
              <p className="mt-4 font-archivo text-sm text-white/55">{post.date}</p>
            </div>
          </Link>
        ))}
      </div>
    </section>
  );
}
