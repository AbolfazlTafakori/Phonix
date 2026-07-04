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
          radial-gradient(560px 560px at 102% 42%, rgba(24, 24, 214, 0.20), transparent 70%),
          radial-gradient(620px 620px at -6% 60%, rgba(230, 0, 83, 0.14), transparent 70%),
          radial-gradient(1000px 620px at 50% 102%, rgba(109, 40, 217, 0.20), transparent 72%),
          /* ── fill the dark mid/lower stretch with soft, subtle color halos ── */
          radial-gradient(680px 680px at 2% 32%, rgba(230, 0, 83, 0.12), transparent 70%),
          radial-gradient(680px 680px at 100% 56%, rgba(62, 58, 242, 0.12), transparent 70%),
          radial-gradient(760px 760px at 50% 46%, rgba(109, 40, 217, 0.10), transparent 72%),
          radial-gradient(640px 640px at 26% 74%, rgba(14, 165, 181, 0.08), transparent 72%)
        `,
      }}
    />
  );
}
