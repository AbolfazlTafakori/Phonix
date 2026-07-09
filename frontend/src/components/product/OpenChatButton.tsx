"use client";

/** Opens the site-wide LiveChat widget from the product-page support card. */
export default function OpenChatButton() {
  return (
    <button
      type="button"
      onClick={() => window.dispatchEvent(new Event("phonix:open-chat"))}
      className="mt-2.5 flex h-11 w-full items-center justify-center rounded-xl border bg-white text-[13px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]"
      style={{ borderColor: "var(--ac-btn-secondary-border)", color: "var(--ac-btn-secondary-text)" }}
    >
      چت آنلاین
    </button>
  );
}
