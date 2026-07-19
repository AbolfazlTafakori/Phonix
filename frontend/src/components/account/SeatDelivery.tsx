"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { OrderInputValue, SeatUnitInfo } from "@/lib/types";
import SeatInfoForm from "./SeatInfoForm";

// Seat-based view of a shared-account delivery. The delivered text is one self-contained block per seat (see
// StockFulfillmentService.BuildSlotDeliveryContent); this parses those blocks and shows the account header once,
// a button per seat, and — only for the selected seat — that seat's own credentials. When the content isn't the
// seat format (legacy/plain deliveries) parsing yields no seats and the caller falls back to the raw renderer.
//
// Every value shown here comes from the customer's own order (server-side owner-scoped), so a customer only ever
// sees the seats assigned to them — never other seats, customers, accounts, or remaining inventory.

export type Seat = {
  service: string;
  username: string;
  password: string;
  plan: string;
  months: string;
  seatLabel: string;
};

const isDivider = (s: string) => /^\s*[─—-]{3,}\s*$/.test(s);

function parseBlock(block: string): Seat | null {
  const lines = block.split("\n").map((l) => l.trim()).filter(Boolean);
  if (lines.length === 0) return null;
  const header = lines[0];
  const users = lines.filter((x) => /^user\s*:/i.test(x)).map((x) => x.slice(x.indexOf(":") + 1).trim());
  const one = (label: string) => {
    const l = lines.find((x) => new RegExp(`^${label}\\s*:`, "i").test(x));
    return l ? l.slice(l.indexOf(":") + 1).trim() : "";
  };
  const months = header.match(/(\d+)\s*Month/i)?.[1] ?? "";
  const service = header.replace(/\s*\d+\s*Connection.*/i, "").trim();
  return {
    service,
    username: users[0] ?? "",
    password: one("Pass"),
    plan: one("Plan"),
    months,
    // "User :" appears twice per block — the account username first, then the seat label last.
    seatLabel: users.length > 1 ? users[users.length - 1] : "",
  };
}

export function parseSeats(content: string): Seat[] {
  const lines = (content ?? "").replace(/\r\n/g, "\n").split("\n");
  const blocks: string[][] = [[]];
  for (const line of lines) {
    if (isDivider(line)) blocks.push([]);
    else blocks[blocks.length - 1].push(line);
  }
  return blocks
    .map((b) => b.join("\n").trim())
    .filter(Boolean)
    .map(parseBlock)
    .filter((s): s is Seat => s !== null && s.seatLabel !== "");
}

// One credential/detail row. No separate copy button — the whole row IS the control: a tap copies the value.
// `sensitive` rows (the password) stay masked at rest so a stray screenshot of the page never exposes them;
// a tap reveals the real value just long enough to read/paste, then it re-masks itself automatically.
const REVEAL_MS = 4000;

function InfoRow({ label, value, sensitive = false }: { label: string; value: string; sensitive?: boolean }) {
  const [revealed, setRevealed] = useState(false);
  const [copied, setCopied] = useState(false);

  // Never leaves a sensitive value exposed — it re-masks on its own even if the customer never taps again.
  useEffect(() => {
    if (!revealed) return;
    const t = setTimeout(() => setRevealed(false), REVEAL_MS);
    return () => clearTimeout(t);
  }, [revealed]);

  if (!value) return null;
  const masked = "•".repeat(Math.min(Math.max(value.length, 8), 14));
  const showPlain = !sensitive || revealed;

  async function handleClick() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1200);
    } catch {
      /* clipboard unavailable — reveal still works without it */
    }
    if (sensitive) setRevealed((r) => !r);
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      dir="ltr"
      className="flex w-full flex-col items-start gap-0.5 rounded-lg px-3 py-2 text-left transition hover:brightness-105"
      style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)" }}
    >
      <span className="text-[11px]" style={{ color: "var(--ac-muted)" }}>
        {label}
        {copied ? " · کپی شد ✓" : sensitive && !revealed ? " · برای نمایش و کپی لمس کنید" : ""}
      </span>
      <span
        className="truncate text-sm font-bold"
        style={{ color: "var(--ac-text)", unicodeBidi: "isolate", letterSpacing: sensitive && !showPlain ? "2px" : undefined }}
      >
        {showPlain ? value : masked}
      </span>
    </button>
  );
}

