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
          /* ── fill the dark mid/lower stretch with color halos ── */
          radial-gradient(720px 720px at 0% 30%, rgba(230, 0, 83, 0.26), transparent 68%),
          radial-gradient(720px 720px at 100% 55%, rgba(62, 58, 242, 0.26), transparent 68%),
          radial-gradient(820px 820px at 50% 46%, rgba(109, 40, 217, 0.20), transparent 70%),
          radial-gradient(700px 700px at 24% 76%, rgba(14, 165, 181, 0.16), transparent 70%)
        `,
      }}
    />
  );
}
