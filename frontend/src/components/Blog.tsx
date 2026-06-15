import { blogPosts } from "@/data/home";
import SectionHeading from "./SectionHeading";

export default function Blog() {
  return (
    <section className="mx-auto mt-16 max-w-[1320px] px-5 sm:mt-24">
      <SectionHeading title="مطالب وبلاگ" />

      <div className="mt-12 grid grid-cols-1 gap-6 md:grid-cols-3">
        {blogPosts.map((post, i) => (
          <article
            key={i}
            className="overflow-hidden rounded-2xl border border-white/8 bg-[#15151f]/80 transition duration-300 hover:-translate-y-1 hover:border-white/20"
          >
            <img src={post.image} alt="" className="h-48 w-full object-cover" />
            <div dir="ltr" className="p-6 text-left">
              <p className="font-archivo text-sm text-white/65">{post.tag}</p>
              <h3 className="mt-3 font-display text-xl font-medium leading-8 text-lilac-gradient">
                {post.title}
              </h3>
              <p className="mt-4 font-archivo text-sm text-white/55">{post.date}</p>
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}
