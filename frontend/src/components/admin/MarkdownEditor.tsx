"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import AdminIcon from "./AdminIcon";

// Lightweight markdown editor for product descriptions and articles: a small toolbar that wraps/inserts
// markdown into a plain textarea, plus images that go in as ![](url). The storefront renders the result via
// <RichText>.
//
// An image can be added three ways: the toolbar button, pasting it (Ctrl+V — a screenshot, or "Copy image"
// in a browser), or dropping the file onto the text. While it uploads, a placeholder sits where it will go,
// so writing carries on and the picture lands exactly there.

// Image files on a clipboard or a drop, if any.
function imageFiles(data: DataTransfer | null): File[] {
  if (!data) return [];
  const files = Array.from(data.files ?? []).filter((f) => f.type.startsWith("image/"));
  if (files.length > 0) return files;
  return Array.from(data.items ?? [])
    .filter((i) => i.kind === "file" && i.type.startsWith("image/"))
    .map((i) => i.getAsFile())
    .filter((f): f is File => f !== null);
}

// A file's own name makes a usable alt text; the names clipboards and cameras invent ("image.png",
// "Screenshot 2026-…", "IMG_2041") say nothing about the picture, so those get none.
function altFor(file: File): string {
  const base = file.name.replace(/\.[^.]*$/, "").trim();
  return /^(image|screenshot|screen shot|img|photo|pasted|clipboard|unnamed)([\s_\-.(\d)]|$)/i.test(base) ? "" : base;
}

