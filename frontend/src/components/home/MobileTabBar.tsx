"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { useCart } from "@/lib/cart";
import { CartIcon, UserIcon } from "../Icons";

function HomeIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M3 10.5 12 3l9 7.5" />
      <path d="M5 9.5V20a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9.5" />
      <path d="M9.5 21v-6h5v6" />
    </svg>
  );
}

function GridIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3.5" y="3.5" width="7" height="7" rx="2" />
      <rect x="13.5" y="3.5" width="7" height="7" rx="2" />
      <rect x="3.5" y="13.5" width="7" height="7" rx="2" />
      <rect x="13.5" y="13.5" width="7" height="7" rx="2" />
    </svg>
  );
}

/**
 * The bottom tab bar for small screens — the primary way to move around on mobile, the way marketplace apps
 * do it instead of hiding everything behind a hamburger. Hidden from `lg` up (the desktop header owns
 * navigation there) and on product detail pages, where the sticky buy bar takes the bottom of the screen.
 */
export default function MobileTabBar() {
  const pathname = usePathname();
  const { user } = useAuth();
  const { count } = useCart();

  // Product detail (`/products/<slug>`) hands the bottom edge to the purchase bar. The listing (`/products`)
  // and its filtered variants keep the tab bar.
  if (/^\/products\/[^/]+/.test(pathname)) return null;

  const tabs = [
    { key: "home", label: "خانه", href: "/", icon: HomeIcon, active: pathname === "/" },
    { key: "cats", label: "دسته‌بندی", href: "/categories", icon: GridIcon, active: pathname.startsWith("/categories") },
    { key: "cart", label: "سبد خرید", href: "/cart", icon: CartIcon, active: pathname.startsWith("/cart"), badge: count },
    { key: "me", label: "فونیکس من", href: user ? "/account" : "/login", icon: UserIcon, active: pathname.startsWith("/account") || pathname.startsWith("/login") },
  ];

  return (
    <nav
      className="fixed inset-x-0 bottom-0 z-40 grid grid-cols-4 border-t bg-[var(--ac-panel-bg)] lg:hidden"
      style={{ borderColor: "var(--ac-panel-border)", boxShadow: "0 -8px 26px rgba(0,0,0,0.07)", paddingBottom: "env(safe-area-inset-bottom)" }}
    >
      {tabs.map((t) => {
        const Icon = t.icon;
        return (
          <Link
            key={t.key}
            href={t.href}
            className="flex flex-col items-center gap-1 py-2 text-[11px] font-bold transition"
            style={{ color: t.active ? "#F2551F" : "var(--ac-muted)" }}
          >
            <span className="relative">
              <Icon className="h-[22px] w-[22px]" />
              {t.badge != null && t.badge > 0 && (
                <span className="absolute -right-2 -top-1.5 grid h-4 min-w-4 place-items-center rounded-full bg-[#F2551F] px-1 text-[9px] font-black text-white">
                  {t.badge}
                </span>
              )}
            </span>
            {t.label}
          </Link>
        );
      })}
    </nav>
  );
}
