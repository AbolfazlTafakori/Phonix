"use client";

import { useCallback, useState } from "react";
import Link from "next/link";
import { useRouter, usePathname } from "next/navigation";
import type { SiteContent, Product } from "@/lib/types";
import { formatToman, productDisplayPrice } from "@/lib/format";
import { useAuth } from "@/lib/auth";
import { useCart } from "@/lib/cart";
import { api } from "@/lib/api";
import { SearchIcon, CartIcon, UserIcon } from "../Icons";
import ThemeToggle from "./ThemeToggle";
import { productPath } from "@/lib/seo";
import Img from "@/components/ui/Img";

type Props = { brand: SiteContent["brand"]; searchPlaceholder: string };

const navLinks = [
  { label: "خانه", href: "/" },
  { label: "محصولات", href: "/products" },
  { label: "دسته‌بندی‌ها", href: "/categories" },
  { label: "وبلاگ", href: "/blog" },
  { label: "درباره ما", href: "#" },
  { label: "تماس با ما", href: "#" },
];

export default function HomeHeader({ brand, searchPlaceholder }: Props) {
  const router = useRouter();
  const pathname = usePathname();
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
  const [menuOpen, setMenuOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);

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
    setSearchOpen(false);
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
          placeholder={searchPlaceholder || "جستجو در بین هزاران محصول..."}
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
                    onClick={() => { setFocused(false); setSearchOpen(false); }}
                    className="flex items-center gap-3 px-4 py-2.5 transition hover:bg-[#f7f8fa]"
                  >
                    <Img src={p.image} alt={p.name} className="h-10 w-10 shrink-0 rounded-lg object-cover" sizes="80px" />
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
          <button
            type="button"
            aria-label="منو"
            onClick={() => setMenuOpen(true)}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-xl text-[var(--hl-ink)] transition hover:text-[var(--hl-red)] lg:hidden"
          >
            <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
          </button>
          <Link href="/" className="flex items-center gap-2.5">
            <Img priority src={brand.logo} alt={brand.siteName} className="h-11 w-auto sm:h-14" sizes="240px" />
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

        {/* search — inline on desktop only */}
        <div className="hidden flex-1 lg:block">{searchBox()}</div>

        {/* actions (left in RTL) */}
        <div className="flex shrink-0 items-center gap-2 ms-auto sm:gap-4 lg:ms-0">
          {/* mobile search toggle */}
          <button
            type="button"
            aria-label="جستجو"
            onClick={() => setSearchOpen((o) => !o)}
            className="grid h-10 w-10 place-items-center rounded-full text-[var(--hl-ink)] transition hover:text-[var(--hl-red)] lg:hidden"
          >
            <SearchIcon className="h-5 w-5" />
          </button>

          {/* cart — only for signed-in users; swapped to sit where the theme toggle was */}
          {user && (
            <Link
              href="/cart"
              aria-label="سبد خرید"
              className="relative grid h-11 w-11 place-items-center rounded-full text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]"
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
            className="flex items-center gap-2 text-[16px] font-bold text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]"
          >
            <UserIcon className="h-5 w-5" />
            <span className="hidden md:inline">{user ? "حساب کاربری" : "ورود / ثبت‌نام"}</span>
          </Link>

          {/* theme toggle — desktop only (mobile lives in the drawer), swapped to the far edge */}
          <div className="hidden lg:block">
            <ThemeToggle />
          </div>
        </div>
      </div>

      {/* mobile expandable search */}
      {searchOpen && (
        <div className="border-t border-[var(--hl-border)] bg-white/95 px-4 py-3 backdrop-blur sm:px-8 lg:hidden">
          {searchBox(true)}
        </div>
      )}
    </header>

      {/* mobile side drawer + backdrop — rendered outside the header so it sits above every layer */}
      <div
        onClick={() => setMenuOpen(false)}
        aria-hidden
        className={`fixed inset-0 z-[60] bg-black/40 transition-opacity duration-200 lg:hidden ${menuOpen ? "opacity-100" : "pointer-events-none opacity-0"}`}
      />
      <aside
        className={`fixed right-0 top-0 z-[70] flex h-dvh w-72 max-w-[82vw] flex-col bg-white shadow-2xl transition-transform duration-300 lg:hidden ${menuOpen ? "translate-x-0" : "translate-x-full"}`}
      >
        <div className="flex items-center justify-between border-b border-[var(--hl-border)] px-4 py-3">
          <div className="flex items-center gap-2">
            <Img src={brand.logo} alt={brand.siteName} className="h-9 w-auto" sizes="240px" />
            <span className="text-[14px] font-extrabold leading-[1.1] text-[var(--hl-ink)]">{brand.logoLine1}<br />{brand.logoLine2}</span>
          </div>
          {/* theme toggle sits next to the close button */}
          <div className="flex items-center gap-1">
            <ThemeToggle />
            <button type="button" aria-label="بستن" onClick={() => setMenuOpen(false)} className="grid h-10 w-10 place-items-center rounded-lg text-[var(--hl-ink-2)] transition hover:text-[var(--hl-red)]">
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          </div>
        </div>
        <nav className="flex-1 overflow-y-auto p-3">
          <ul className="flex flex-col gap-1">
            {navLinks.map((l, i) => (
              <li key={i}>
                <Link
                  href={l.href}
                  onClick={() => setMenuOpen(false)}
                  className={`block rounded-xl px-4 py-3 text-[16px] font-bold transition ${i === activeIndex ? "bg-[#fff6f2] text-[var(--hl-red)]" : "text-[var(--hl-ink-2)] hover:bg-[#f7f8fa] hover:text-[var(--hl-ink)]"}`}
                >
                  {l.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </aside>
    </>
  );
}
