"use client";

import { useRouter } from "next/navigation";

/**
 * Back control shown above the product name on small screens, where the product page hides the bottom tab
 * bar for its sticky buy bar. Steps back in history, or falls back to the products listing when the product
 * was opened directly.
 */
export default function MobileBackButton() {
  const router = useRouter();
  const goBack = () => {
    if (typeof window !== "undefined" && window.history.length > 1) router.back();
    else router.push("/products");
  };
  return (
    <button
      type="button"
      onClick={goBack}
      aria-label="بازگشت"
      className="mb-2 grid h-9 w-9 place-items-center rounded-full border transition active:scale-95 lg:hidden"
      style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", color: "var(--ac-text)" }}
    >
      <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m9 6 6 6-6 6" /></svg>
    </button>
  );
}
