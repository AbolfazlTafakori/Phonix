import { getAdvancedSettings, getSiteContent } from "@/lib/content";

export const dynamic = "force-dynamic";
export const metadata = { title: "در حال به‌روزرسانی" };

export default async function MaintenancePage() {
  const [adv, content] = await Promise.all([getAdvancedSettings(), getSiteContent()]);

  return (
    <main className="relative grid min-h-screen place-items-center overflow-hidden px-6 text-center" style={{ background: "var(--chat-surface)" }}>
      <div className="pointer-events-none absolute -top-32 left-1/4 h-96 w-96 rounded-full bg-[#ef233c]/20 blur-[130px]" />
      <div className="pointer-events-none absolute -bottom-32 right-1/4 h-96 w-96 rounded-full bg-[#6d28d9]/20 blur-[130px]" />

      <div className="relative z-10 w-full max-w-xl">
        <div className="mb-8 flex items-center justify-center gap-2.5">
          <img src={content.brand.logo} alt={content.brand.siteName} className="h-12 w-auto" />
          <span className="font-bigshot text-lg leading-[1.05]" style={{ color: "var(--chat-ink)" }}>
            {content.brand.logoLine1}
            <br />
            {content.brand.logoLine2}
          </span>
        </div>

        <div className="mx-auto mb-8 grid h-24 w-24 place-items-center rounded-3xl" style={{ border: "1px solid var(--chat-border)", background: "var(--chat-surface-2)" }}>
          <svg className="h-12 w-12 animate-spin text-[#ef233c]" style={{ animationDuration: "4s" }} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-2.9 1.2V21a2 2 0 1 1-4 0v-.1A1.7 1.7 0 0 0 7 19.2l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0-1.2-2.9H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.8 7l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H10a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 2.9 1.2l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V10a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" />
          </svg>
        </div>

        <h1 className="text-3xl font-bold sm:text-4xl" style={{ color: "var(--chat-ink)" }}>{adv.maintenanceTitle}</h1>
        <p className="mx-auto mt-5 max-w-md text-sm leading-8" style={{ color: "var(--chat-muted)" }}>{adv.maintenanceMessage}</p>

        <div className="mt-8 inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-medium" style={{ border: "1px solid var(--chat-border)", background: "var(--chat-surface-2)", color: "var(--chat-ink-2)" }}>
          <span className="h-2 w-2 animate-pulse rounded-full bg-amber-400" />
          به‌زودی برمی‌گردیم
        </div>

        {content.footer.socials.length > 0 && (
          <div className="mt-10 flex items-center justify-center gap-6" style={{ color: "var(--chat-muted)" }}>
            {content.footer.socials.map((s) => (
              <a key={s.label} href={s.href} className="text-sm font-medium transition hover:brightness-125">
                {s.label}
              </a>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}
