"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, usePathname } from "next/navigation";
import type { SiteContent, Product, Notification } from "@/lib/types";
import { formatToman, productDisplayPrice } from "@/lib/format";
import { useAuth } from "@/lib/auth";
import { useCart } from "@/lib/cart";
import { api } from "@/lib/api";
import { SearchIcon, CartIcon, UserIcon, BellIcon } from "../Icons";
import ThemeToggle from "./ThemeToggle";
import { productPath } from "@/lib/seo";

type Props = { brand: SiteContent["brand"]; searchPlaceholder: string };

const navLinks = [
  { label: "خانه", href: "/" },
  { label: "محصولات", href: "/products" },
  { label: "دسته‌بندی‌ها", href: "/categories" },
  { label: "وبلاگ", href: "/blog" },
  { label: "درباره ما", href: "#" },
  { label: "تماس با ما", href: "#" },
];

function fmtDate(iso: string): string {
  try { return new Date(iso).toLocaleString("fa-IR", { dateStyle: "short", timeStyle: "short" }); }
  catch { return ""; }
}

/** The notification list that drops from the header bell — styled for the light storefront theme. */
function NotifDropdown({ notifs, onClose }: { notifs: Notification[]; onClose: () => void }) {
  return (
    <div className="absolute left-0 top-full z-[60] mt-2 w-[300px] max-w-[calc(100vw-2rem)] overflow-hidden rounded-2xl border border-[var(--hl-border)] bg-white shadow-[0_24px_50px_-18px_rgba(0,0,0,0.25)]">
      <div className="flex items-center justify-between border-b border-[var(--hl-border)] px-4 py-3">
        <h3 className="text-[14px] font-bold text-[var(--hl-ink)]">اعلان‌ها</h3>
        <button onClick={onClose} aria-label="بستن" className="grid h-7 w-7 place-items-center rounded-full text-[var(--hl-muted)] transition hover:text-[var(--hl-ink)]">
          <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
        </button>
      </div>
      {notifs.length === 0 ? (
        <p className="px-5 py-10 text-center text-[13px] text-[var(--hl-muted)]">اعلانی وجود ندارد.</p>
      ) : (
        <ul className="max-h-[60vh] divide-y divide-[var(--hl-border)] overflow-y-auto">
          {notifs.slice(0, 20).map((n) => {
            const body = (
              <>
                <div className="flex items-center justify-between gap-2">
                  <span className="flex min-w-0 items-center gap-2 text-[13px] font-bold text-[var(--hl-ink)]">
                    {!n.isRead && <span className="h-2 w-2 shrink-0 rounded-full bg-[var(--hl-red)]" />}
                    <span className="truncate">{n.title}</span>
                  </span>
                  <span className="shrink-0 text-[10px] text-[var(--hl-muted)]" dir="ltr">{fmtDate(n.createdAtUtc)}</span>
                </div>
                {n.body && <p className="mt-1 line-clamp-2 text-[12px] leading-6 text-[var(--hl-ink-2)]">{n.body}</p>}
              </>
            );
            return (
              <li key={n.id}>
                {n.link ? (
                  <Link href={n.link} onClick={onClose} className={`block px-4 py-3 transition hover:bg-[#f7f8fa] ${!n.isRead ? "bg-[#fff6f2]" : ""}`}>{body}</Link>
                ) : (
                  <div className={`px-4 py-3 ${!n.isRead ? "bg-[#fff6f2]" : ""}`}>{body}</div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

export default function HomeHeader({ brand, searchPlaceholder }: Props) {
  const router = useRouter();
  const pathname = usePathname();
  // On mobile the bottom tab bar already carries cart + account, so the header drops them there. The one
  // exception is the product detail page, where the sticky buy bar replaces the tab bar — the header keeps
  // them so those actions never disappear. Desktop always shows them.
  const onProductDetail = /^\/products\/[^/]+/.test(pathname);
  // Inside "my account" the hub is the menu, so — like the marketplaces — the mobile header drops the search
  // field there (desktop keeps it).
  const onAccount = pathname.startsWith("/account");
  // Highlight only the FIRST nav link that matches the current route, so two links that share a
  // destination (محصولات / دسته‌بندی‌ها → /products) don't light up together. Anchors ("#") never match.
  const activeIndex = navLinks.findIndex(
    (l) => l.href.startsWith("/") && (l.href === "/" ? pathname === "/" : pathname.startsWith(l.href)),
  );
  const { user } = useAuth();
  const { count } = useCart();
  const [term, setTerm] = useState("");
  const [focused, setFocused] = useState(false);
  const [products, setProducts] = useState<Product[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [notifs, setNotifs] = useState<Notification[]>([]);
  const [unread, setUnread] = useState(0);
  const [bellOpen, setBellOpen] = useState(false);
  const bellRef = useRef<HTMLDivElement>(null);

  // Poll the customer's notifications so the bell badge stays current; pause while the tab is hidden.
  useEffect(() => {
    if (!user) { setNotifs([]); setUnread(0); return; }
    let alive = true;
    const refresh = () => api.notifications.mine()
      .then((list) => { if (alive) { setNotifs(list); setUnread(list.filter((n) => !n.isRead).length); } })
      .catch(() => {});
    refresh();
    const timer = setInterval(() => { if (document.visibilityState === "visible") refresh(); }, 20000);
    return () => { alive = false; clearInterval(timer); };
  }, [user]);

  // Close the bell dropdown on an outside click.
  useEffect(() => {
    if (!bellOpen) return;
    const onDown = (e: MouseEvent) => { if (bellRef.current && !bellRef.current.contains(e.target as Node)) setBellOpen(false); };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [bellOpen]);

  function toggleBell() {
    setBellOpen((open) => {
      const next = !open;
      if (next && unread > 0) { api.notifications.markRead().catch(() => {}); setUnread(0); }
      return next;
    });
  }

  const loadProducts = useCallback(() => {
    setFocused(true);
    setLoaded((was) => {
      if (!was) api.products.list().then((l) => setProducts(l.filter((p) => p.isActive))).catch(() => setLoaded(false));
      return true;
    });
  }, []);

  const needle = term.trim().toLowerCase();
  const suggestions = needle
    ? products.filter((p) => p.name.toLowerCase().includes(needle) || p.sku.toLowerCase().includes(needle)).slice(0, 6)
    : [];

  function submitSearch(e: React.FormEvent) {
    e.preventDefault();
    const q = term.trim();
    router.push(q ? `/products?q=${encodeURIComponent(q)}` : "/products");
    setFocused(false);
  }

  const searchBox = (autoFocus = false) => (
    <div className="relative w-full">
      <form
        onSubmit={submitSearch}
        className="flex h-11 w-full items-center gap-2 rounded-full border border-[var(--hl-border)] bg-[#f7f8fa] px-5 transition focus-within:border-[var(--hl-red)]/40 focus-within:bg-white"
      >
        <button type="submit" aria-label="جستجو" className="shrink-0 text-[var(--hl-muted)] transition hover:text-[var(--hl-red)]">
          <SearchIcon className="h-5 w-5" />
        </button>
        <input
          dir="rtl"
          autoFocus={autoFocus}
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          onFocus={loadProducts}
          onBlur={() => setTimeout(() => setFocused(false), 150)}
          placeholder={searchPlaceholder || "جستجو در فونیکس"}
          className="w-full min-w-0 bg-transparent text-[17px] font-medium text-[var(--hl-ink)] placeholder:text-[var(--hl-muted)] focus:outline-none"
        />
      </form>
      {needle && focused && (
        <div className="absolute inset-x-0 top-full z-50 mt-2 overflow-hidden rounded-2xl border border-[var(--hl-border)] bg-white shadow-xl">
          {suggestions.length === 0 ? (
            <p className="px-4 py-6 text-center text-sm text-[var(--hl-muted)]">{loaded ? "محصولی یافت نشد" : "در حال جستجو…"}</p>
          ) : (
            <ul className="max-h-[60vh] overflow-y-auto py-1.5">
              {suggestions.map((p) => (
                <li key={p.id}>
                  <Link
                    href={productPath(p)}
                    onClick={() => setFocused(false)}
                    className="flex items-center gap-3 px-4 py-2.5 transition hover:bg-[#f7f8fa]"
                  >
                    <img src={p.image} alt={p.name} className="h-10 w-10 shrink-0 rounded-lg object-cover" />
                    <span className="min-w-0 flex-1 truncate text-sm font-bold text-[var(--hl-ink)]">{p.name}</span>
                    <span className="shrink-0 text-xs font-bold text-[var(--hl-red)]">{formatToman(productDisplayPrice(p))}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );

  return (
    <>
    <header className="sticky top-0 z-50 w-full border-b border-[var(--hl-border)] bg-white/85 backdrop-blur">
      <div className="mx-auto flex h-[72px] max-w-[1840px] items-center gap-3 px-4 sm:h-[88px] sm:gap-6 sm:px-8 xl:px-16">
        {/* brand + nav (right in RTL) */}
        <div className="flex shrink-0 items-center gap-4 lg:gap-7">
          {/* inside the account area the mobile header carries the theme toggle on this (right) side, with
              the bell opposite it — mirroring how the marketplaces lay their profile header out */}
          {onAccount && (
            <div className="lg:hidden">
              <ThemeToggle />
            </div>
          )}
          <Link href="/" className="hidden items-center gap-2.5 lg:flex">
            <img src={brand.logo} alt={brand.siteName} className="h-11 w-auto sm:h-14" />
            <span className="hidden text-[15px] font-extrabold leading-[1.1] text-[var(--hl-ink)] sm:inline-block sm:text-[17px]">
              {brand.logoLine1}
              <br />
              {brand.logoLine2}
            </span>
          </Link>
          <nav className="hidden items-center gap-6 text-[17px] font-bold lg:flex">
            {navLinks.map((l, i) => (
              <Link
                key={i}
                href={l.href}
                className={`relative py-1 transition ${
                  i === activeIndex
                    ? "text-[var(--hl-red)] after:absolute after:inset-x-0 after:-bottom-[6px] after:h-[3px] after:rounded-full after:bg-gradient-to-l after:from-[#ef233c] after:to-[#ff5a1f]"
                    : "text-[var(--hl-ink-2)] hover:text-[var(--hl-ink)]"
                }`}
              >
                {l.label}
              </Link>
            ))}
          </nav>
        </div>

        {/* search — inline on every screen, marketplace-style; hidden on mobile inside the account area */}
        <div className={onAccount ? "hidden flex-1 lg:block" : "flex-1"}>{searchBox()}</div>

        {/* actions (left in RTL) — pushed to the far edge when the search field is not there to fill the row */}
        <div className={`flex shrink-0 items-center gap-2 sm:gap-4 ${onAccount ? "ms-auto lg:ms-0" : ""}`}>
          {/* notification bell — mobile only, sitting where the theme toggle is on desktop */}
          {user && (
            <div ref={bellRef} className="relative lg:hidden">
              <button
                type="button"
                onClick={toggleBell}
                aria-label="اعلان‌ها"
                className="relative grid h-10 w-10 place-items-center rounded-full text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]"
              >
                <BellIcon className="h-[22px] w-[22px]" />
                {unread > 0 && <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-[var(--hl-red)]" />}
              </button>
              {bellOpen && <NotifDropdown notifs={notifs} onClose={() => setBellOpen(false)} />}
            </div>
          )}

          {/* cart — desktop keeps it; on mobile the bottom tab bar carries it (except product detail) */}
          {user && (
            <Link
              href="/cart"
              aria-label="سبد خرید"
              className={`relative ${onProductDetail ? "grid" : "hidden lg:grid"} h-11 w-11 place-items-center rounded-full text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]`}
            >
              <CartIcon className="h-6 w-6" />
              {count > 0 && (
                <span className="absolute -right-0.5 -top-0.5 grid h-5 min-w-5 place-items-center rounded-full bg-[var(--hl-red)] px-1 text-[10px] font-bold text-white">
                  {count}
                </span>
              )}
            </Link>
          )}

          <Link
            href={user ? "/account" : "/login"}
            aria-label={user ? "حساب کاربری" : "ورود / ثبت‌نام"}
            className={`${onProductDetail ? "flex" : "hidden lg:flex"} items-center gap-2 text-[16px] font-bold text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]`}
          >
            <UserIcon className="h-5 w-5" />
            <span className="hidden md:inline">{user ? "حساب کاربری" : "ورود / ثبت‌نام"}</span>
          </Link>

          {/* theme toggle — desktop only; the mobile header shows the bell in this spot */}
          <div className="hidden lg:block">
            <ThemeToggle />
          </div>
        </div>
      </div>
    </header>
    </>
  );
}
