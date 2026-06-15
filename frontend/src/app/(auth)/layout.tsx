import Link from "next/link";
import type { ReactNode } from "react";

export default function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div
      className="relative flex min-h-screen items-center justify-center overflow-hidden px-4 py-20"
      style={{
        backgroundColor: "#0b0b12",
        backgroundImage: `
          radial-gradient(1250px 950px at -6% -8%, rgba(230, 0, 83, 0.45), transparent 60%),
          radial-gradient(1350px 1020px at 106% 108%, rgba(62, 58, 242, 0.50), transparent 60%)
        `,
      }}
    >
      <Link href="/" className="absolute right-6 top-6 flex items-center gap-3 sm:right-10 sm:top-8">
        <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-16 w-auto sm:h-[72px]" />
        <span className="font-bigshot text-lg leading-[1.05] text-white sm:text-xl">
          Phoenix
          <br />
          Verify
        </span>
      </Link>

      {children}
    </div>
  );
}
