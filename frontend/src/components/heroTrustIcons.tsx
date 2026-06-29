import type { ReactNode } from "react";

// Shared registry for the hero trust-row icons so the carousel (which draws them) and the admin editor
// (which lists them in a dropdown) stay in sync. Each entry's `node` is the inner SVG of a 24×24 viewBox
// drawn with stroke="currentColor", so the colour is inherited from the parent.
export type HeroTrustIconDef = { key: string; label: string; node: ReactNode };

export const HERO_TRUST_ICONS: HeroTrustIconDef[] = [
  { key: "bolt", label: "صاعقه (تحویل آنی)", node: <path d="M13 2 4.5 13H11l-1 9 8.5-11.5H12l1-8.5Z" /> },
  {
    key: "shield",
    label: "سپر (گارانتی)",
    node: (
      <>
        <path d="M12 3 5 6v6c0 4.4 3 7.2 7 8.5 4-1.3 7-4.1 7-8.5V6l-7-3Z" />
        <path d="m9 12 2 2 4-4" />
      </>
    ),
  },
  {
    key: "lock",
    label: "قفل (پرداخت امن)",
    node: (
      <>
        <rect x="5" y="11" width="14" height="9" rx="2" />
        <path d="M8 11V8a4 4 0 0 1 8 0v3" />
      </>
    ),
  },
  {
    key: "headset",
    label: "هدست (پشتیبانی)",
    node: (
      <>
        <path d="M5 13a7 7 0 0 1 14 0" />
        <path d="M5 13v3a2 2 0 0 0 2 2h1v-6H7a2 2 0 0 0-2 2Zm14 0v3a2 2 0 0 1-2 2h-1v-6h1a2 2 0 0 1 2 2Z" />
      </>
    ),
  },
  {
    key: "check",
    label: "تیک تأیید",
    node: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="m8.5 12 2.5 2.5 5-5" />
      </>
    ),
  },
  { key: "star", label: "ستاره", node: <path d="m12 3 2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.9 6.8 19.2l1-5.8L3.5 9.2l5.9-.9L12 3Z" /> },
  {
    key: "clock",
    label: "ساعت (تحویل سریع)",
    node: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v5l3.5 2" />
      </>
    ),
  },
  {
    key: "wallet",
    label: "کیف پول",
    node: (
      <>
        <rect x="3" y="6" width="18" height="13" rx="2.5" />
        <path d="M3 10h18M16.5 13.5h.5" />
      </>
    ),
  },
];

export function heroTrustIconNode(key: string): ReactNode {
  return (HERO_TRUST_ICONS.find((i) => i.key === key) ?? HERO_TRUST_ICONS[0]).node;
}
