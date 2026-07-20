"use client";

import { useMemo } from "react";

// Renders one email body — the single most security-sensitive surface in the panel.
//
// The HTML was already run through an allowlist sanitizer server-side; this is the SECOND, independent layer:
// a sandboxed iframe with an empty `sandbox` attribute (no allow-scripts, no allow-same-origin), plus a CSP
// inside the document that blocks every network fetch. Even a sanitizer bypass lands in an origin that can
// run nothing and reach nothing.
//
// It renders on WHITE with dark text on purpose. Email HTML is authored for white backgrounds — senders set
// dark text and assume a light canvas — so dropping it onto the panel's dark theme makes half of it
// invisible (the "why is it blank/white" problem). Every serious mail client (Gmail, Outlook) renders the
// body on white for exactly this reason; matching them is the correct, predictable choice.
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
  html,body{margin:0;padding:14px;background:#ffffff;color:#1a1a2e;
    font:14px/1.9 system-ui,-apple-system,"Segoe UI",Tahoma,sans-serif;word-break:break-word;overflow-wrap:anywhere}
  a{color:#1733d6}
  img{max-width:100%;height:auto}
  table{max-width:100%;border-collapse:collapse}
  blockquote{margin:0 0 0 .8rem;padding-right:.8rem;border-right:3px solid #d4d4e4;color:#5a5a6e}
  pre{white-space:pre-wrap}
</style></head><body>${html}</body></html>`;
  }, [html]);

  if (html) {
    return (
      <iframe
        // Empty sandbox = maximum restriction the platform offers. Never add allow-* tokens here.
        sandbox=""
        srcDoc={srcDoc}
        title="متن ایمیل"
        className={`w-full rounded-xl border border-black/5 bg-white ${className || "h-[52vh]"}`}
      />
    );
  }

  if (text) {
    // Plain-text bodies get the same white reading surface, so a thread that mixes HTML and plain messages
    // looks consistent rather than flipping between light and dark panels.
    return (
      <pre
        dir="auto"
        className={`overflow-auto whitespace-pre-wrap break-words rounded-xl border border-black/5 bg-white px-4 py-3 font-sans text-sm leading-8 text-[#1a1a2e] ${className}`}
      >
        {text}
      </pre>
    );
  }

  return <p className="rounded-xl bg-white/[0.03] px-4 py-6 text-center text-sm text-white/35">این ایمیل متنی ندارد.</p>;
}
