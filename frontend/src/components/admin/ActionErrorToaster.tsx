"use client";

import { useEffect, useState } from "react";

// Safety net for panel actions whose handler awaits an API call inside try/finally without a catch. The API
// client throws a readable Persian message on every non-2xx reply, but with nothing catching it the rejection
// went nowhere: the spinner stopped and the button looked simply dead, which is the worst possible failure on
// a panel someone is using to move real money. Listening for unhandled rejections surfaces that message
// wherever it happens, including in code written later, without touching two dozen call sites.
//
// Panel-only on purpose: these strings are operator-facing ("مقدار نامعتبر است."), not something a customer
// should be shown raw. The public flows that matter (checkout, login) already catch their own errors.
type Toast = { id: number; message: string };

const AUTO_DISMISS_MS = 8000;
const MAX_VISIBLE = 3;

// Rejections that are normal browser behavior rather than a failed action: a fetch cancelled because the
// operator navigated away, and Next's own router aborts. Surfacing these would train people to ignore the
// toast, which would defeat the point.
function isNoise(reason: unknown): boolean {
  if (reason instanceof DOMException && reason.name === "AbortError") return true;
  const msg = reason instanceof Error ? reason.message : String(reason ?? "");
  return !msg.trim() || /abort|cancell?ed|NEXT_REDIRECT|NEXT_NOT_FOUND/i.test(msg);
}

export default function ActionErrorToaster() {
  const [toasts, setToasts] = useState<Toast[]>([]);

  useEffect(() => {
    let seq = 0;
    function onRejection(event: PromiseRejectionEvent) {
      if (isNoise(event.reason)) return;
      const message = event.reason instanceof Error ? event.reason.message : String(event.reason);
      const id = ++seq;
      setToasts((prev) => {
        // Clicking a broken button twice should not stack the same line twice.
        if (prev.some((t) => t.message === message)) return prev;
        return [...prev, { id, message }].slice(-MAX_VISIBLE);
      });
      window.setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), AUTO_DISMISS_MS);
    }
    window.addEventListener("unhandledrejection", onRejection);
    return () => window.removeEventListener("unhandledrejection", onRejection);
  }, []);

  if (toasts.length === 0) return null;

  return (
    // pointer-events-none on the wrapper so an empty or lingering toast area can never swallow a click meant
    // for the page underneath; the toasts themselves take pointer events back.
    <div className="pointer-events-none fixed bottom-5 left-5 z-[100] flex max-w-[min(92vw,26rem)] flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          role="alert"
          className="pointer-events-auto flex items-start gap-3 rounded-xl border border-rose-500/30 bg-[#2a1417] px-4 py-3 shadow-lg shadow-black/40"
        >
          <span className="mt-0.5 shrink-0 text-rose-400">⚠</span>
          <p className="min-w-0 flex-1 text-xs leading-6 text-rose-100">{t.message}</p>
          <button
            onClick={() => setToasts((prev) => prev.filter((x) => x.id !== t.id))}
            aria-label="بستن"
            className="shrink-0 text-white/40 transition hover:text-white"
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  );
}
