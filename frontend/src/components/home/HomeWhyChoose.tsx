import Image from "next/image";

type Feature = { title: string; desc: string; icon: string };

const features: Feature[] = [
  { title: "تنوع بالا", desc: "صدها محصول متنوع", icon: "/figma/why-variety.png" },
  { title: "تحویل سریع", desc: "آنی و بدون معطلی", icon: "/figma/why-delivery.png" },
  { title: "پشتیبانی واقعی", desc: "۲۴/۷ در کنار شما", icon: "/figma/why-support.png" },
  { title: "امنیت بالا", desc: "حفظ اطلاعات شما", icon: "/figma/why-security.png" },
  { title: "خدمات معتبر", desc: "از برندهای کاملاً مطمئن", icon: "/figma/why-trusted.png" },
  { title: "قیمت منصفانه", desc: "بهترین قیمت بازار", icon: "/figma/why-price.png" },
];

export default function HomeWhyChoose() {
  return (
    <section id="about" className="mx-auto max-w-[1840px] px-16 py-16">
      <div className="mb-8 flex items-start gap-2">
        <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
        <div>
          <h2 className="text-[30px] font-black text-[var(--hl-ink)]">چرا فونیکس وریفای؟</h2>
          <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">دلایلی که ما را به انتخاب مطمئن شما تبدیل می‌کند</p>
        </div>
      </div>

      <div className="grid grid-cols-6 gap-5">
        {features.map((f) => (
          <div key={f.title} className="hl-card flex flex-col items-center gap-3 rounded-[20px] p-6 text-center">
            <Image src={f.icon} alt={f.title} width={96} height={96} className="h-24 w-24 object-contain" />
            <div>
              <h3 className="text-[20px] font-bold text-[var(--hl-ink)]">{f.title}</h3>
              <p className="mt-1 text-[17px] text-[var(--hl-muted)]">{f.desc}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
