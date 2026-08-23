"use client";

import { useState } from "react";

// Renders admin-delivered order text (credentials, links, instructions) safely and legibly:
// - Preserves the original line structure (each line kept, blank lines become spacing).
// - Bidi-correct: each line picks its own direction so a standalone English/URL line left-aligns while
//   Persian lines stay right-aligned, and every Latin run inside a Persian line is isolated so it reads
//   exactly as it was written. That isolation is not cosmetic: trailing punctuation is bidi-neutral, so
//   on a line like "رمز: Mienbac360@@@@" the @@@@ takes the line's right-to-left direction and is drawn
//   to the LEFT of the password. It reads as "@@@@Mienbac360" — which is what a customer then types, and
//   why they cannot log in with a password that was delivered correctly.
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

// A run of Latin letters/digits plus any ASCII punctuation hanging off it — credentials, emails, codes,
// keys. Whitespace ends a run, so words stay separate and only the run itself is isolated.
const LATIN_RUN_RE = /([A-Za-z0-9][!-~]*)/g;

// <bdi> is the element for exactly this: it isolates its contents from the surrounding bidi context, so a
// password keeps its own internal order and its trailing punctuation stays where it was typed.
function isolateLatin(text: string, keyPrefix: string) {
  // split() with a capturing group alternates separator/match chunks, and a matched run always starts with
  // a letter or digit — testing that is enough. Reusing LATIN_RUN_RE.test() here would not be: a /g regex
  // carries lastIndex between calls and would answer differently for the same input.
  return text.split(LATIN_RUN_RE).map((chunk, i) =>
    /^[A-Za-z0-9]/.test(chunk)
      ? <bdi key={`${keyPrefix}-${i}`} dir="ltr">{chunk}</bdi>
      : <span key={`${keyPrefix}-${i}`}>{chunk}</span>,
  );
}

// A run of box-drawing/dashes on its own line is a block separator (see StockFulfillmentService.SeatDivider).
const isDivider = (s: string) => /^[\s]*[─—-]{3,}[\s]*$/.test(s);

function Line({ line, bold }: { line: string; bold?: boolean }) {
  if (isDivider(line)) return <hr className="my-2 border-0 border-t" style={{ borderColor: "var(--ac-panel-border)" }} />;
  if (!line.trim()) return <div className="h-3" aria-hidden />;
  const parts = line.split(URL_RE);
  return (
    <p dir={hasRtl(line) ? "rtl" : "ltr"} className={`leading-8 ${bold ? "font-bold" : ""}`} style={{ color: "var(--ac-text)", whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
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
          <span key={i}>{isolateLatin(part, String(i))}</span>
        ),
      )}
    </p>
  );
}

export default function DeliveryContent({ content }: { content: string }) {
  const text = (content ?? "").replace(/\r\n/g, "\n");
  const lines = text.split("\n");
  // Bold the first non-empty line of each block: the very start, and the first line after every divider.
  let expectHeader = true;
  const bold = lines.map((line) => {
    if (isDivider(line)) { expectHeader = true; return false; }
    if (expectHeader && line.trim()) { expectHeader = false; return true; }
    return false;
  });
  return (
    <div className="space-y-0.5 text-sm">
      <div className="mb-2 flex justify-end">
        <CopyButton text={text} label="کپی همه" />
      </div>
      {lines.map((line, i) => (
        <Line key={i} line={line} bold={bold[i]} />
      ))}
    </div>
  );
}
