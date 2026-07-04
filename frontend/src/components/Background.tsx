export default function Background() {
  return (
    <div
      aria-hidden
      // Fixed backdrop so the color halos stay behind everything at every scroll position. Opacities are
      // high on purpose: the page/body sits on an opaque near-black (#111 / rgb(17,17,27)), so faint halos
      // just get swallowed by it — the color has to be strong enough to actually read over that dark base.
      className="pointer-events-none fixed inset-0 -z-10 bg-ink"
      style={{
        backgroundImage: `
          radial-gradient(620px 620px at 10% 4%,  rgba(230, 0, 83, 0.55),  transparent 66%),
          radial-gradient(680px 680px at 90% 10%, rgba(62, 58, 242, 0.55), transparent 66%),
          radial-gradient(760px 760px at 50% 42%, rgba(109, 40, 217, 0.42), transparent 68%),
          radial-gradient(660px 660px at 4% 70%,  rgba(62, 58, 242, 0.50), transparent 68%),
          radial-gradient(700px 700px at 96% 80%, rgba(230, 0, 83, 0.50),  transparent 68%),
          radial-gradient(780px 780px at 50% 102%, rgba(109, 40, 217, 0.44), transparent 70%)
        `,
      }}
    />
  );
}
