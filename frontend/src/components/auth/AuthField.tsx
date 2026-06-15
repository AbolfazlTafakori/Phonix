import type { ReactNode } from "react";

type Props = {
  label: string;
  aside?: ReactNode;
  type?: string;
  placeholder?: string;
};

export default function AuthField({ label, aside, type = "text", placeholder }: Props) {
  return (
    <div className="mb-5">
      <div className="mb-2 flex items-center justify-between gap-3">
        <label className="text-sm font-medium text-white/85">{label}</label>
        {aside}
      </div>
      <input
        type={type}
        placeholder={placeholder}
        className="h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20"
      />
    </div>
  );
}
