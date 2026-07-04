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
      {/* Footer-style blurred color blobs, scattered down the dark middle/lower stretch so the page keeps
          its color life below the hero (radial gradients alone read too faint over such a tall page). */}
      <span className="absolute right-[6%] top-[26%] h-72 w-96 rounded-full bg-[#e60053]/22 blur-[100px]" />
      <span className="absolute left-[4%] top-[40%] h-80 w-[26rem] rounded-full bg-[#3e3af2]/22 blur-[110px]" />
      <span className="absolute left-1/2 top-[52%] h-72 w-[28rem] -translate-x-1/2 rounded-full bg-[#6d28d9]/20 blur-[110px]" />
      <span className="absolute right-[10%] top-[66%] h-72 w-96 rounded-full bg-[#3e3af2]/18 blur-[100px]" />
      <span className="absolute left-[8%] top-[80%] h-64 w-96 rounded-full bg-[#e60053]/16 blur-[100px]" />
    </div>
  );
}
