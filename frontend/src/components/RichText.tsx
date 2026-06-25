import React from "react";

// Minimal, safe Markdown-subset renderer for article-style product descriptions. No dependency and no
// dangerouslySetInnerHTML — everything is turned into React elements, so stored content can't inject HTML.
// Supported: # / ## / ### headings, **bold**, *italic*, [text](url) links, ![alt](url) images, and "- " lists.

function safeUrl(url: string): string | null {
  const u = url.trim();
  return /^https?:\/\//i.test(u) || u.startsWith("/") ? u : null;
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
      if (src) nodes.push(<img key={key} src={src} alt={m[1]} className="my-2 inline-block max-h-32 rounded-lg align-middle" />);
    } else if (m[3] !== undefined && m[4] !== undefined) {
      const href = safeUrl(m[4]);
      nodes.push(href ? <a key={key} href={href} target="_blank" rel="noreferrer" className="text-[#6f93ff] underline">{m[3]}</a> : m[3]);
    } else if (m[5] !== undefined) {
      nodes.push(<strong key={key} className="font-bold text-white">{m[5]}</strong>);
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
    <div className={`space-y-4 text-sm leading-8 text-white/75 ${className}`}>
      {blocks.map((block, bi) => {
        const imgOnly = block.match(/^!\[([^\]]*)\]\(([^)]+)\)$/);
        if (imgOnly) {
          const src = safeUrl(imgOnly[2]);
          return src ? <img key={bi} src={src} alt={imgOnly[1]} className="mx-auto max-w-full rounded-xl border border-white/10" /> : null;
        }
        if (block.startsWith("### ")) return <h4 key={bi} className="text-base font-bold text-white">{renderInline(block.slice(4), `h${bi}`)}</h4>;
        if (block.startsWith("## ")) return <h3 key={bi} className="text-lg font-bold text-white">{renderInline(block.slice(3), `h${bi}`)}</h3>;
        if (block.startsWith("# ")) return <h2 key={bi} className="text-xl font-bold text-white">{renderInline(block.slice(2), `h${bi}`)}</h2>;

        const lines = block.split("\n");
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
