"use client";

import type { ReactNode } from "react";
import { usePathname } from "next/navigation";

/**
 * Hides the footer on small screens for the app-like surfaces the bottom navigation serves — home, products,
 * categories, cart and the account area — where a long marketing footer only gets in the way. Ordinary
 * content pages (blog, checkout, terms, auth) keep it, and so does every desktop viewport.
 */
const APP_SURFACES = ["/products", "/categories", "/cart", "/account"];

export default function FooterVisibility({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const hideOnMobile =
    pathname === "/" || APP_SURFACES.some((p) => pathname === p || pathname.startsWith(`${p}/`));

  return <div className={hideOnMobile ? "hidden lg:block" : ""}>{children}</div>;
}
