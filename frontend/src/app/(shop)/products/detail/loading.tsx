const shimmer = "animate-pulse rounded-xl bg-[var(--hl-border)]/50";

export default function Loading() {
  return (
    <div className="mx-auto max-w-[1240px] px-5 pb-16 pt-6 md:px-6">
      <div className={`h-4 w-72 max-w-full ${shimmer}`} />
      <div className="mt-6 grid items-start gap-6 lg:grid-cols-[420px_1fr_320px]">
        <div className={`aspect-square w-full rounded-[22px] ${shimmer}`} />
        <div className="space-y-4">
          <div className={`h-8 w-3/4 ${shimmer}`} />
          <div className={`h-4 w-full ${shimmer}`} />
          <div className={`h-4 w-2/3 ${shimmer}`} />
          <div className="grid grid-cols-3 gap-2.5 pt-2">
            <div className={`h-20 ${shimmer}`} />
            <div className={`h-20 ${shimmer}`} />
            <div className={`h-20 ${shimmer}`} />
          </div>
          <div className={`h-12 w-full ${shimmer}`} />
        </div>
        <div className="space-y-3 rounded-[22px] border p-5" style={{ borderColor: "var(--hl-border)" }}>
          <div className={`h-8 w-1/2 ${shimmer}`} />
          <div className="grid grid-cols-2 gap-2">
            <div className={`h-11 ${shimmer}`} />
            <div className={`h-11 ${shimmer}`} />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div className={`h-20 ${shimmer}`} />
            <div className={`h-20 ${shimmer}`} />
            <div className={`h-20 ${shimmer}`} />
            <div className={`h-20 ${shimmer}`} />
          </div>
          <div className={`h-14 w-full ${shimmer}`} />
          <div className={`h-12 w-full ${shimmer}`} />
        </div>
      </div>
      <div className={`mt-8 h-28 w-full rounded-[22px] ${shimmer}`} />
      <div className={`mt-10 h-72 w-full rounded-[22px] ${shimmer}`} />
    </div>
  );
}
