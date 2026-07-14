import { getSiteContent } from "@/lib/content";
import { TwitterIcon, TelegramIcon, InstagramIcon } from "./Icons";

const socialIcons: Record<string, (props: { className?: string }) => React.ReactElement> = {
  twitter: TwitterIcon,
  telegram: TelegramIcon,
  instagram: InstagramIcon,
};

function SealIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 3 5 6v6c0 4.4 3 7.2 7 8.5 4-1.3 7-4.1 7-8.5V6l-7-3Z" />
      <path d="m9 12 2 2 4-4" />
    </svg>
  );
}

export default async function Footer() {
  const { brand, footer } = await getSiteContent();

  // Columns drive the premium layout; fall back to the legacy single links list so a footer saved before
  // this redesign still renders its links.
  const columns =
    footer.columns?.length
      ? footer.columns
      : footer.links?.length
        ? [{ title: footer.linksTitle, links: footer.links }]
        : [];

  const c = footer.contact;
  const contactItems = [
    c?.phone && { label: "پشتیبانی", value: c.phone, ltr: true },
    c?.email && { label: "ایمیل", value: c.email, ltr: true },
    c?.hours && { label: "ساعات پاسخ‌گویی", value: c.hours, ltr: false },
    c?.address && { label: "آدرس", value: c.address, ltr: false },
  ].filter(Boolean) as { label: string; value: string; ltr: boolean }[];

  // A seal only appears when the admin has it enabled — toggling it off removes it from the footer.
  const seals = (footer.trustSeals ?? []).filter((s) => s.enabled && s.title.trim());

  return (
    <footer className="mx-auto mb-8 mt-20 max-w-[1320px] px-5 sm:mt-28">
      <div className="relative overflow-hidden rounded-[28px] border border-white/10 bg-[#0c0c14] px-6 pb-6 sm:px-12">
        {/* top accent hairline */}
        <span aria-hidden className="absolute inset-x-0 top-0 h-px bg-gradient-to-l from-transparent via-[#e60053]/55 to-[#3e3af2]/40" />
        {/* ambient glow halos */}
        <div aria-hidden className="pointer-events-none absolute -top-16 right-[10%] h-56 w-80 rounded-full bg-[#e60053]/20 blur-[90px]" />
        <div aria-hidden className="pointer-events-none absolute -top-12 left-[8%] h-52 w-72 rounded-full bg-[#3e3af2]/20 blur-[90px]" />
        <div aria-hidden className="pointer-events-none absolute -bottom-24 left-1/3 h-48 w-[26rem] rounded-full bg-[#6d28d9]/15 blur-[90px]" />

        <div className="relative pt-12">
          <div className="grid gap-8 sm:gap-10 md:grid-cols-[1.7fr_1fr_1fr_1.2fr]">
            {/* brand — uses the real logo + brand font */}
            <div>
              <div className="flex items-center gap-0">
                <img loading="lazy" decoding="async" src={brand.logo} alt={brand.siteName} className="h-[clamp(3rem,7vw,4.5rem)] w-auto" />
                <span className="-ml-[clamp(0.25rem,1.2vw,0.7rem)] font-bigshot text-[clamp(0.95rem,2vw,1.3rem)] leading-tight text-white">
                  {brand.logoLine1}
                  <br />
                  {brand.logoLine2}
                </span>
              </div>
              {footer.aboutText && (
                <p className="mt-4 max-w-[34ch] text-[13px] leading-8 text-white/50">{footer.aboutText}</p>
              )}
              {footer.socials.length > 0 && (
                <div className="mt-5 flex gap-4">
                  {footer.socials.map((s) => {
                    const Icon = socialIcons[s.icon] ?? TwitterIcon;
                    return (
                      <a key={s.label} href={s.href} aria-label={s.label} className="text-white/45 transition hover:-translate-y-0.5 hover:text-white">
                        <Icon className="h-[22px] w-[22px]" />
                      </a>
                    );
                  })}
                </div>
              )}
            </div>

            {/* link columns */}
            {columns.map((col, i) => (
              <div key={i}>
                <h3 className="mb-4 text-[13px] font-bold text-white/90">{col.title}</h3>
                <ul className="space-y-3">
                  {col.links.map((l) => (
                    <li key={l.label}>
                      <a href={l.href} className="text-[13px] text-white/50 transition hover:text-white">
                        {l.label}
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            ))}

            {/* contact */}
            {contactItems.length > 0 && (
              <div>
                <h3 className="mb-4 text-[13px] font-bold text-white/90">تماس</h3>
                <div className="space-y-3.5">
                  {contactItems.map((item) => (
                    <div key={item.label}>
                      <span className="block text-[10.5px] tracking-wide text-white/35">{item.label}</span>
                      <span className="mt-0.5 block text-[13.5px] font-medium text-white/80" dir={item.ltr ? "ltr" : undefined}>
                        {item.value}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>

          {/* bottom bar: copyright + enabled trust seals */}
          <div className="mt-9 flex flex-wrap-reverse items-center justify-between gap-4 border-t border-white/8 pt-6">
            <p className="text-xs text-white/40">{footer.copyright}</p>
            {seals.length > 0 && (
              <div className="flex flex-wrap gap-2.5">
                {seals.map((s, i) => (
                  <a
                    key={i}
                    href={s.link || "#"}
                    className="flex items-center gap-2 rounded-xl border border-white/[0.08] bg-white/[0.03] px-3.5 py-2 transition hover:border-white/20 hover:bg-white/[0.05]"
                  >
                    <SealIcon className={`h-[18px] w-[18px] ${i % 2 === 0 ? "text-emerald-400" : "text-[#6f93ff]"}`} />
                    <span className="leading-tight">
                      <span className="block text-[12px] font-medium text-white">{s.title}</span>
                      {s.subtitle && <span className="block text-[11px] text-white/55" dir="ltr">{s.subtitle}</span>}
                    </span>
                  </a>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </footer>
  );
}
