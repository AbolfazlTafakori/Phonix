export default function SectionHeading({
  title,
  align = "center",
}: {
  title: string;
  align?: "center" | "right";
}) {
  return (
    <div className={align === "right" ? "flex justify-start" : "flex justify-center"}>
      <div className="relative rounded-2xl border border-white/10 bg-[#1b1b2a]/70 px-12 py-3 shadow-[0_0_60px_-15px_rgba(230,0,83,0.55)]">
        <span className="absolute inset-y-3 right-0 w-[3px] rounded-full bg-gradient-to-b from-[#e60053] to-transparent" />
        <span className="absolute inset-y-3 left-0 w-[3px] rounded-full bg-gradient-to-b from-[#e60053] to-transparent" />
        <h2 className="text-2xl font-bold text-white sm:text-[32px]">{title}</h2>
      </div>
    </div>
  );
}