export default function SeatDelivery({
  seats,
  deviceInfo,
  orderId,
  unitId,
}: {
  seats: Seat[];
  deviceInfo?: OrderInputValue[];
  // Present only when the caller knows which delivered unit these seats came from — that's what lets each
  // seat collect its own info. Omitted (e.g. a preview), the form simply isn't offered.
  orderId?: number;
  unitId?: number;
}) {
  const [sel, setSel] = useState(0);
  // Whether this service asks for per-seat info, and what's already been filed, keyed by seat index. Loaded
  // once per unit; a save updates the entry in place so switching seats never refetches.
  const [info, setInfo] = useState<SeatUnitInfo | null>(null);
  useEffect(() => {
    if (orderId === undefined || unitId === undefined) return;
    let alive = true;
    api.seatInfo
      .forUnit(orderId, unitId)
      .then((r) => { if (alive) setInfo(r); })
      .catch(() => { /* the panel still shows the credentials if this fails */ });
    return () => { alive = false; };
  }, [orderId, unitId]);

  if (seats.length === 0) return null;
  const activeIndex = Math.min(sel, seats.length - 1);
  const active = seats[activeIndex];
  const service = seats.find((s) => s.service)?.service ?? "";
  const months = seats.find((s) => s.months)?.months ?? "";

  return (
    <div className="space-y-3">
      {/* account header — shown once for the whole shared account */}
      <div dir="ltr" className="text-sm" style={{ color: "var(--ac-text)", unicodeBidi: "isolate" }}>
        <div className="font-bold" style={{ color: "var(--ac-title)" }}>{service}</div>
        <div style={{ color: "var(--ac-muted)" }}>
          {seats.length} Connection{seats.length > 1 ? "s" : ""}
          {months ? ` · ${months} Month` : ""}
        </div>
      </div>

      {/* one button per profile; clicking reveals only that profile's info. dir="ltr" on the row itself is what
          keeps the buttons in reading order (A0, A1, A2 left→right) — without it the RTL page direction would
          lay the flex row out right-to-left and the labels would appear reversed. */}
      <div>
        <div className="mb-1.5 text-xs font-bold" style={{ color: "var(--ac-muted)" }}>پروفایل‌ها</div>
        <div dir="ltr" className="flex flex-wrap justify-start gap-2">
          {seats.map((s, i) => {
            const on = i === Math.min(sel, seats.length - 1);
            return (
              <button
                key={i}
                type="button"
                onClick={() => setSel(i)}
                className={`rounded-lg px-3 py-1.5 text-sm font-bold transition ${on ? "ring-2 ring-[#FF5A1F]" : "hover:brightness-105"}`}
                style={{ background: on ? "rgba(255,90,31,0.12)" : "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: "var(--ac-text)" }}
              >
                {s.seatLabel}
              </button>
            );
          })}
        </div>
      </div>

      {/* the selected profile only */}
      <div className="space-y-2 rounded-xl p-3" style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)" }}>
        {/* label (right) + seat code (left) — justify-between on the page's own RTL flow puts the first child
            at the start (right) and the last at the end (left), so this needs no dir override on the row. */}
        <div className="flex items-baseline justify-between text-sm font-bold" style={{ color: "var(--ac-title)" }}>
          <span>پروفایل</span>
          <span dir="ltr" style={{ unicodeBidi: "isolate" }}>{active.seatLabel}</span>
        </div>
        {/* username (right) + password (left) as a matched pair — page direction is RTL, so the first item in
            DOM order lands on the right without needing an explicit dir override on this row. */}
        <div className="grid grid-cols-1 gap-2 min-[380px]:grid-cols-2">
          <InfoRow label="نام کاربری" value={active.username} />
          <InfoRow label="گذرواژه" value={active.password} sensitive />
        </div>
        {/* plan (right) + duration (left), same paired layout — only when there's a duration to pair with,
            otherwise the plan renders alone so it never leaves an empty cell beside it. */}
        {active.months ? (
          <div className="grid grid-cols-1 gap-2 min-[380px]:grid-cols-2">
            <InfoRow label="پلن" value={active.plan} />
            <InfoRow label="مدت اشتراک" value={`${active.months} ماه`} />
          </div>
        ) : (
          <InfoRow label="پلن" value={active.plan} />
        )}
        {/* per-seat submission — scoped to the profile selected above, so on a multi-seat purchase each person
            files their own picture and note without touching anyone else's */}
        {info?.enabled && orderId !== undefined && unitId !== undefined && (
          <SeatInfoForm
            key={activeIndex}
            orderId={orderId}
            unitId={unitId}
            seatIndex={activeIndex}
            seatLabel={active.seatLabel}
            hint={info.hint}
            submission={info.submissions.find((s) => s.seatIndex === activeIndex)}
            onSaved={(saved) =>
              setInfo((prev) =>
                prev === null
                  ? prev
                  : {
                      ...prev,
                      submissions: [...prev.submissions.filter((s) => s.seatIndex !== saved.seatIndex), saved],
                    },
              )
            }
          />
        )}
        {deviceInfo && deviceInfo.length > 0 && (
          <div className="space-y-2 border-t pt-2" style={{ borderColor: "var(--ac-divider)" }}>
            <div className="text-[11px] font-bold" style={{ color: "var(--ac-muted)" }}>اطلاعات دستگاه</div>
            {deviceInfo.map((d, i) => (
              <InfoRow key={i} label={d.label} value={d.value} sensitive={d.sensitive} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
