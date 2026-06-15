import type { ReactNode } from "react";

export default function AuthCard({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="relative w-full max-w-[660px] rounded-2xl border border-white/10 bg-[#16161f]/85 px-6 py-10 shadow-[0_30px_80px_-30px_rgba(0,0,0,0.85)] sm:px-12 sm:py-12">
      <h1 className="mb-9 text-center text-2xl font-bold text-white sm:text-[28px]">{title}</h1>
      {children}
    </div>
  );
}
