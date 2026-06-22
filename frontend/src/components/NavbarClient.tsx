"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import type { SiteContent, Notification } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { useCart } from "@/lib/cart";
import { api } from "@/lib/api";
import { SearchIcon, CartIcon, UserIcon, BellIcon } from "./Icons";
import MenuIcon from "./account/MenuIcon";
import NavLink from "./NavLink";

type Props = { brand: SiteContent["brand"]; header: SiteContent["header"] };
type Menu = "search" | "bell" | "account" | null;

const accountItems = [
  { label: "داشبورد", href: "/account", icon: "user" },
  { label: "لیست سفارشات", href: "/account/orders", icon: "orders" },
  { label: "تیکت‌ها", href: "/account/tickets", icon: "ticket" },
  { label: "احراز هویت", href: "/account/kyc", icon: "shield" },
];

// extra entries shown under the main nav in the mobile drawer
const menuExtra = [
  { label: "درباره ما", href: "#", icon: "help" },
  { label: "تماس با ما", href: "#", icon: "phone" },
  { label: "وبلاگ", href: "/blog", icon: "blog" },
  { label: "کانال تلگرام", href: "#", icon: "telegram" },
];

function navIcon(href: string): string {
  if (href === "/") return "home";
  if (href.startsWith("/blog")) return "blog";
  return "box";
}

const iconBtn =
  "grid h-10 w-10 shrink-0 place-items-center rounded-xl border border-white/10 bg-white/5 text-white/85 transition hover:bg-white/10 hover:text-white sm:h-11 sm:w-11";

function fmtDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString("fa-IR", { dateStyle: "short", timeStyle: "short" });
  } catch {
    return "";
  }
}

function BellPanel({ notifs, onClose }: { notifs: Notification[]; onClose: () => void }) {
  const [tab, setTab] = useState<"private" | "public">("private");
  const shown = notifs.filter((n) => (tab === "public" ? n.isPublic : !n.isPublic));

  return (
    <div>
      <div className="flex items-center justify-between border-b border-white/8 px-5 py-3.5">
        <h3 className="text-sm font-bold text-white">پیام‌ها</h3>
        <button onClick={onClose} aria-label="بستن" className="text-white/45 transition hover:text-white">✕</button>
      </div>
      <div className="flex border-b border-white/8 text-sm">
        {(["private", "public"] as const).map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`flex-1 py-3 font-bold transition ${tab === t ? "border-b-2 border-[#3a64f2] text-white" : "text-white/45 hover:text-white/70"}`}
          >
            {t === "private" ? "پیام‌های خصوصی" : "پیام‌های عمومی"}
          </button>
        ))}
      </div>
      <div className="max-h-[60vh] overflow-y-auto">
        {shown.length === 0 ? (
          <p className="px-5 py-10 text-center text-sm text-white/40">پیامی وجود ندارد.</p>
        ) : (
          <ul className="divide-y divide-white/6">
            {shown.map((n) => {
              const body = (
                <div className={`px-5 py-3.5 transition hover:bg-white/[0.03] ${!n.isRead ? "bg-[#3a64f2]/[0.06]" : ""}`}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="flex items-center gap-2 text-sm font-bold text-white">
                      {!n.isRead && <span className="h-2 w-2 shrink-0 rounded-full bg-[#e60053]" />}
                      {n.title}
                    </span>
                    <span className="shrink-0 text-[11px] text-white/35" dir="ltr">{fmtDate(n.createdAtUtc)}</span>
                  </div>
                  {n.body && <p className="mt-1 text-xs leading-6 text-white/55">{n.body}</p>}
                </div>
              );
              return <li key={n.id}>{n.link ? <Link href={n.link} onClick={onClose}>{body}</Link> : body}</li>;
            })}
          </ul>
        )}
      </div>
    </div>
  );
}

