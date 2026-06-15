"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ChevronDown } from "./Icons";

type Props = { href: string; label: string; hasMenu?: boolean };

export default function NavLink({ href, label, hasMenu }: Props) {
  const pathname = usePathname();
  const isActive =
    href !== "#" && (href === "/" ? pathname === "/" : pathname.startsWith(href));
  const shown = isActive ? "opacity-100" : "opacity-0 group-hover:opacity-100";

  return (
    <Link
      href={href}
      className={`group relative flex items-center gap-1 px-1 pb-4 pt-2 text-[17px] font-bold transition-colors ${
        isActive ? "text-white" : "text-white/85 hover:text-white"
      }`}
    >
      <span className="relative z-10 flex items-center gap-1">
        {label}
        {hasMenu && <ChevronDown className="h-4 w-4 text-white/60" />}
      </span>

      {/* sharp white underline */}
      <span
        aria-hidden
        className={`pointer-events-none absolute bottom-[9px] left-1/2 h-[2px] w-full -translate-x-1/2 rounded-full bg-white transition-opacity duration-300 ${shown}`}
      />

      {/* blue light glow just below the underline */}
      <span
        aria-hidden
        className={`pointer-events-none absolute bottom-[2px] left-1/2 h-[6px] w-[125%] -translate-x-1/2 rounded-full bg-[#3b6bff] blur-[7px] transition-opacity duration-300 ${shown}`}
      />
      <span
        aria-hidden
        className={`pointer-events-none absolute bottom-[6px] left-1/2 h-[3px] w-[60%] -translate-x-1/2 rounded-full bg-[#7aa2ff] blur-[3px] transition-opacity duration-300 ${shown}`}
      />
    </Link>
  );
}
