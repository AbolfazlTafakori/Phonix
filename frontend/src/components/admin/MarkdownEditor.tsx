"use client";

import { useRef, useState } from "react";
import { api } from "@/lib/api";
import AdminIcon from "./AdminIcon";

// Lightweight markdown editor for product descriptions: a small toolbar that wraps/inserts markdown into a
// plain textarea, plus image upload that inserts ![](url). The storefront renders the result via <RichText>.
export default function MarkdownEditor({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const ref = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);

  function wrap(before: string, after: string, placeholder: string) {
    const el = ref.current;
    if (!el) return;
    const { selectionStart: s, selectionEnd: e } = el;
    const sel = value.slice(s, e) || placeholder;
    onChange(value.slice(0, s) + before + sel + after + value.slice(e));
    requestAnimationFrame(() => {
      el.focus();
      el.selectionStart = s + before.length;
      el.selectionEnd = s + before.length + sel.length;
    });
  }

  function prefixLine(prefix: string) {
    const el = ref.current;
    if (!el) return;
    const s = el.selectionStart;
    const lineStart = value.lastIndexOf("\n", s - 1) + 1;
    onChange(value.slice(0, lineStart) + prefix + value.slice(lineStart));
    requestAnimationFrame(() => {
      el.focus();
      el.selectionStart = el.selectionEnd = s + prefix.length;
    });
  }

  async function pickImage(file: File | undefined) {
    if (!file) return;
    setUploading(true);
    try {
      const url = await api.media.upload(file);
      const el = ref.current;
      const pos = el?.selectionStart ?? value.length;
      const snippet = `${value.slice(0, pos).endsWith("\n") || pos === 0 ? "" : "\n\n"}![](${url})\n\n`;
      onChange(value.slice(0, pos) + snippet + value.slice(pos));
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  }

  const btn = "rounded-lg border border-white/10 px-2.5 py-1.5 text-xs font-bold text-white/75 transition hover:bg-white/5 hover:text-white";

  return (
    <div className="rounded-xl border border-white/10 bg-[#0d0d15]">
      <div className="flex flex-wrap items-center gap-1.5 border-b border-white/8 p-2">
        <button type="button" onClick={() => wrap("**", "**", "متن پررنگ")} className={`${btn} font-black`}>B</button>
        <button type="button" onClick={() => wrap("*", "*", "متن کج")} className={`${btn} italic`}>I</button>
        <button type="button" onClick={() => prefixLine("## ")} className={btn}>تیتر</button>
        <button type="button" onClick={() => prefixLine("- ")} className={btn}>• لیست</button>
        <button type="button" onClick={() => wrap("[", "](https://)", "متن لینک")} className={btn}>لینک</button>
        <button type="button" onClick={() => fileRef.current?.click()} disabled={uploading} className={`${btn} flex items-center gap-1 disabled:opacity-50`}>
          <AdminIcon name="image" className="h-3.5 w-3.5" />
          {uploading ? "در حال آپلود…" : "افزودن عکس"}
        </button>
        <input ref={fileRef} type="file" accept="image/*" className="hidden" onChange={(e) => pickImage(e.target.files?.[0])} />
      </div>
      <textarea
        ref={ref}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        rows={8}
        dir="rtl"
        placeholder="توضیحات را مثل یک مقاله بنویسید… می‌توانید بخش‌هایی را پررنگ کنید، تیتر بگذارید و بین متن عکس اضافه کنید."
        className="block w-full resize-y bg-transparent px-4 py-3 text-sm leading-7 text-white outline-none placeholder:text-white/35"
      />
      <p className="border-t border-white/8 px-4 py-2 text-[11px] text-white/40">
        راهنما: <span className="font-mono">**پررنگ**</span> · <span className="font-mono">## تیتر</span> · <span className="font-mono">- مورد لیست</span> · عکس‌ها بین متن نمایش داده می‌شوند.
      </p>
    </div>
  );
}
