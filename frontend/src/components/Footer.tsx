import { footerLinks } from "@/data/home";
import { TwitterIcon, TelegramIcon, InstagramIcon } from "./Icons";

const socials = [
  { label: "twitter", Icon: TwitterIcon, href: "#" },
  { label: "Telegram", Icon: TelegramIcon, href: "#" },
  { label: "instagram", Icon: InstagramIcon, href: "#" },
];

export default function Footer() {
  return (
    <footer className="mx-auto mb-8 mt-20 max-w-[1320px] px-5 sm:mt-28">
      <div className="relative overflow-hidden rounded-[28px] border border-white/10 bg-[#15151f]/90 px-6 py-10 sm:px-12">
        <div className="absolute -top-24 left-1/3 h-72 w-72 rounded-full bg-[#6d28d9]/20 blur-[120px]" />

        <div className="relative grid gap-10 sm:grid-cols-[0.7fr_2.3fr] sm:gap-8">
          {/* links — right, top */}
          <div className="sm:pr-6">
            <h3 className="mb-6 text-2xl font-bold text-lilac-gradient">لینک های مهم</h3>
            <ul className="space-y-4">
              {footerLinks.map((link) => (
                <li key={link.label}>
                  <a href={link.href} className="text-lg font-bold text-white/75 transition hover:text-white">
                    {link.label}
                  </a>
                </li>
              ))}
            </ul>
          </div>

          {/* brand — left */}
          <div>
            {/* logo — top-left */}
            <div dir="ltr" className="mb-6 flex items-center gap-3 sm:-ml-4 sm:-mt-4">
              <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-32 w-auto" />
              <span className="font-bigshot text-2xl leading-tight text-white">
                Phoenix
                <br />
                Verify
              </span>
            </div>
            {/* about — pushed fully right */}
            <div className="ml-auto max-w-[500px] sm:-mt-32">
              <h3 className="mb-3 text-xl font-bold text-white">فونیکس ورفای چیست؟</h3>
              <p className="text-justify text-[15px] font-medium leading-8 text-white/65">
                به بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب خوش آمدید! ما با افتخار
                بهترین و مطمئن‌ترین خدمات را برای شما فراهم می‌کنیم. ما متعهد به ارائه بهترین کیفیت و
                پشتیبانی به مشتریان خود هستیم. با ما، بهترین تجربه خرید آنلاین را داشته باشید.
              </p>
            </div>
          </div>
        </div>

        {/* socials — separate row, left-aligned, twitter first */}
        <div dir="ltr" className="relative mt-12 flex flex-wrap items-center gap-8">
          {socials.map(({ label, Icon, href }) => (
            <a key={label} href={href} className="flex items-center gap-2.5 text-white/80 transition hover:text-white">
              <Icon className="h-7 w-7" />
              <span className="font-turncoat text-xl">{label}</span>
            </a>
          ))}
        </div>

        {/* copyright — lower & bold */}
        <div className="relative mt-8 border-t border-white/10 pt-8 text-center">
          <p className="text-base font-extrabold text-white/80">
            تمام حقوق برای فونیکس ورفای محفوظ است
          </p>
        </div>
      </div>
    </footer>
  );
}
