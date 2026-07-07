import type { ReactNode } from "react";

// Split auth card: the form on the right, a promotional image on the left. A fixed desktop height keeps
// the frame and the image the same size across states/pages; only the form content changes. The image is
// passed in so each page (login / register / reset) can show its own artwork.
export default function AuthShell({ image, children }: { image: string; children: ReactNode }) {
  return (
    <div className="mx-auto grid w-full max-w-[440px] overflow-hidden rounded-[26px] border-2 border-[#ff5a1f]/45 bg-[var(--chat-surface)] shadow-[0_30px_80px_-35px_rgba(239,35,60,0.35)] lg:h-[760px] lg:max-w-[980px] lg:grid-cols-2">
      {/* form panel (right in RTL) */}
      <div className="flex items-center overflow-y-auto px-6 py-8 sm:px-10">
        <div className="mx-auto w-full max-w-[380px]">{children}</div>
      </div>
      {/* promo panel (left in RTL) — visual only */}
      <div className="relative hidden border-r border-[var(--chat-border)] bg-[#fdf1ec] lg:block">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src={image} alt="فونیکس وریفای — دنیای خدمات دیجیتال" className="absolute inset-0 h-full w-full object-cover" />
      </div>
    </div>
  );
}
