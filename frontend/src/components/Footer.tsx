import { getSiteContent } from "@/lib/content";
import { TwitterIcon, TelegramIcon, InstagramIcon } from "./Icons";

const socialIcons: Record<string, (props: { className?: string }) => React.ReactElement> = {
  twitter: TwitterIcon,
  telegram: TelegramIcon,
  instagram: InstagramIcon,
};

export default async function Footer() {
  const { brand, footer } = await getSiteContent();

  return (
    <footer className="mx-auto mb-8 mt-20 max-w-[1320px] px-5 sm:mt-28">
      <div className="relative overflow-hidden rounded-[28px] border border-white/10 bg-[#15151f]/90 px-6 py-10 sm:px-12">
        <div className="absolute -top-24 left-1/3 h-72 w-72 rounded-full bg-[#6d28d9]/20 blur-[120px]" />

        <div dir="ltr" className="relative flex flex-wrap items-start gap-x-8 gap-y-10">
          {/* logo — left corner (smaller on mobile) */}
          <div className="order-1 flex shrink-0 items-center gap-0">
            <img src={brand.logo} alt={brand.siteName} className="h-[clamp(3.5rem,9vw,7rem)] w-auto" />
            <span className="-ml-[clamp(0.25rem,1.7vw,1rem)] font-bigshot text-[clamp(1rem,2.4vw,1.6rem)] leading-tight text-white">
              {brand.logoLine1}
              <br />
              {brand.logoLine2}
            </span>
          </div>

          {/* links — mobile: top-right beside logo · desktop: far right */}
          <div dir="rtl" className="order-2 ml-auto sm:order-3 sm:ml-0">
            <h3 className="mb-6 text-2xl font-bold text-lilac-gradient">{footer.linksTitle}</h3>
            <ul className="space-y-4">
              {footer.links.map((link) => (
                <li key={link.label}>
                  <a href={link.href} className="text-lg font-bold text-white/75 transition hover:text-white">
                    {link.label}
                  </a>
                </li>
              ))}
            </ul>
          </div>

          {/* about — mobile: full-width below · desktop: middle, pushed right */}
          <div dir="rtl" className="order-3 basis-full sm:order-2 sm:ml-auto sm:max-w-[460px] sm:basis-auto">
            <h3 className="mb-3 text-xl font-bold text-white">{footer.aboutTitle}</h3>
            <p className="text-[15px] font-medium leading-8 text-white/65 [text-wrap:balance]">{footer.aboutText}</p>
          </div>
        </div>

        {/* socials — separate row, left-aligned */}
        <div dir="ltr" className="relative mt-12 flex flex-wrap items-center gap-8">
          {footer.socials.map((s) => {
            const Icon = socialIcons[s.icon] ?? TwitterIcon;
            return (
              <a key={s.label} href={s.href} className="flex items-center gap-2.5 text-white/80 transition hover:text-white">
                <Icon className="h-7 w-7" />
                <span className="font-turncoat text-xl">{s.label}</span>
              </a>
            );
          })}
        </div>

        {/* copyright — lower & bold */}
        <div className="relative mt-8 border-t border-white/10 pt-8 text-center">
          <p className="text-base font-extrabold text-white/80">{footer.copyright}</p>
        </div>
      </div>
    </footer>
  );
}
