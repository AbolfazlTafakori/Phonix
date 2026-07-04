export default function Background() {
  return (
    <div
      aria-hidden
      // Fixed so the color halos are a constant backdrop behind everything: they stay visible on every
      // scroll position (products, footer, etc. all render on top of them) instead of being spread thin
      // across the full page height and mostly scrolled past.
      className="pointer-events-none fixed inset-0 -z-10 bg-ink"
      style={{
        backgroundImage: `
          radial-gradient(620px 620px at 12% 6%,  rgba(230, 0, 83, 0.32),  transparent 68%),
          radial-gradient(660px 660px at 88% 12%, rgba(62, 58, 242, 0.32), transparent 68%),
          radial-gradient(720px 720px at 50% 40%, rgba(109, 40, 217, 0.26), transparent 70%),
          radial-gradient(640px 640px at 6% 72%,  rgba(62, 58, 242, 0.28), transparent 70%),
          radial-gradient(680px 680px at 94% 82%, rgba(230, 0, 83, 0.28),  transparent 70%),
          radial-gradient(760px 760px at 50% 100%, rgba(109, 40, 217, 0.24), transparent 72%)
        `,
      }}
    />
  );
}
