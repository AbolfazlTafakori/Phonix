"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { MediaItem } from "@/lib/types";
import { formatNumber, toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

// The image library: every picture staff have uploaded, with a link to copy. An image for an article, a banner
// or a page is uploaded here and linked — no commit and deploy for each new picture.

function sizeLabel(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${toFa((bytes / (1024 * 1024)).toFixed(1))} مگابایت`;
  return `${formatNumber(Math.max(1, Math.round(bytes / 1024)))} کیلوبایت`;
}

const dateLabel = (iso: string) => new Date(iso).toLocaleDateString("fa-IR", { year: "numeric", month: "2-digit", day: "2-digit" });

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

type Filter = "all" | "library" | "other";

export default function AdminMediaPage() {
  const [items, setItems] = useState<MediaItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [uploading, setUploading] = useState(0);
  const [uploadError, setUploadError] = useState("");
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [copied, setCopied] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [preview, setPreview] = useState<MediaItem | null>(null);
  const [dragging, setDragging] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    api.mediaLibrary
      .list()
      .then((list) => { if (!cancelled) setItems(list); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : "خطا در بارگذاری"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  async function upload(files: File[]) {
    if (files.length === 0) return;
    setUploadError("");
    setUploading((n) => n + files.length);
    await Promise.all(
      files.map(async (file) => {
        try {
          const item = await api.mediaLibrary.upload(file);
          setItems((prev) => [item, ...prev]);
        } catch (e) {
          setUploadError(`«${file.name}»: ${e instanceof Error ? e.message : "آپلود ناموفق بود."}`);
        } finally {
          setUploading((n) => n - 1);
        }
      }),
    );
  }

  // Ctrl+V anywhere on the page uploads a copied image — unless the user is typing in the search box.
  useEffect(() => {
    function onPaste(e: ClipboardEvent) {
      if ((e.target as HTMLElement | null)?.closest("input, textarea")) return;
      const files = imageFiles(e.clipboardData);
      if (files.length === 0) return;
      e.preventDefault();
      void upload(files);
    }
    window.addEventListener("paste", onPaste);
    return () => window.removeEventListener("paste", onPaste);
  }, []);

  async function copy(text: string, key: string) {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(key);
      setTimeout(() => setCopied((c) => (c === key ? null : c)), 1800);
    } catch {
      window.prompt("کپی کنید:", text);
    }
  }

  async function remove(item: MediaItem) {
    if (!confirm(`تصویر «${item.name || "بدون نام"}» برای همیشه حذف شود؟`)) return;
    setBusyId(item.id);
    try {
      await api.mediaLibrary.remove(item.id);
      setItems((prev) => prev.filter((i) => i.id !== item.id));
      setPreview((p) => (p?.id === item.id ? null : p));
    } catch (e) {
      alert(e instanceof Error ? e.message : "حذف تصویر ناموفق بود.");
    } finally {
      setBusyId(null);
    }
  }

  const shown = useMemo(() => {
    const q = search.trim().toLowerCase();
    return items.filter(
      (i) =>
        (filter === "all" || (filter === "library") === i.inLibrary) &&
        (!q || i.name.toLowerCase().includes(q) || i.uploadedBy.toLowerCase().includes(q)),
    );
  }, [items, search, filter]);

  const absolute = (url: string) => (typeof window === "undefined" ? url : `${window.location.origin}${url}`);
  const filterBtn = (f: Filter, label: string) => (
    <button
      type="button"
      onClick={() => setFilter(f)}
      className={`rounded-lg px-3 py-1.5 text-xs font-bold transition ${filter === f ? "bg-[#3a64f2]/20 text-white" : "text-white/55 hover:text-white"}`}
    >
      {label}
    </button>
  );

  return (
    <div
      onDragOver={(e) => {
        if (Array.from(e.dataTransfer.items ?? []).some((i) => i.kind === "file")) {
          e.preventDefault();
          setDragging(true);
        }
      }}
      onDragLeave={(e) => { if (e.currentTarget === e.target) setDragging(false); }}
      onDrop={(e) => {
        setDragging(false);
        const files = imageFiles(e.dataTransfer);
        if (files.length === 0) return;
        e.preventDefault();
        void upload(files);
      }}
    >
      <PageHeader
        title="کتابخانه تصاویر"
        desc={`${formatNumber(items.length)} تصویر · تصویر را یک بار آپلود کنید و لینکش را در مقاله، بنر یا هر بخش سایت استفاده کنید`}
        action={
          <button
            onClick={() => fileRef.current?.click()}
            className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            {uploading > 0 ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
            {uploading > 0 ? `در حال آپلود ${toFa(uploading)} تصویر…` : "آپلود تصویر"}
          </button>
        }
      />
      <input
        ref={fileRef}
        type="file"
        accept="image/*"
        multiple
        className="hidden"
        onChange={(e) => {
          void upload(Array.from(e.target.files ?? []));
          e.target.value = "";
        }}
      />

      <Card
        className={`mb-4 flex flex-col gap-3 border-dashed p-4 text-center text-sm transition sm:flex-row sm:items-center sm:justify-between sm:text-right ${
          dragging ? "border-[#3a64f2] bg-[#3a64f2]/10" : ""
        }`}
      >
        <p className="text-white/55">
          تصویر را اینجا <b className="text-white/80">رها کنید</b>، یا کپی کنید و در همین صفحه <b className="text-white/80">Ctrl+V</b> بزنید.
          چند تصویر را هم‌زمان می‌شود آپلود کرد.
        </p>
        <div className="flex shrink-0 items-center gap-1 rounded-xl bg-white/[0.03] p-1">
          {filterBtn("all", "همه")}
          {filterBtn("library", "آپلودهای کتابخانه")}
          {filterBtn("other", "سایر تصاویر سایت")}
        </div>
      </Card>

      {uploadError && (
        <p className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/[0.08] px-4 py-3 text-sm text-rose-300">{uploadError}</p>
      )}

      <input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="جستجو بر اساس نام تصویر یا آپلودکننده…"
        className={`${inputCls} mb-4`}
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : shown.length === 0 ? (
        <Card className="p-10 text-center text-sm text-white/40">
          {items.length === 0 ? "هنوز تصویری آپلود نشده است." : "تصویری با این مشخصات پیدا نشد."}
        </Card>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
          {shown.map((item) => (
            <Card key={item.id} className="flex flex-col overflow-hidden">
              <button
                type="button"
                onClick={() => setPreview(item)}
                className="relative grid aspect-[4/3] place-items-center bg-[#0d0d15]"
                title="نمایش بزرگ"
              >
                <img src={item.url} alt={item.name} loading="lazy" className="max-h-full max-w-full object-contain" />
                {item.inUse && (
                  <span className="absolute right-2 top-2 rounded-md bg-emerald-500/20 px-1.5 py-0.5 text-[10px] font-bold text-emerald-300">
                    در حال استفاده
                  </span>
                )}
              </button>
              <div className="flex flex-1 flex-col gap-2 p-3">
                <p className="truncate text-xs font-bold text-white" title={item.name}>{item.name || "بدون نام"}</p>
                <p className="text-[11px] text-white/40">
                  {sizeLabel(item.size)} · {dateLabel(item.uploadedAtUtc)}
                  {item.uploadedBy ? ` · ${item.uploadedBy}` : ""}
                </p>
                <div className="mt-auto grid grid-cols-2 gap-1.5">
                  <button
                    type="button"
                    onClick={() => copy(absolute(item.url), `link:${item.id}`)}
                    className="rounded-lg border border-white/10 px-2 py-1.5 text-[11px] font-bold text-white/75 transition hover:bg-white/5 hover:text-white"
                  >
                    {copied === `link:${item.id}` ? "کپی شد ✓" : "کپی لینک"}
                  </button>
                  <button
                    type="button"
                    onClick={() => copy(`![${item.name}](${item.url})`, `md:${item.id}`)}
                    className="rounded-lg border border-white/10 px-2 py-1.5 text-[11px] font-bold text-white/75 transition hover:bg-white/5 hover:text-white"
                    title="برای پیست در متن مقاله یا توضیحات محصول"
                  >
                    {copied === `md:${item.id}` ? "کپی شد ✓" : "کپی برای مقاله"}
                  </button>
                </div>
                <button
                  type="button"
                  onClick={() => remove(item)}
                  disabled={busyId === item.id}
                  title={item.inUse ? "این تصویر در سایت استفاده شده و تا وقتی آن‌جاست حذف نمی‌شود" : undefined}
                  className="rounded-lg px-2 py-1 text-[11px] font-bold text-rose-300/80 transition hover:bg-rose-500/10 hover:text-rose-300 disabled:opacity-50"
                >
                  {busyId === item.id ? "…" : "حذف"}
                </button>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* full-size view — click anywhere to dismiss */}
      {preview && (
        <div
          role="button"
          tabIndex={0}
          onClick={() => setPreview(null)}
          onKeyDown={(e) => { if (e.key === "Escape" || e.key === "Enter") setPreview(null); }}
          className="fixed inset-0 z-50 grid cursor-zoom-out place-items-center bg-black/80 p-6"
        >
          <div className="flex max-h-full max-w-full flex-col items-center gap-3">
            <img src={preview.url} alt={preview.name} className="max-h-[80vh] max-w-full rounded-xl object-contain" />
            <p dir="ltr" className="max-w-full truncate rounded-lg bg-white/10 px-3 py-1.5 font-mono text-xs text-white/80">{absolute(preview.url)}</p>
          </div>
        </div>
      )}
    </div>
  );
}
