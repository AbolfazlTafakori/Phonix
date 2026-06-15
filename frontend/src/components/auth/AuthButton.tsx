import type { ReactNode } from "react";

export default function AuthButton({ children }: { children: ReactNode }) {
  return (
    <button
      type="submit"
      className="mt-3 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-base font-bold text-white shadow-[0_14px_36px_-14px_rgba(58,100,242,0.8)] transition hover:brightness-110"
    >
      {children}
    </button>
  );
}
