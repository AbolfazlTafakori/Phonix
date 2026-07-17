"use client";

import { useState } from "react";
import type { OrderInputValue } from "@/lib/types";

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

function CopyRow({ label, value }: { label: string; value: string }) {
  const [done, setDone] = useState(false);
  if (!value) return null;
  return (
    <div className="flex items-center justify-between gap-2 rounded-lg px-3 py-2" style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)" }}>
      <div className="min-w-0">
        <div className="text-[11px]" style={{ color: "var(--ac-muted)" }}>{label}</div>
        <div dir="ltr" className="truncate text-sm font-bold" style={{ color: "var(--ac-text)", unicodeBidi: "isolate" }}>{value}</div>
      </div>
      <button
        type="button"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(value);
            setDone(true);
            setTimeout(() => setDone(false), 1500);
          } catch {
            /* clipboard unavailable — ignore */
          }
        }}
        className="shrink-0 rounded-md px-2 py-1 text-[11px] font-bold transition hover:brightness-105"
        style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)", color: done ? "#059669" : "var(--ac-muted)" }}
      >
        {done ? "کپی شد ✓" : "کپی"}
      </button>
    </div>
  );
}

export default function SeatDelivery({ seats, deviceInfo }: { seats: Seat[]; deviceInfo?: OrderInputValue[] }) {
  const [sel, setSel] = useState(0);
  if (seats.length === 0) return null;
  const active = seats[Math.min(sel, seats.length - 1)];
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

      {/* one button per seat; clicking reveals only that seat's info */}
      <div>
        <div className="mb-1.5 text-xs font-bold" style={{ color: "var(--ac-muted)" }}>صندلی‌ها</div>
        <div className="flex flex-wrap gap-2">
          {seats.map((s, i) => {
            const on = i === Math.min(sel, seats.length - 1);
            return (
              <button
                key={i}
                type="button"
                onClick={() => setSel(i)}
                dir="ltr"
                className={`rounded-lg px-3 py-1.5 text-sm font-bold transition ${on ? "ring-2 ring-[#FF5A1F]" : "hover:brightness-105"}`}
                style={{ background: on ? "rgba(255,90,31,0.12)" : "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: "var(--ac-text)" }}
              >
                {s.seatLabel}
              </button>
            );
          })}
        </div>
      </div>

      {/* the selected seat only */}
      <div className="space-y-2 rounded-xl p-3" style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)" }}>
        <div dir="ltr" className="text-sm font-bold" style={{ color: "var(--ac-title)", unicodeBidi: "isolate" }}>Seat {active.seatLabel}</div>
        <CopyRow label="نام کاربری" value={active.username} />
        <CopyRow label="گذرواژه" value={active.password} />
        <CopyRow label="پلن" value={active.plan} />
        {active.months ? <CopyRow label="مدت اشتراک" value={`${active.months} ماه`} /> : null}
        {deviceInfo && deviceInfo.length > 0 && (
          <div className="space-y-2 border-t pt-2" style={{ borderColor: "var(--ac-divider)" }}>
            <div className="text-[11px] font-bold" style={{ color: "var(--ac-muted)" }}>اطلاعات دستگاه</div>
            {deviceInfo.map((d, i) => (
              <CopyRow key={i} label={d.label} value={d.value} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
