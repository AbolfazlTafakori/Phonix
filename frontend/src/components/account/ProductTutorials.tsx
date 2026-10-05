"use client";

import { useState } from "react";
import RichText from "@/components/RichText";
import type { Tutorial } from "@/lib/types";
import { toFa } from "@/lib/format";

// The how-to guides for one purchased product, under its service details in the buyer's orders. Each opens in
// place; the first is open from the start when it's the only one, so a single guide isn't hidden behind a click.
export default function ProductTutorials({ tutorials }: { tutorials: Tutorial[] }) {
  const [open, setOpen] = useState<number | null>(tutorials.length === 1 ? tutorials[0].id : null);
  if (tutorials.length === 0) return null;

  return (
    <div className="rounded-xl p-4" style={{ background: "rgba(58,100,242,0.06)", border: "1px solid rgba(58,100,242,0.25)" }}>
      <p className="mb-3 flex items-center gap-2 text-sm font-bold" style={{ color: "var(--ac-title)" }}>
        <svg viewBox="0 0 24 24" aria-hidden="true" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M2 4h7a3 3 0 0 1 3 3v13a2 2 0 0 0-2-2H2zM22 4h-7a3 3 0 0 0-3 3v13a2 2 0 0 1 2-2h8z" />
        </svg>
        آموزش استفاده از این سرویس
      </p>
      <div className="space-y-2">
        {tutorials.map((t) => {
          const isOpen = open === t.id;
          return (
            <div key={t.id} className="overflow-hidden rounded-lg" style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)" }}>
              <button
                type="button"
                onClick={() => setOpen(isOpen ? null : t.id)}
                aria-expanded={isOpen}
                className="flex w-full items-center justify-between gap-3 px-4 py-3 text-right text-sm font-bold transition hover:bg-[color:var(--ac-menu-hover)]"
                style={{ color: "var(--ac-text)" }}
              >
                <span className="min-w-0 flex-1">{t.title}</span>
                {t.videos.length > 0 && (
                  <span className="shrink-0 rounded-md px-2 py-0.5 text-[11px]" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>
                    {toFa(t.videos.length)} ویدیو
                  </span>
                )}
                <span className={`shrink-0 transition-transform ${isOpen ? "" : "-rotate-90"}`} style={{ color: "var(--ac-muted)" }}>▾</span>
              </button>
              {isOpen && (
                <div className="space-y-4 border-t px-4 py-4" style={{ borderColor: "var(--ac-divider)" }}>
                  {t.videos.map((v) => (
                    <figure key={v.id} className="space-y-1.5">
                      {/* preload="metadata": the player shows length and first frame without pulling the whole
                          file down until the buyer presses play. */}
                      <video
                        src={v.url}
                        controls
                        preload="metadata"
                        playsInline
                        controlsList="nodownload"
                        onContextMenu={(e) => e.preventDefault()}
                        className="w-full rounded-lg bg-black"
                      />
                      {v.name && (
                        <figcaption className="text-xs" style={{ color: "var(--ac-muted)" }}>{v.name}</figcaption>
                      )}
                    </figure>
                  ))}
                  {t.body.trim() && <RichText content={t.body} className="text-sm leading-8" />}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
