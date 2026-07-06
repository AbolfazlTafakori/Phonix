import Link from "next/link";
import type { ReactNode } from "react";

export default function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div
      className="relative flex min-h-screen items-center justify-center overflow-hidden px-4 py-16"
      style={{
        backgroundColor: "#f7f8fb",
        backgroundImage: `
          radial-gradient(1100px 820px at -6% -8%, rgba(255, 90, 31, 0.16), transparent 60%),
          radial-gradient(1200px 900px at 106% 108%, rgba(239, 35, 60, 0.14), transparent 60%)
        `,
      }}
    >
      <Link href="/" className="absolute right-6 top-6 flex items-center gap-2.5 sm:right-10 sm:top-8">
        <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-12 w-auto sm:h-14" />
        <span className="text-[15px] font-extrabold leading-[1.05] text-[#151515] sm:text-lg">
          Phoenix
          <br />
          Verify
        </span>
      </Link>

      {children}
    </div>
  );
}
