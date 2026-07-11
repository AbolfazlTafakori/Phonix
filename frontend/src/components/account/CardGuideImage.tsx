"use client";

import { useState } from "react";

// The level-1 guide sample shown above the card-registration rules. If the team drops a real photo at
// /public/card-guide.jpg it is used; otherwise we render an inline Bank Mellat sample card so the buyer
// always sees an example of a correctly-photographed card (all fields visible).
// Drop the real sample photo at /public/card-guide.(webp|jpg|jpeg|png) — the first that exists is used.
// webp is tried first (smallest/fastest on the server); the rest are fallbacks for whatever you saved.
const SOURCES = ["/card-guide.webp", "/card-guide.jpg", "/card-guide.jpeg", "/card-guide.png"];

export default function CardGuideImage() {
  const [idx, setIdx] = useState(0);

  if (idx < SOURCES.length) {
    return (
      // eslint-disable-next-line @next/next/no-img-element
      <img loading="lazy" decoding="async"
        key={idx}
        src={SOURCES[idx]}
        alt="نمونه عکس کارت بانکی"
        // responsive but capped so the card preview never dominates the modal.
        className="mx-auto mb-4 block h-auto w-full max-w-[340px] rounded-xl border border-white/10"
        onError={() => setIdx((i) => i + 1)}
      />
    );
  }

  return (
    <div className="mx-auto mb-4 w-full max-w-[340px] overflow-hidden rounded-xl border border-white/10">
      <svg viewBox="0 0 380 240" className="block w-full" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="نمونه کارت بانکی" fontFamily="Vazirmatn, Tahoma, Arial, sans-serif">
        <defs>
          <linearGradient id="mlt" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#e7352c" />
            <stop offset="0.55" stopColor="#d4231d" />
            <stop offset="1" stopColor="#b81717" />
          </linearGradient>
          <clipPath id="mltClip">
            <rect x="0" y="0" width="380" height="240" rx="18" />
          </clipPath>
        </defs>

        <g clipPath="url(#mltClip)">
          <rect x="0" y="0" width="380" height="240" fill="url(#mlt)" />
          {/* white top band with a gentle dip */}
          <path d="M0 0 H380 V58 Q190 82 0 58 Z" fill="#ffffff" />
          {/* flowing wave lines */}
          <g stroke="#ffffff" strokeOpacity="0.2" fill="none" strokeWidth="1.2">
            <path d="M-20 165 C 70 120, 150 215, 250 150 S 410 125, 410 170" />
            <path d="M-20 182 C 70 137, 150 232, 250 167 S 410 142, 410 187" />
            <path d="M-20 148 C 80 108, 160 198, 270 138 S 410 110, 410 152" />
            <path d="M-20 200 C 90 158, 170 248, 280 185 S 410 160, 410 205" />
          </g>
        </g>

        {/* bank mellat (right) */}
        <text x="288" y="33" textAnchor="middle" fontSize="19" fontWeight="800" fill="#c2181a" style={{ direction: "rtl" }}>بانک ملت</text>
        <text x="288" y="47" textAnchor="middle" fontSize="9" fontWeight="700" fill="#8a8a8a" letterSpacing="0.5">bank mellat</text>
        <g transform="translate(334,15)">
          <rect x="0" y="0" width="21" height="21" rx="5" fill="#c2181a" />
          <path d="M5 15 L10.5 6 L16 15 Z" fill="#ffffff" />
          <circle cx="10.5" cy="14.5" r="2.2" fill="#ffd23f" />
        </g>

        {/* mellat card logo (top-left) with its label stacked underneath */}
        <rect x="48" y="11" width="28" height="18" rx="4" fill="#1f86c9" />
        <rect x="53" y="15" width="13" height="8" rx="2" fill="#cfe9f7" />
        <path d="M48 16 v8 l-7 -4 z" fill="#c2181a" />
        <text x="62" y="40" textAnchor="middle" fontSize="10.5" fontWeight="800" fill="#161616" style={{ direction: "rtl" }}>ملت کارت</text>
        <text x="62" y="49" textAnchor="middle" fontSize="8" fill="#666666">Mellat Card</text>

        {/* hashtag */}
        <text x="190" y="99" textAnchor="middle" fontSize="15" fontWeight="800" fill="#ffffff" style={{ direction: "rtl" }}>#به_احترام_هم</text>
        {/* sheba */}
        <text x="190" y="129" textAnchor="middle" fontSize="12.5" fontWeight="700" fill="#160505" letterSpacing="0.5">IR00 0000 0000 0000 0000 0000 00</text>
        {/* card number */}
        <text x="190" y="167" textAnchor="middle" fontSize="24" fontWeight="900" fill="#120303" letterSpacing="3">0000 0000 0000 0000</text>
        {/* name */}
        <text x="296" y="192" textAnchor="middle" fontSize="13" fontWeight="700" fill="#ffffff" style={{ direction: "rtl" }}>نام و نام خانوادگی</text>
        {/* expiry + cvv2 */}
        <text x="94" y="221" textAnchor="middle" fontSize="11.5" fontWeight="700" fill="#ffffff" style={{ direction: "rtl" }}>تاریخ انقضاء: **/*140</text>
        <text x="322" y="221" textAnchor="middle" fontSize="11.5" fontWeight="700" fill="#ffffff">CVV2: ****</text>
      </svg>
    </div>
  );
}
