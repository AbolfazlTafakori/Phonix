import { categories } from "@/data/home";
import SectionHeading from "./SectionHeading";

export default function Categories() {
  return (
    <section className="mx-auto mt-16 max-w-[1320px] px-5 sm:mt-24">
      <SectionHeading title="لیست محصولات" />

      <div dir="ltr" className="mt-12 grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-4">
        {categories.map((cat) => (
          <a
            key={cat.title}
            href={cat.href}
            className="group flex flex-col items-center rounded-2xl border border-white/8 bg-[#15151f]/80 px-4 pb-7 pt-8 text-center transition duration-300 hover:-translate-y-1 hover:border-[#e60053]/40 hover:bg-[#1b1b2a]/90 hover:shadow-[0_24px_60px_-24px_rgba(230,0,83,0.5)]"
          >
            <div className="flex h-40 items-center justify-center">
              <span className={`inline-flex ${cat.iconClass ?? ""}`}>
                <img
                  src={cat.icon}
                  alt={cat.title}
                  className="max-h-40 w-auto object-contain transition duration-300 group-hover:scale-105"
                />
              </span>
            </div>
            <h3 className="mt-5 flex min-h-[4.5rem] items-center justify-center text-2xl font-bold leading-9 text-white sm:text-3xl">
              {cat.title}
            </h3>
          </a>
        ))}
      </div>
    </section>
  );
}
