export default function Background() {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute inset-0 -z-10 overflow-hidden bg-ink"
      style={{
        backgroundImage: `
          radial-gradient(640px 640px at 86% -4%, rgba(62, 58, 242, 0.38), transparent 70%),
          radial-gradient(560px 560px at 10% 1%, rgba(230, 0, 83, 0.28), transparent 70%),
          radial-gradient(820px 820px at 50% 5%, rgba(107, 10, 52, 0.22), transparent 72%),
          radial-gradient(1000px 620px at 50% 102%, rgba(109, 40, 217, 0.20), transparent 72%)
        `,
      }}
    >
      {/* Footer-style blurred color blobs scattered down the page so no dark band is left below the hero.
          Same technique as the footer halos (solid color + heavy blur), just brighter and spread out. */}
      <span className="absolute right-[6%] top-[15%] h-72 w-[26rem] rounded-full bg-[#e60053]/30 blur-[100px]" />
      <span className="absolute left-[3%] top-[28%] h-80 w-[26rem] rounded-full bg-[#3e3af2]/30 blur-[110px]" />
      <span className="absolute right-[8%] top-[42%] h-72 w-[28rem] rounded-full bg-[#6d28d9]/28 blur-[110px]" />
      <span className="absolute left-1/2 top-[54%] h-72 w-[30rem] -translate-x-1/2 rounded-full bg-[#e60053]/24 blur-[110px]" />
      <span className="absolute right-[6%] top-[68%] h-72 w-[26rem] rounded-full bg-[#3e3af2]/26 blur-[100px]" />
      <span className="absolute left-[6%] top-[82%] h-64 w-96 rounded-full bg-[#6d28d9]/24 blur-[100px]" />
    </div>
  );
}
