export default function Stars({ value, className = "" }: { value: number; className?: string }) {
  const rounded = Math.round(value);
  return (
    <span dir="ltr" className={`inline-flex items-center gap-0.5 ${className}`} aria-label={`${value} از ۵`}>
      {[1, 2, 3, 4, 5].map((i) => (
        <span key={i} className={i <= rounded ? "text-amber-400" : "text-[var(--hl-border)]"}>
          ★
        </span>
      ))}
    </span>
  );
}
