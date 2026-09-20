"use client";

import { InfoRow } from "./SeatDelivery";

// The delivery of a panel-provisioned service (V2Ray / WireGuard), laid out like a shared-account seat —
// one tile per field, tap to copy — instead of the raw lines the fulfilment service writes. The text it
// parses is "label: value" per line (see V2RayFulfillmentService.DeliveryText and its WireGuard twin); a
// line without a colon is a note (the renewal's "nothing changed") and is shown above the tiles.
//
// Long values — the subscription link, the UUID — get the full width so they never truncate beside a
// short one; the rest pair up two per row.

type Field = { label: string; value: string };

const WIDE = /لینک|شناسه|uuid|url/i;

export function parsePanelDelivery(content: string): { notes: string[]; fields: Field[] } {
  const notes: string[] = [];
  const fields: Field[] = [];
  for (const raw of (content ?? "").replace(/\r\n/g, "\n").split("\n")) {
    const line = raw.trim();
    if (!line) continue;
    const at = line.indexOf(":");
    // A URL's own "https:" must not be mistaken for the separator: the label is the part before the FIRST
    // colon only when that part carries no scheme.
    if (at > 0 && !/^https?$/i.test(line.slice(0, at).trim())) {
      fields.push({ label: line.slice(0, at).trim(), value: line.slice(at + 1).trim() });
    } else {
      notes.push(line);
    }
  }
  return { notes, fields };
}

export default function PanelDelivery({ content }: { content: string }) {
  const { notes, fields } = parsePanelDelivery(content);
  if (fields.length === 0 && notes.length === 0) return null;

  const wide = fields.filter((f) => WIDE.test(f.label));
  const narrow = fields.filter((f) => !WIDE.test(f.label));

  return (
    <div className="space-y-2">
      {notes.map((n, i) => (
        <p key={i} className="rounded-lg px-3 py-2 text-[12px] leading-6" style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: "var(--ac-text)" }}>
          {n}
        </p>
      ))}
      {narrow.length > 0 && (
        <div className="grid grid-cols-1 gap-2 min-[380px]:grid-cols-2">
          {narrow.map((f) => <InfoRow key={f.label} label={f.label} value={f.value} />)}
        </div>
      )}
      {wide.map((f) => <InfoRow key={f.label} label={f.label} value={f.value} />)}
    </div>
  );
}
