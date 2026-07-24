"use client";

import type { ReactNode } from "react";
import { usePathname } from "next/navigation";

/**
 * Hides the footer on small screens for the app-like surfaces — the account area, a single product page and
 * the category browser — where a long marketing footer only gets in the way of the bottom navigation. Every
 * other page, and every desktop viewport, keeps it.
 */
export default function FooterVisibility({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const hideOnMobile =
    pathname.startsWith("/account") ||
    pathname.startsWith("/categories") ||
    /^\/products\/[^/]+/.test(pathname);

  return <div className={hideOnMobile ? "hidden lg:block" : ""}>{children}</div>;
}
