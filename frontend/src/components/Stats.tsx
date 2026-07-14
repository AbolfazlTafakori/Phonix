import { getSiteContent } from "@/lib/content";
import Img from "@/components/ui/Img";

export default async function Stats() {
  const { stats } = await getSiteContent();

  return (
    <section className="mx-auto mt-10 max-w-[1100px] px-5">
      <div className="grid grid-cols-1 overflow-hidden rounded-[28px] border border-white/8 bg-gradient-to-b from-[#1c1c2b]/80 to-[#141420]/80 shadow-[0_24px_60px_-30px_rgba(0,0,0,0.8)] sm:grid-cols-3">
        {stats.map((s, i) => (
          <div
            key={s.label}
            className="relative flex flex-col items-center justify-center gap-3 border-t border-white/8 px-6 py-8 first:border-t-0 sm:border-t-0"
          >
            {/* crimson divider between columns */}
            {i > 0 && (
              <span
                aria-hidden
                className="absolute right-0 top-1/2 hidden h-16 w-[2px] -translate-y-1/2 bg-gradient-to-b from-transparent via-[#e60053] to-transparent sm:block"
              />
            )}

            <div className="flex h-14 items-center justify-center">
              {s.icon ? (
                <Img src={s.icon} alt="" className="h-14 w-14 object-contain" sizes="112px" />
              ) : (
                <span className="font-display text-[40px] font-bold italic leading-none text-white">{s.value}</span>
              )}
            </div>

            <span className="text-lg font-bold text-white sm:text-xl">{s.label}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
