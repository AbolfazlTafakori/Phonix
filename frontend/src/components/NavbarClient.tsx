"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import type { SiteContent } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { useCart } from "@/lib/cart";
import { SearchIcon, CartIcon, UserIcon } from "./Icons";
import NavLink from "./NavLink";

type Props = { brand: SiteContent["brand"]; header: SiteContent["header"] };

function SearchBox({ placeholder, onSubmit }: { placeholder: string; onSubmit?: () => void }) {
  const router = useRouter();
  const [term, setTerm] = useState("");

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const q = term.trim();
    router.push(q ? `/films?q=${encodeURIComponent(q)}` : "/films");
    onSubmit?.();
  }

  return (
    <form onSubmit={submit} className="flex h-11 w-full items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 text-white/60 transition focus-within:border-white/25">
      <button type="submit" aria-label="جستجو" className="shrink-0 transition hover:text-white">
        <SearchIcon className="h-4 w-4" />
      </button>
      <input
        dir="rtl"
        value={term}
        onChange={(e) => setTerm(e.target.value)}
        placeholder={placeholder}
        className="w-full bg-transparent text-[15px] font-bold text-white placeholder:text-white/45 focus:outline-none"
      />
    </form>
  );
}

export default function NavbarClient({ brand, header }: Props) {
  const [open, setOpen] = useState(false);
  const { user, logout } = useAuth();
  const { count } = useCart();

  return (
    <header className="sticky top-0 z-50 w-full border-b border-white/5 bg-ink/90 backdrop-blur">
      <div className="mx-auto flex h-[72px] max-w-[1320px] items-center justify-between gap-4 px-5">
        {/* brand */}
        <Link href="/" onClick={() => setOpen(false)} className="flex shrink-0 items-center gap-2">
          <img src={brand.logo} alt={brand.siteName} className="h-10 w-auto sm:h-11" />
          <span className="font-bigshot text-[15px] leading-[1.05] text-white">
            {brand.logoLine1}
            <br />
            {brand.logoLine2}
          </span>
        </Link>

        {/* desktop nav */}
        <nav className="hidden items-center gap-7 lg:flex">
          {header.navLinks.map((link) => (
            <NavLink key={link.label} href={link.href} label={link.label} hasMenu={link.hasMenu} />
          ))}
        </nav>

        {/* desktop search */}
        <div className="hidden flex-1 justify-center lg:flex">
          <div className="w-full max-w-[320px]">
            <SearchBox placeholder={header.searchPlaceholder} />
          </div>
        </div>

        {/* desktop cart + account */}
        <div className="hidden items-center gap-3 lg:flex">
          <Link
            href="/cart"
            className="relative flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 py-2 text-[15px] font-bold text-white/90 transition hover:bg-white/10 hover:text-white"
          >
            <CartIcon className="h-5 w-5" />
            {header.cartLabel}
            {count > 0 && (
              <span className="grid h-5 min-w-5 place-items-center rounded-full bg-[#e60053] px-1 text-[11px] font-bold text-white">{count}</span>
            )}
          </Link>
          {user ? (
            <div className="flex items-center gap-2">
              <Link
                href="/account"
                className="flex items-center gap-2 rounded-full bg-gradient-to-l from-[#6d28d9] to-[#4f1f9e] px-5 py-2.5 text-[15px] font-bold text-white shadow-[0_8px_24px_-8px_rgba(109,40,217,0.8)] transition hover:brightness-110"
              >
                <UserIcon className="h-5 w-5" />
                {user.username}
              </Link>
              <button
                onClick={logout}
                className="rounded-full border border-white/10 bg-white/5 px-4 py-2.5 text-[14px] font-bold text-white/70 transition hover:text-white"
              >
                خروج
              </button>
            </div>
          ) : (
            <Link
              href={header.accountLink}
              className="flex items-center gap-2 rounded-full bg-gradient-to-l from-[#6d28d9] to-[#4f1f9e] px-5 py-2.5 text-[15px] font-bold text-white shadow-[0_8px_24px_-8px_rgba(109,40,217,0.8)] transition hover:brightness-110"
            >
              <UserIcon className="h-5 w-5" />
              {header.accountLabel}
            </Link>
          )}
        </div>

        {/* mobile hamburger */}
        <button
          onClick={() => setOpen((v) => !v)}
          aria-label="منو"
          aria-expanded={open}
          className="grid h-11 w-11 shrink-0 place-items-center rounded-xl border border-white/10 bg-white/5 text-white transition hover:bg-white/10 lg:hidden"
        >
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            {open ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 7h16M4 12h16M4 17h16" />}
          </svg>
        </button>
      </div>

      {/* mobile drawer */}
      {open && (
        <div className="border-t border-white/8 bg-ink/95 px-5 py-5 lg:hidden">
          <div className="mb-4">
            <SearchBox placeholder={header.searchPlaceholder} onSubmit={() => setOpen(false)} />
          </div>

          <nav className="flex flex-col gap-1">
            {header.navLinks.map((link) => (
              <Link
                key={link.label}
                href={link.href}
                onClick={() => setOpen(false)}
                className="rounded-xl px-4 py-3 text-[16px] font-bold text-white/85 transition hover:bg-white/5 hover:text-white"
              >
                {link.label}
              </Link>
            ))}
          </nav>

          <div className="mt-4 flex flex-col gap-3">
            <Link
              href="/cart"
              onClick={() => setOpen(false)}
              className="flex items-center justify-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 py-3 font-bold text-white"
            >
              <CartIcon className="h-5 w-5" />
              {header.cartLabel}
              {count > 0 && <span className="grid h-5 min-w-5 place-items-center rounded-full bg-[#e60053] px-1 text-[11px] font-bold text-white">{count}</span>}
            </Link>
            {user ? (
              <>
                <Link
                  href="/account"
                  onClick={() => setOpen(false)}
                  className="flex items-center justify-center gap-2 rounded-full bg-gradient-to-l from-[#6d28d9] to-[#4f1f9e] px-5 py-3 font-bold text-white"
                >
                  <UserIcon className="h-5 w-5" />
                  {user.username}
                </Link>
                <button
                  onClick={() => { logout(); setOpen(false); }}
                  className="rounded-full border border-white/10 bg-white/5 px-5 py-3 font-bold text-white/70"
                >
                  خروج از حساب
                </button>
              </>
            ) : (
              <Link
                href={header.accountLink}
                onClick={() => setOpen(false)}
                className="flex items-center justify-center gap-2 rounded-full bg-gradient-to-l from-[#6d28d9] to-[#4f1f9e] px-5 py-3 font-bold text-white"
              >
                <UserIcon className="h-5 w-5" />
                {header.accountLabel}
              </Link>
            )}
          </div>
        </div>
      )}
    </header>
  );
}
