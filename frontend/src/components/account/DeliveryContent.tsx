"use client";

import { useState } from "react";

// Renders admin-delivered order text (credentials, links, instructions) safely and legibly:
// - Preserves the original line structure (each line kept, blank lines become spacing).
// - Bidi-correct: every line uses dir="auto" so a standalone English/URL line left-aligns while
//   Persian lines stay right-aligned; embedded Latin/URLs are isolated so they never scramble the
//   surrounding Persian text.
// - URLs become clean, one-tap-copyable links (LTR-isolated) so they can be copied intact.
// - All colors come from theme tokens (--ac-*), so text stays readable in both light and dark themes.

function CopyButton({ text, label = "کپی" }: { text: string; label?: string }) {
  const [done, setDone] = useState(false);
  return (
    <button
      type="button"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(text);
          setDone(true);
          setTimeout(() => setDone(false), 1500);
        } catch {
          /* clipboard unavailable — ignore */
        }
      }}
      dir="rtl"
      className="shrink-0 rounded-md px-2 py-0.5 text-[11px] font-bold transition hover:brightness-105"
      style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: done ? "#059669" : "var(--ac-muted)" }}
      title="کپی"
    >
      {done ? "کپی شد ✓" : label}
    </button>
  );
}

const URL_RE = /(https?:\/\/[^\s<]+)/g;

// Persian/Arabic letters → the line reads right-to-left; a line with none (a bare URL or an
// English sentence) reads left-to-right and therefore left-aligns. Isolated inline URLs on an
// otherwise-Persian line keep the surrounding text intact.
const hasRtl = (s: string) => /[؀-ۿ]/.test(s);

function Line({ line }: { line: string }) {
  if (!line.trim()) return <div className="h-3" aria-hidden />;
  const parts = line.split(URL_RE);
  return (
    <p dir={hasRtl(line) ? "rtl" : "ltr"} className="leading-8" style={{ color: "var(--ac-text)", whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
      {parts.map((part, i) =>
        /^https?:\/\//.test(part) ? (
          <span key={i} className="mx-0.5 inline-flex max-w-full items-center gap-1 rounded-lg px-2 py-0.5 align-middle" style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)" }}>
            <a
              href={part}
              target="_blank"
              rel="noreferrer"
              dir="ltr"
              className="max-w-[min(70vw,420px)] truncate text-[13px] font-medium underline"
              style={{ color: "#3b82f6", unicodeBidi: "isolate" }}
            >
              {part}
            </a>
            <CopyButton text={part} />
          </span>
        ) : (
          <span key={i}>{part}</span>
        ),
      )}
    </p>
  );
}

export default function DeliveryContent({ content }: { content: string }) {
  const text = (content ?? "").replace(/\r\n/g, "\n");
  const lines = text.split("\n");
  return (
    <div className="space-y-0.5 text-sm">
      <div className="mb-2 flex justify-end">
        <CopyButton text={text} label="کپی همه" />
      </div>
      {lines.map((line, i) => (
        <Line key={i} line={line} />
      ))}
    </div>
  );
}
