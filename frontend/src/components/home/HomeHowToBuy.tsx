import Image from "next/image";

type Step = { n: number; title: string; desc: string; icon: string };

// Ordered as shown right-to-left in the reference (step 1 on the right).
const steps: Step[] = [
  { n: 1, title: "انتخاب محصول", desc: "محصول مورد نظر خود را انتخاب کنید", icon: "/figma/step-cart.png" },
  { n: 2, title: "پرداخت آنلاین", desc: "پرداخت امن از طریق درگاه‌های معتبر", icon: "/figma/step-card.png" },
  { n: 3, title: "دریافت محصول", desc: "بعد از پرداخت، بلافاصله فایل دریافت کنید", icon: "/figma/step-download.png" },
  { n: 4, title: "استفاده و لذت خرید", desc: "از خدمات ما استفاده کنید و لذت ببرید", icon: "/figma/step-check.png" },
];

export default function HomeHowToBuy() {
  return (
    <section className="mx-auto max-w-[1600px] px-16 py-16">
      <div className="mb-8 flex items-start gap-2">
        <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
        <div>
          <h2 className="text-[30px] font-black text-[var(--hl-ink)]">چطور خرید کنیم؟</h2>
          <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">فقط در چهار قدم ساده خرید خود را کامل کنید</p>
        </div>
      </div>

      <div className="grid grid-cols-4 gap-6">
        {steps.map((s, i) => (
          <div key={s.n} className="relative">
            {/* connector to the next card (to the left in RTL) */}
            {i < steps.length - 1 && (
              <span aria-hidden className="absolute left-[-24px] top-1/2 hidden h-px w-6 -translate-y-1/2 border-t-2 border-dashed border-[var(--hl-border)] xl:block" />
            )}
            <div className="hl-card relative flex flex-row items-center gap-4 rounded-[20px] p-6">
              <Image src={s.icon} alt={s.title} width={112} height={112} className="h-28 w-28 shrink-0 object-contain" />
              <div>
                <h3 className="text-[19px] font-bold text-[var(--hl-ink)]">{s.title}</h3>
                <p className="mt-1.5 text-[15px] leading-[1.8] text-[var(--hl-muted)]">{s.desc}</p>
              </div>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