export default function NavbarClient({ brand, header }: Props) {
  const router = useRouter();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [menu, setMenu] = useState<Menu>(null);
  const [term, setTerm] = useState("");
  const { user, logout } = useAuth();
  const { count } = useCart();
  const [notifs, setNotifs] = useState<Notification[]>([]);
  const [unread, setUnread] = useState(0);

  useEffect(() => {
    if (!user) {
      setNotifs([]);
      setUnread(0);
      return;
    }
    api.notifications
      .mine()
      .then((list) => {
        setNotifs(list);
        setUnread(list.filter((n) => !n.isRead).length);
      })
      .catch(() => {});
  }, [user]);

  useEffect(() => {
    document.body.style.overflow = mobileOpen ? "hidden" : "";
    return () => {
      document.body.style.overflow = "";
    };
  }, [mobileOpen]);

  function toggle(m: Exclude<Menu, null>) {
    setMenu((cur) => (cur === m ? null : m));
    if (m === "bell" && menu !== "bell" && unread > 0) {
      api.notifications.markRead().catch(() => {});
      setUnread(0);
    }
  }

  function submitSearch(e: React.FormEvent) {
    e.preventDefault();
    const q = term.trim();
    router.push(q ? `/films?q=${encodeURIComponent(q)}` : "/films");
    setMenu(null);
    setTerm("");
  }

  return (
    <>
    <header className="sticky top-0 z-50 w-full border-b border-white/5 bg-ink/90 backdrop-blur">
      <div className="mx-auto flex h-[72px] max-w-[1320px] items-center gap-3 px-4 sm:px-5">
        {menu === "search" ? (
          /* mobile/tablet: the inline search bar takes over the whole row */
          <form onSubmit={submitSearch} className="flex h-11 flex-1 items-center gap-2 rounded-full border border-white/15 bg-white/5 px-4">
            <SearchIcon className="h-5 w-5 shrink-0 text-white/55" />
            <input
              autoFocus
              dir="rtl"
              value={term}
              onChange={(e) => setTerm(e.target.value)}
              placeholder={header.searchPlaceholder}
              className="w-full min-w-0 bg-transparent text-[15px] font-bold text-white placeholder:text-white/45 focus:outline-none"
            />
            <button type="button" onClick={() => setMenu(null)} aria-label="بستن جستجو" className="shrink-0 text-white/55 transition hover:text-white">
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M6 6l12 12M18 6L6 18" /></svg>
            </button>
          </form>
        ) : (
          <>
            {/* brand + nav — pinned to the right next to the logo */}
            <div className="flex min-w-0 shrink-0 items-center gap-3 sm:gap-5 lg:gap-7">
              <Link href="/" onClick={() => setMobileOpen(false)} className="flex shrink-0 items-center gap-2">
                <img src={brand.logo} alt={brand.siteName} className="h-9 w-auto sm:h-11" />
                <span className="hidden font-bigshot text-[15px] leading-[1.05] text-white sm:block">
                  {brand.logoLine1}
                  <br />
                  {brand.logoLine2}
                </span>
              </Link>
              <nav className="hidden items-center gap-6 lg:flex">
                {header.navLinks.map((link) => (
                  <NavLink key={link.label} href={link.href} label={link.label} hasMenu={link.hasMenu} />
                ))}
                <NavLink href="/blog" label="وبلاگ" />
              </nav>
              {/* phone-only menu toggle, sits right next to the logo */}
              <button
                onClick={() => setMobileOpen((v) => !v)}
                aria-label="منو"
                aria-expanded={mobileOpen}
                className={`${iconBtn} sm:hidden`}
              >
                <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  {mobileOpen ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 7h16M4 12h16M4 17h16" />}
                </svg>
              </button>
            </div>

            {/* desktop search bar fills the centre; an empty spacer below lg */}
            <div className="flex flex-1 justify-center">
              <form onSubmit={submitSearch} className="hidden h-11 w-full max-w-[360px] items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 transition focus-within:border-white/25 lg:flex">
                <button type="submit" aria-label="جستجو" className="shrink-0 text-white/55 transition hover:text-white">
                  <SearchIcon className="h-5 w-5" />
                </button>
                <input
                  dir="rtl"
                  value={term}
                  onChange={(e) => setTerm(e.target.value)}
                  placeholder={header.searchPlaceholder}
                  className="w-full min-w-0 bg-transparent text-[15px] font-bold text-white placeholder:text-white/45 focus:outline-none"
                />
              </form>
            </div>
          </>
        )}

        {/* action cluster (left) */}
        <div className="flex shrink-0 items-center gap-1.5 sm:gap-2">
          {menu !== "search" && (
            <button onClick={() => toggle("search")} aria-label="جستجو" className={`${iconBtn} lg:hidden`}>
              <SearchIcon className="h-5 w-5" />
            </button>
          )}

          {user && (
            <div className="relative max-sm:hidden">
              <button onClick={() => toggle("bell")} aria-label="پیام‌ها" className={`${iconBtn} relative`}>
                <BellIcon className="h-5 w-5" />
                {unread > 0 && (
                  <span className="absolute -right-1 -top-1 grid h-5 min-w-5 place-items-center rounded-full bg-[#e60053] px-1 text-[10px] font-bold text-white">
                    {unread > 9 ? "9+" : unread}
                  </span>
                )}
              </button>
              {menu === "bell" && (
                <div className="absolute left-0 top-full z-50 mt-2 w-[min(23rem,calc(100vw-2rem))] overflow-hidden rounded-2xl border border-white/10 bg-[#15151f] shadow-2xl">
                  <BellPanel notifs={notifs} onClose={() => setMenu(null)} />
                </div>
              )}
            </div>
          )}

          {user && (
            <Link href="/cart" aria-label={header.cartLabel} className={`${iconBtn} relative max-sm:hidden`}>
              <CartIcon className="h-5 w-5" />
              {count > 0 && (
                <span className="absolute -right-1 -top-1 grid h-5 min-w-5 place-items-center rounded-full bg-[#e60053] px-1 text-[10px] font-bold text-white">{count}</span>
              )}
            </Link>
          )}

          {user ? (
            <div className="relative">
              <button onClick={() => toggle("account")} aria-label="حساب کاربری" className={iconBtn}>
                <UserIcon className="h-5 w-5" />
              </button>
              {menu === "account" && (
                <div className="absolute left-0 top-full z-50 mt-2 w-[min(17rem,calc(100vw-2rem))] overflow-hidden rounded-2xl border border-white/10 bg-[#15151f] shadow-2xl">
                  <div className="flex items-center gap-3 border-b border-white/8 px-5 py-4">
                    <div className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-base font-bold text-white">
                      {(user.name || user.username || "؟").charAt(0)}
                    </div>
                    <div className="min-w-0">
                      <p className="truncate text-sm font-bold text-white">{user.name || user.username}</p>
                      {user.phone && <p className="truncate text-xs text-white/45" dir="ltr">{user.phone}</p>}
                    </div>
                  </div>
                  <nav className="py-1.5">
                    {accountItems.map((it) => (
                      <Link
                        key={it.href}
                        href={it.href}
                        onClick={() => setMenu(null)}
                        className="flex items-center gap-3 px-5 py-2.5 text-sm font-medium text-white/75 transition hover:bg-white/5 hover:text-white"
                      >
                        <MenuIcon name={it.icon} className="h-5 w-5 text-white/45" />
                        {it.label}
                      </Link>
                    ))}
                  </nav>
                  <button
                    onClick={() => { setMenu(null); logout(); }}
                    className="flex w-full items-center gap-3 border-t border-white/8 px-5 py-3 text-sm font-medium text-white/60 transition hover:bg-white/5 hover:text-white"
                  >
                    <MenuIcon name="logout" className="h-5 w-5 text-white/45" />
                    خروج از حساب
                  </button>
                </div>
              )}
            </div>
          ) : (
            <Link
              href={header.accountLink}
              className="flex items-center gap-2 rounded-full bg-gradient-to-l from-[#6d28d9] to-[#4f1f9e] px-4 py-2.5 text-[14px] font-bold text-white transition hover:brightness-110"
            >
              <UserIcon className="h-5 w-5" />
              <span className="hidden sm:inline">ثبت نام / ورود</span>
            </Link>
          )}

          <button
            onClick={() => setMobileOpen((v) => !v)}
            aria-label="منو"
            aria-expanded={mobileOpen}
            className={`${iconBtn} lg:hidden ${menu === "search" ? "hidden" : "max-sm:hidden"}`}
          >
            <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              {mobileOpen ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 7h16M4 12h16M4 17h16" />}
            </svg>
          </button>
        </div>
      </div>

      {/* click-away overlay for dropdowns */}
      {(menu === "bell" || menu === "account") && <div className="fixed inset-0 z-40" onClick={() => setMenu(null)} />}
    </header>

      {/* mobile side drawer — rendered outside <header> so the header's backdrop-blur
          doesn't trap this fixed element to the 72px header height */}
      <div className={`fixed inset-0 z-[60] lg:hidden ${mobileOpen ? "" : "pointer-events-none"}`} aria-hidden={!mobileOpen}>
        {/* backdrop */}
        <div
          onClick={() => setMobileOpen(false)}
          className={`absolute inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ${mobileOpen ? "opacity-100" : "opacity-0"}`}
        />
        {/* sliding panel (from the right) — explicit transform avoids Tailwind v4's
            independent `translate` property getting stuck mid-toggle */}
        <aside
          style={{ transform: mobileOpen ? "translateX(0)" : "translateX(100%)", transition: "transform 300ms ease-out" }}
          className="absolute inset-y-0 right-0 flex w-[min(86vw,360px)] flex-col bg-[#13131c] shadow-2xl"
        >
          <div className="flex items-center justify-between border-b border-white/8 px-5 py-4">
            <Link href="/" onClick={() => setMobileOpen(false)} className="flex items-center gap-2">
              <img src={brand.logo} alt={brand.siteName} className="h-9 w-auto" />
              <span className="font-bigshot text-[15px] leading-[1.05] text-white">
                {brand.logoLine1}
                <br />
                {brand.logoLine2}
              </span>
            </Link>
            <button
              onClick={() => setMobileOpen(false)}
              aria-label="بستن منو"
              className="grid h-9 w-9 place-items-center rounded-lg text-white/55 transition hover:bg-white/5 hover:text-white"
            >
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M6 6l12 12M18 6L6 18" /></svg>
            </button>
          </div>

          <nav className="flex-1 overflow-y-auto py-2">
            {header.navLinks.map((link) => (
              <Link
                key={link.label}
                href={link.href}
                onClick={() => setMobileOpen(false)}
                className="flex items-center gap-3 px-5 py-3.5 text-[15px] font-bold text-white/85 transition hover:bg-white/5 hover:text-white"
              >
                <MenuIcon name={navIcon(link.href)} className="h-[22px] w-[22px] shrink-0 text-white/55" />
                {link.label}
              </Link>
            ))}

            <div className="mx-5 my-2 h-px bg-white/8" />

            {menuExtra.map((it) => (
              <Link
                key={it.label}
                href={it.href}
                onClick={() => setMobileOpen(false)}
                className="flex items-center gap-3 px-5 py-3.5 text-[15px] font-bold text-white/75 transition hover:bg-white/5 hover:text-white"
              >
                <MenuIcon name={it.icon} className="h-[22px] w-[22px] shrink-0 text-white/55" />
                {it.label}
              </Link>
            ))}
          </nav>
        </aside>
      </div>
    </>
  );
}
