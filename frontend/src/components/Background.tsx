export default function Background() {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute inset-0 -z-10 bg-ink"
      style={{
        backgroundImage: `
          radial-gradient(640px 640px at 86% -4%, rgba(62, 58, 242, 0.38), transparent 70%),
          radial-gradient(560px 560px at 10% 1%, rgba(230, 0, 83, 0.28), transparent 70%),
          radial-gradient(820px 820px at 50% 5%, rgba(107, 10, 52, 0.22), transparent 72%),
          radial-gradient(1000px 620px at 50% 102%, rgba(109, 40, 217, 0.22), transparent 72%),
          /* ── strong color halos spread down the whole page (same technique as the hero glow above) ── */
          radial-gradient(760px 760px at 0% 26%,  rgba(230, 0, 83, 0.34),  transparent 66%),
          radial-gradient(780px 780px at 100% 38%, rgba(62, 58, 242, 0.34), transparent 66%),
          radial-gradient(860px 860px at 50% 50%,  rgba(109, 40, 217, 0.30), transparent 68%),
          radial-gradient(760px 760px at 6% 64%,   rgba(230, 0, 83, 0.28),  transparent 68%),
          radial-gradient(780px 780px at 96% 76%,  rgba(62, 58, 242, 0.28), transparent 68%),
          radial-gradient(720px 720px at 30% 88%,  rgba(109, 40, 217, 0.24), transparent 70%)
        `,
      }}
    />
  );
}
