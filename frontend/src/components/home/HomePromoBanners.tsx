import Link from "next/link";

// Full pre-designed banner images (text baked in). Ordered right-to-left to
// match the reference: discount (right) · virtual number (middle) · gamers (left).
const banners = [
  { img: "/figma/banner-discount.png", href: "/products", alt: "تخفیف‌های شگفت‌انگیز — تا ۳۰٪ تخفیف ویژه" },
  { img: "/figma/banner-number.png", href: "/products", alt: "شماره مجازی برای همه کشورها" },
  { img: "/figma/banner-gamers.png", href: "/products", alt: "ویژه گیمرها — بهترین اکانت‌های بازی و استریم" },
];

export default function HomePromoBanners() {
  return (
    <section className="mx-auto grid max-w-[1840px] grid-cols-3 gap-6 px-16 py-16">
      {banners.map((b) => (
        <Link
          key={b.img}
          href={b.href}
          className="group block overflow-hidden rounded-[22px] transition duration-200 hover:-translate-y-1 hover:shadow-[0_20px_44px_-18px_rgba(20,20,20,0.28)]"
        >
          <img
            src={b.img}
            alt={b.alt}
            className="aspect-[16/9] w-full object-cover transition duration-300 group-hover:scale-[1.03]"
          />
        </Link>
      ))}
    </section>
  );
}
