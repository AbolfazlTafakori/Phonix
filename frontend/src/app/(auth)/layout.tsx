import Link from "next/link";
import type { ReactNode } from "react";
import Img from "@/components/ui/Img";

export default function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="home-light auth-bg relative flex min-h-screen items-center justify-center overflow-hidden px-4 py-16">
      {/* drifting brand blobs so the surface feels alive instead of a flat white sheet */}
      <div aria-hidden className="auth-blob auth-blob-1" />
      <div aria-hidden className="auth-blob auth-blob-2" />
      <div aria-hidden className="auth-blob auth-blob-3" />

      <Link href="/" className="absolute right-6 top-6 z-10 flex items-center gap-2.5 sm:right-10 sm:top-8">
        <Img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-12 w-auto sm:h-14" sizes="240px" />
        <span className="text-[15px] font-extrabold leading-[1.05] text-[var(--hl-ink)] sm:text-lg">
          Phoenix
          <br />
          Verify
        </span>
      </Link>

      <div className="relative z-10 w-full max-w-[980px]">{children}</div>
    </div>
  );
}
