import React from "react";

// Minimal, safe Markdown-subset renderer for article-style product descriptions. No dependency and no
// dangerouslySetInnerHTML — everything is turned into React elements, so stored content can't inject HTML.
// Supported: # / ## / ### headings, **bold**, *italic*, [text](url) links, ![alt](url) images, "- " lists
// and pipe tables.

function safeUrl(url: string): string | null {
  const u = url.trim();
  return /^https?:\/\//i.test(u) || u.startsWith("/") ? u : null;
}

// A heading may name its own anchor: "## آیفون و آیپد {#iphone}". The id is written by the author rather
// than derived from the heading text, because a Persian heading has no dependable slug — anything generated
// from it would have to be guessed at again on the linking side, and rewording a heading would silently
// break every link pointing at it.
const HEADING_ID = /\s*\{#([a-z0-9-]{1,64})\}\s*$/;

function splitHeading(raw: string): { label: string; id?: string } {
  const m = raw.match(HEADING_ID);
  return m ? { label: raw.slice(0, m.index), id: m[1] } : { label: raw };
}

// The links a long article uses to jump to its own sections. Separate from safeUrl because a fragment is
// a valid destination for a link but never for an image source.
function safeHref(url: string): string | null {
  const u = url.trim();
  return /^#[a-z0-9-]{1,64}$/.test(u) ? u : safeUrl(u);
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
      const href = safeHref(m[4]);
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
    // min-w-0 so the block can shrink inside a grid or flex parent; without it a wide table inside
    // pushes the whole column past the viewport instead of scrolling inside its own box.
    <div className={`min-w-0 space-y-4 text-sm leading-8 text-[var(--hl-ink-2)] ${className}`}>
      {blocks.map((block, bi) => {
        const imgOnly = block.match(/^!\[([^\]]*)\]\(([^)]+)\)$/);
        if (imgOnly) {
          const src = safeUrl(imgOnly[2]);
          return src ? <img loading="lazy" decoding="async" key={bi} src={src} alt={imgOnly[1]} className="mx-auto max-w-full rounded-xl border border-[var(--hl-border)]" /> : null;
        }
        // scroll-mt clears the sticky site header — without it a heading jumped to from the article's own
        // table of contents lands underneath it and looks like the link went to the wrong place.
        const heading = (tag: "h2" | "h3" | "h4", raw: string, size: string) => {
          const { label, id } = splitHeading(raw);
          const Tag = tag;
          return (
            <Tag key={bi} {...(id ? { id } : {})} className={`${size} scroll-mt-28 font-bold text-[var(--hl-ink)]`}>
              {renderInline(label, `h${bi}`)}
            </Tag>
          );
        };
        if (block.startsWith("### ")) return heading("h4", block.slice(4), "text-base");
        if (block.startsWith("## ")) return heading("h3", block.slice(3), "text-lg");
        if (block.startsWith("# ")) return heading("h2", block.slice(2), "text-xl");

        const lines = block.split("\n");

        const table = parseTable(lines);
        if (table) {
          return (
            // Comparison tables are wider than a phone; the scroll stays inside the table so the page
            // itself never scrolls sideways.
            // A comparison table is usually wider than a phone. The scroll has to be contained here, and
            // that only works if this box and everything above it may shrink below their content — a grid
            // or flex item defaults to min-width:auto and would otherwise push the whole column wide,
            // which is what dragged the article text outside its card on mobile. overscroll-contain keeps
            // a sideways drag on the table from turning into a page swipe.
            <div
              key={bi}
              className="ap-table-scroll w-full min-w-0 max-w-full overflow-x-auto overscroll-x-contain"
            >
              <table className="w-max min-w-full border-collapse text-right text-[13px]">
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
