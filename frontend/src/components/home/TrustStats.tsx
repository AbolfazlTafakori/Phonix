const stats = [
  { value: "+10,000", label: "سفارش موفق", icon: "/figma/stat-orders.png" },
  { value: "+5,000", label: "مشتری راضی", icon: "/figma/stat-customers.png" },
  { value: "99%", label: "رضایت مشتریان", icon: "/figma/stat-satisfaction.png" },
  { value: "پشتیبانی ۲۴/۷", label: "همیشه در کنار شما", icon: "/figma/stat-support.png" },
];

// Sizes are fluid (clamp) so the strip scales with the viewport instead of
// wrapping or overflowing at narrower desktop widths.
export default function TrustStats() {
  return (
    <section className="mx-auto max-w-[1600px] px-16">
      <div className="hl-card grid grid-cols-4 gap-4 rounded-[24px] px-6 py-7">
        {stats.map((s, i) => (
          <div
            key={s.label}
            className={`flex items-center justify-center gap-3 ${i > 0 ? "border-r border-[var(--hl-border)]" : ""}`}
          >
            <img
              src={s.icon}
              alt=""
              aria-hidden
              className="shrink-0 object-contain"
              style={{ width: "clamp(40px, 3.4vw, 56px)", height: "clamp(40px, 3.4vw, 56px)" }}
            />
            <div className="text-right">
              <div
                className="whitespace-nowrap font-black leading-none text-[var(--hl-ink)]"
                style={{ fontSize: "clamp(16px, 1.5vw, 26px)" }}
              >
                {s.value}
              </div>
              <div
                className="mt-2 whitespace-nowrap font-bold text-[var(--hl-ink-2)]"
                style={{ fontSize: "clamp(11px, 0.95vw, 15px)" }}
              >
                {s.label}
              </div>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