export default function MarkdownEditor({
  value,
  onChange,
  placeholder = "توضیحات را مثل یک مقاله بنویسید… می‌توانید بخش‌هایی را پررنگ کنید، تیتر بگذارید و بین متن عکس اضافه کنید.",
  rows = 8,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  rows?: number;
}) {
  const ref = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(0);
  const [error, setError] = useState("");
  const [dragging, setDragging] = useState(false);

  // Uploads finish after the user has kept typing, so each one edits the CURRENT text, not the text as it
  // was when it started — otherwise a finishing upload would undo everything written in the meantime.
  const latest = useRef(value);
  useEffect(() => {
    latest.current = value;
  }, [value]);
  function commit(next: string) {
    latest.current = next;
    onChange(next);
  }

  function wrap(before: string, after: string, placeholderText: string) {
    const el = ref.current;
    if (!el) return;
    const { selectionStart: s, selectionEnd: e } = el;
    const sel = value.slice(s, e) || placeholderText;
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

  // Tables have to sit on their own lines with a blank line before them, or the renderer reads them as
  // an ordinary paragraph and the pipes show up as text.
  function insertTable() {
    const el = ref.current;
    const pos = el?.selectionStart ?? value.length;
    const before = value.slice(0, pos);
    const lead = before === "" || before.endsWith("\n\n") ? "" : before.endsWith("\n") ? "\n" : "\n\n";
    const table =
      "| ستون ۱ | ستون ۲ | ستون ۳ |\n" +
      "| --- | --- | --- |\n" +
      "| مقدار | مقدار | مقدار |\n" +
      "| مقدار | مقدار | مقدار |\n";
    const after = value.slice(pos).startsWith("\n") ? "" : "\n";
    onChange(before + lead + table + after + value.slice(pos));
    requestAnimationFrame(() => {
      el?.focus();
      const caret = (before + lead).length;
      if (el) el.selectionStart = el.selectionEnd = caret;
    });
  }

  // Each image goes in as its own paragraph (the renderer only shows an image on a line of its own), first as
  // a placeholder that is swapped for the real link when its upload finishes — or removed if it fails.
  async function insertImages(files: File[]) {
    if (files.length === 0) return;
    setError("");
    const current = latest.current;
    const pos = Math.min(ref.current?.selectionStart ?? current.length, current.length);
    const tokens = files.map(() => `![در حال آپلود تصویر…](#uploading-${Math.random().toString(36).slice(2, 10)})`);
    const before = current.slice(0, pos);
    const lead = before === "" || before.endsWith("\n\n") ? "" : before.endsWith("\n") ? "\n" : "\n\n";
    const block = lead + tokens.join("\n\n") + "\n\n";
    commit(before + block + current.slice(pos));
    const caret = pos + block.length;
    requestAnimationFrame(() => {
      const el = ref.current;
      if (el) {
        el.focus();
        el.selectionStart = el.selectionEnd = caret;
      }
    });

    setUploading((n) => n + files.length);
    await Promise.all(
      files.map(async (file, i) => {
        try {
          const url = await api.media.upload(file);
          commit(latest.current.replace(tokens[i], `![${altFor(file)}](${url})`));
        } catch (e) {
          commit(latest.current.replace(`${tokens[i]}\n\n`, "").replace(tokens[i], ""));
          setError(e instanceof Error ? e.message : "آپلود تصویر ناموفق بود.");
        } finally {
          setUploading((n) => n - 1);
        }
      }),
    );
  }

  function onPaste(e: React.ClipboardEvent<HTMLTextAreaElement>) {
    const files = imageFiles(e.clipboardData);
    if (files.length === 0) return;
    // Word and similar apps put a picture of the copied text on the clipboard next to the text itself. When
    // there is text, the user meant the text.
    if (e.clipboardData.getData("text/plain").trim()) return;
    e.preventDefault();
    void insertImages(files);
  }

  function onDrop(e: React.DragEvent<HTMLTextAreaElement>) {
    setDragging(false);
    const files = imageFiles(e.dataTransfer);
    if (files.length === 0) return;
    e.preventDefault();
    void insertImages(files);
  }

  const btn = "rounded-lg border border-white/10 px-2.5 py-1.5 text-xs font-bold text-white/75 transition hover:bg-white/5 hover:text-white";

  return (
    <div className={`rounded-xl border bg-[#0d0d15] transition ${dragging ? "border-[#3a64f2]" : "border-white/10"}`}>
      <div className="flex flex-wrap items-center gap-1.5 border-b border-white/8 p-2">
        <button type="button" onClick={() => wrap("**", "**", "متن پررنگ")} className={`${btn} font-black`}>B</button>
        <button type="button" onClick={() => wrap("*", "*", "متن کج")} className={`${btn} italic`}>I</button>
        <button type="button" onClick={() => prefixLine("## ")} className={btn}>تیتر</button>
        <button type="button" onClick={() => prefixLine("- ")} className={btn}>• لیست</button>
        <button type="button" onClick={() => wrap("[", "](https://)", "متن لینک")} className={btn}>لینک</button>
        <button type="button" onClick={insertTable} className={btn}>جدول</button>
        <button type="button" onClick={() => fileRef.current?.click()} className={`${btn} flex items-center gap-1`}>
          <AdminIcon name="image" className="h-3.5 w-3.5" />
          {uploading > 0 ? "در حال آپلود…" : "افزودن عکس"}
        </button>
        <input
          ref={fileRef}
          type="file"
          accept="image/*"
          multiple
          className="hidden"
          onChange={(e) => {
            void insertImages(Array.from(e.target.files ?? []));
            e.target.value = "";
          }}
        />
      </div>
      <textarea
        ref={ref}
        value={value}
        onChange={(e) => commit(e.target.value)}
        onPaste={onPaste}
        onDragOver={(e) => {
          if (Array.from(e.dataTransfer.items ?? []).some((i) => i.kind === "file")) {
            e.preventDefault();
            setDragging(true);
          }
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        rows={rows}
        dir="rtl"
        placeholder={placeholder}
        className="block w-full resize-y bg-transparent px-4 py-3 text-sm leading-7 text-white outline-none placeholder:text-white/35"
      />
      {error && <p className="border-t border-white/8 px-4 py-2 text-xs text-rose-300">{error}</p>}
      <p className="border-t border-white/8 px-4 py-2 text-[11px] text-white/40">
        راهنما: <span className="font-mono">**پررنگ**</span> · <span className="font-mono">## تیتر</span> · <span className="font-mono">- مورد لیست</span> · <span className="font-mono">| جدول |</span> · عکس را می‌توانید مستقیم اینجا پیست (Ctrl+V) یا رها کنید.
      </p>
    </div>
  );
}
