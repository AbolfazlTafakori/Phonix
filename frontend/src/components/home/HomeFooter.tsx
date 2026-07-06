import Link from "next/link";
import { TelegramIcon, InstagramIcon, TwitterIcon } from "../Icons";

const YoutubeIcon = ({ className }: { className?: string }) => (
  <svg viewBox="0 0 24 24" className={className} fill="currentColor">
    <path d="M23 12s0-3.2-.4-4.7a2.5 2.5 0 00-1.7-1.7C19.4 5.2 12 5.2 12 5.2s-7.4 0-8.9.4A2.5 2.5 0 001.4 7.3C1 8.8 1 12 1 12s0 3.2.4 4.7a2.5 2.5 0 001.7 1.7c1.5.4 8.9.4 8.9.4s7.4 0 8.9-.4a2.5 2.5 0 001.7-1.7C23 15.2 23 12 23 12zM9.8 15.3V8.7l5.7 3.3z" />
  </svg>
);

const quick = [
  { label: "درباره ما", href: "#about" },
  { label: "تماس با ما", href: "#" },
  { label: "وبلاگ", href: "/blog" },
  { label: "قوانین و مقررات", href: "#" },
];
const access = [
  { label: "محصولات", href: "/products" },
  { label: "شماره مجازی", href: "/products" },
  { label: "گیفت کارت", href: "/products" },
  { label: "وبلاگ", href: "/blog" },
];

