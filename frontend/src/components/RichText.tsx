import React from "react";

// Minimal, safe Markdown-subset renderer for article-style product descriptions. No dependency and no
// dangerouslySetInnerHTML — everything is turned into React elements, so stored content can't inject HTML.
// Supported: # / ## / ### headings, **bold**, *italic*, [text](url) links, ![alt](url) images, "- " lists
// and pipe tables.

function safeUrl(url: string): string | null {
  const u = url.trim();
  return /^https?:\/\//i.test(u) || u.startsWith("/") ? u : null;
}

// A pipe table is a header row, a `| --- | --- |` separator, then body rows. Without this, an
// admin-written comparison table rendered as literal "| پلن | کیفیت |" text on the page.
function parseTable(lines: string[]): { header: string[]; rows: string[][] } | null {
  const rows = lines.filter((l) => l.trim() !== "");
  if (rows.length < 2) return null;
  if (!rows.every((l) => l.trim().startsWith("|"))) return null;
  if (!/^\|[\s:|-]+\|$/.test(rows[1].trim()) || !rows[1].includes("-")) return null;

  const cells = (line: string) =>
    line.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());
  const header = cells(rows[0]);
  const body = rows.slice(2).map(cells).filter((r) => r.some((c) => c !== ""));
  if (header.length === 0 || body.length === 0) return null;
  return { header, rows: body };
}

function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
  const nodes: React.ReactNode[] = [];
  const re = /!\[([^\]]*)\]\(([^)]+)\)|\[([^\]]+)\]\(([^)]+)\)|\*\*([^*]+)\*\*|\*([^*]+)\*/g;
  let last = 0;
  let m: RegExpExecArray | null;
  let i = 0;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) nodes.push(text.slice(last, m.index));
    const key = `${keyPrefix}-${i++}`;
    if (m[1] !== undefined && m[2] !== undefined) {
      const src = safeUrl(m[2]);
      if (src) nodes.push(<img loading="lazy" decoding="async" key={key} src={src} alt={m[1]} className="my-2 inline-block max-h-32 rounded-lg align-middle" />);
    } else if (m[3] !== undefined && m[4] !== undefined) {
      const href = safeUrl(m[4]);
      // Links to our own pages stay in the tab — sending a reader to another page of the same shop in a
      // new window is disorienting, and the referrer suppression only makes sense for outbound links.
      const internal = href?.startsWith("/") ?? false;
      nodes.push(
        href ? (
          <a
            key={key}
            href={href}
            {...(internal ? {} : { target: "_blank", rel: "noreferrer" })}
            className="font-medium text-[var(--hl-red-text)] underline"
          >
            {m[3]}
          </a>
        ) : (
          m[3]
        ),
      );
    } else if (m[5] !== undefined) {
      nodes.push(<strong key={key} className="font-bold text-[var(--hl-ink)]">{m[5]}</strong>);
    } else if (m[6] !== undefined) {
      nodes.push(<em key={key}>{m[6]}</em>);
    }
    last = re.lastIndex;
  }
  if (last < text.length) nodes.push(text.slice(last));
  return nodes;
}

export default function RichText({ content, className = "" }: { content: string; className?: string }) {
  const text = (content ?? "").replace(/\r\n/g, "\n").trim();
  if (!text) return null;

  const blocks = text.split(/\n{2,}/);
  return (
    <div className={`space-y-4 text-sm leading-8 text-[var(--hl-ink-2)] ${className}`}>
      {blocks.map((block, bi) => {
        const imgOnly = block.match(/^!\[([^\]]*)\]\(([^)]+)\)$/);
        if (imgOnly) {
          const src = safeUrl(imgOnly[2]);
          return src ? <img loading="lazy" decoding="async" key={bi} src={src} alt={imgOnly[1]} className="mx-auto max-w-full rounded-xl border border-[var(--hl-border)]" /> : null;
        }
        if (block.startsWith("### ")) return <h4 key={bi} className="text-base font-bold text-[var(--hl-ink)]">{renderInline(block.slice(4), `h${bi}`)}</h4>;
        if (block.startsWith("## ")) return <h3 key={bi} className="text-lg font-bold text-[var(--hl-ink)]">{renderInline(block.slice(3), `h${bi}`)}</h3>;
        if (block.startsWith("# ")) return <h2 key={bi} className="text-xl font-bold text-[var(--hl-ink)]">{renderInline(block.slice(2), `h${bi}`)}</h2>;

        const lines = block.split("\n");

        const table = parseTable(lines);
        if (table) {
          return (
            // Comparison tables are wider than a phone; the scroll stays inside the table so the page
            // itself never scrolls sideways.
            <div key={bi} className="-mx-1 overflow-x-auto px-1">
              <table className="w-full min-w-[420px] border-collapse text-right text-[13px]">
                <thead>
                  <tr className="border-b-2 border-[var(--hl-border)]">
                    {table.header.map((h, hi) => (
                      <th key={hi} className="whitespace-nowrap px-3 py-2.5 font-bold text-[var(--hl-ink)]">
                        {renderInline(h, `th${bi}-${hi}`)}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {table.rows.map((r, ri) => (
                    <tr key={ri} className="border-b border-[var(--hl-border)] last:border-0">
                      {r.map((c, ci) => (
                        <td key={ci} className="px-3 py-2.5 align-top">{renderInline(c, `td${bi}-${ri}-${ci}`)}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          );
        }

        if (lines.some((l) => l.startsWith("- ")) && lines.every((l) => l.startsWith("- ") || l.trim() === "")) {
          return (
            <ul key={bi} className="list-disc space-y-1.5 pr-5">
              {lines.filter((l) => l.startsWith("- ")).map((l, li) => <li key={li}>{renderInline(l.slice(2), `l${bi}-${li}`)}</li>)}
            </ul>
          );
        }

        return (
          <p key={bi}>
            {lines.map((l, li) => (
              <React.Fragment key={li}>
                {li > 0 && <br />}
                {renderInline(l, `p${bi}-${li}`)}
              </React.Fragment>
            ))}
          </p>
        );
      })}
    </div>
  );
}
