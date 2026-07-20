"use client";

import { useMemo } from "react";

// Renders one email body — the single most security-sensitive surface in the panel.
//
// The HTML was already run through an allowlist sanitizer server-side; this is the SECOND, independent layer:
// a sandboxed iframe with an empty `sandbox` attribute (no allow-scripts, no allow-same-origin), plus a CSP
// inside the document that blocks every network fetch. Even a sanitizer bypass lands in an origin that can
// run nothing and reach nothing.
//
// It renders on the panel's DARK theme with light text, so the body blends into the dark UI instead of
// flashing a white card. The default text color is set light for us to fully control plain-text and any HTML
// that does not set its own colors. HTML that DOES carry its own background/colors (marketing templates)
// still renders as authored — we deliberately do not rewrite an arbitrary email's inline colors, because
// forcing them can make an author's own dark-on-light text invisible; those emails keep their own look.
export default function MailBody({
  html,
  text,
  className = "",
}: {
  html: string;
  text: string;
  className?: string;
}) {
  const srcDoc = useMemo(() => {
    if (!html) return "";
    return `<!doctype html><html dir="auto"><head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src 'none'; font-src 'none'; script-src 'none'; frame-src 'none'; connect-src 'none'; form-action 'none'; base-uri 'none'">
<style>
  html,body{margin:0;padding:14px;background:transparent;color:#dcdce6;
    font:14px/1.9 system-ui,-apple-system,"Segoe UI",Tahoma,sans-serif;word-break:break-word;overflow-wrap:anywhere}
  a{color:#8aa6ff}
  img{max-width:100%;height:auto}
  table{max-width:100%;border-collapse:collapse}
  blockquote{margin:0 0 0 .8rem;padding-right:.8rem;border-right:2px solid rgba(255,255,255,.15);color:#a9a9bb}
  pre{white-space:pre-wrap}
</style></head><body>${html}</body></html>`;
  }, [html]);

  if (html) {
    return (
      <iframe
        // Empty sandbox = maximum restriction the platform offers. Never add allow-* tokens here.
        // allow-transparency lets the dark page behind show through when the email sets no background.
        sandbox=""
        srcDoc={srcDoc}
        title="متن ایمیل"
        className={`w-full rounded-xl border border-white/8 bg-transparent ${className || "h-[52vh]"}`}
      />
    );
  }

  if (text) {
    return (
      <pre
        dir="auto"
        className={`overflow-auto whitespace-pre-wrap break-words rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3 font-sans text-sm leading-8 text-white/80 ${className}`}
      >
        {text}
      </pre>
    );
  }

  return <p className="rounded-xl bg-white/[0.03] px-4 py-6 text-center text-sm text-white/35">این ایمیل متنی ندارد.</p>;
}