export default function HomeFooter({ brand }: { brand: { siteName: string; logo: string; logoLine1: string; logoLine2: string } }) {
  return (
    <footer className="border-t border-[var(--hl-border)] bg-[#fafafa]">
      <div className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16 py-14">
        {/* Columns ordered right-to-left as in the design: trust · support · access · quick · about */}
        <div className="grid grid-cols-2 gap-8 sm:grid-cols-3 lg:grid-cols-5">
          {/* trust (right) */}
          <div>
            <h3 className="mb-4 text-[17px] font-bold text-[var(--hl-ink)]">نمادها و اعتمادها</h3>
            <div className="flex flex-wrap items-center gap-3">
              {/* placeholders — trust badge images will be set later from the admin panel */}
              {[0, 1, 2].map((i) => (
                <div key={i} className="h-14 w-14 rounded-xl border border-[var(--hl-border)] bg-white sm:h-16 sm:w-16" />
              ))}
            </div>
            <ul className="mt-5 flex flex-col gap-3 text-[15px] text-[var(--hl-ink-2)]">
              {["دارای نماد اعتماد الکترونیکی", "درگاه پرداخت امن و مطمئن"].map((t) => (
                <li key={t} className="flex items-center gap-2">
                  <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0 text-[var(--hl-ink-2)]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M9 12l2 2 4-4M12 3l8 4v5c0 5-3.5 8-8 10-4.5-2-8-5-8-10V7l8-4z" />
                  </svg>
                  {t}
                </li>
              ))}
            </ul>
          </div>

          {/* support */}
          <div>
            <h3 className="mb-4 text-[17px] font-bold text-[var(--hl-ink)]">پشتیبانی</h3>
            <ul className="flex flex-col gap-3 text-[15px] text-[var(--hl-ink-2)]">
              <li className="flex items-center gap-2">
                <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M22 16.9v3a2 2 0 01-2.2 2 19.8 19.8 0 01-8.6-3 19.5 19.5 0 01-6-6 19.8 19.8 0 01-3-8.6A2 2 0 014.1 2h3a2 2 0 012 1.7c.1.9.4 1.8.7 2.6a2 2 0 01-.5 2.1L8.1 9.9a16 16 0 006 6l1.5-1.2a2 2 0 012.1-.4c.8.3 1.7.6 2.6.7a2 2 0 011.7 2z" /></svg>
                <span dir="ltr">۰۲۱-۹۱۰۱۳۵۴۵</span>
              </li>
              <li className="flex min-w-0 items-center gap-2">
                <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M4 4h16v16H4zM4 6l8 6 8-6" /></svg>
                <span dir="ltr" className="min-w-0 break-all">support@phoenixverify.com</span>
              </li>
              <li className="flex items-center gap-2">
                <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>
                همه‌روزه ۹ صبح تا ۱۲ شب
              </li>
            </ul>
            <Link href="#" className="mt-4 inline-flex max-w-full items-center gap-2 truncate rounded-xl border border-[#2563eb]/30 bg-[#2563eb]/5 px-4 py-2 text-[14px] font-bold text-[#2563eb] transition hover:bg-[#2563eb]/10">
              <TelegramIcon className="h-4 w-4 shrink-0" />
              @PhoenixVerifySupport
            </Link>
          </div>

          {/* access */}
          <div>
            <h3 className="mb-4 text-[17px] font-bold text-[var(--hl-ink)]">دسترسی سریع</h3>
            <ul className="flex flex-col gap-3">
              {access.map((l) => (
                <li key={l.label}><Link href={l.href} className="text-[15px] text-[var(--hl-ink-2)] transition hover:text-[var(--hl-red)]">{l.label}</Link></li>
              ))}
            </ul>
          </div>

          {/* quick links */}
          <div>
            <h3 className="mb-4 text-[17px] font-bold text-[var(--hl-ink)]">لینک‌های مفید</h3>
            <ul className="flex flex-col gap-3">
              {quick.map((l) => (
                <li key={l.label}><Link href={l.href} className="text-[15px] text-[var(--hl-ink-2)] transition hover:text-[var(--hl-red)]">{l.label}</Link></li>
              ))}
            </ul>
          </div>

          {/* about (left) */}
          <div className="col-span-1">
            <h3 className="mb-4 text-[17px] font-bold text-[var(--hl-ink)]">درباره ما</h3>
            <p className="text-[15px] leading-[1.9] text-[var(--hl-ink-2)]">
              فینیکس وریفای، مرجع معتبر ارائه سرویس‌های دیجیتال، شماره مجازی، گیفت کارت و سایر خدمات آنلاین با پشتیبانی حرفه‌ای.
            </p>
            <div className="mt-5 flex items-center gap-2.5">
              {[
                { I: TelegramIcon, href: "#" },
                { I: InstagramIcon, href: "#" },
                { I: TwitterIcon, href: "#" },
                { I: YoutubeIcon, href: "#" },
              ].map(({ I, href }, i) => (
                <Link key={i} href={href} className="grid h-10 w-10 place-items-center rounded-xl border border-[var(--hl-border)] bg-white text-[var(--hl-ink-2)] transition hover:border-[var(--hl-red)]/40 hover:text-[var(--hl-red)]">
                  <I className="h-5 w-5" />
                </Link>
              ))}
            </div>
          </div>
        </div>

        <div className="mt-12 flex flex-row flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-[var(--hl-border)] pt-6 text-[15px] text-[var(--hl-muted)]">
          <span className="flex items-center gap-2">
            <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 21s-7-4.35-9.5-8.5C1 9.5 2.5 6 6 6c2 0 3.2 1.2 4 2.3C10.8 7.2 12 6 14 6c3.5 0 5 3.5 3.5 6.5C19 16.65 12 21 12 21z" />
            </svg>
            تجربه‌ای مطمئن در خرید محصولات دیجیتال
          </span>
          <span className="flex items-center gap-2">
            <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 3l8 4v5c0 5-3.5 8-8 10-4.5-2-8-5-8-10V7l8-4z" />
            </svg>
            کیفیت، سرعت، امنیت
          </span>
          <span>تمامی حقوق این سایت متعلق به <span className="font-bold text-[var(--hl-red)]">Phoenix Verify</span> است.</span>
        </div>
      </div>
    </footer>
  );
}
