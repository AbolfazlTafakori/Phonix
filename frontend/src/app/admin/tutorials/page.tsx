"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { Product, Tutorial, TutorialInput } from "@/lib/types";
import { formatNumber, toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, Modal, Field, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";
import MarkdownEditor from "@/components/admin/MarkdownEditor";

// Product tutorials: a how-to written once and linked to the products it applies to. A customer sees it in
// their orders, under the product, once their payment is confirmed.

type Draft = TutorialInput & { videoMeta: Record<string, { size: number; url: string }> };

const emptyDraft = (): Draft => ({ title: "", body: "", productIds: [], videos: [], sortOrder: 0, isActive: true, videoMeta: {} });

function sizeLabel(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${toFa((bytes / (1024 * 1024)).toFixed(1))} مگابایت`;
  return `${formatNumber(Math.max(1, Math.round(bytes / 1024)))} کیلوبایت`;
}

export default function AdminTutorialsPage() {
  const [items, setItems] = useState<Tutorial[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [draft, setDraft] = useState<Draft>(emptyDraft());
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [productSearch, setProductSearch] = useState("");
  // One upload at a time, with its progress — a video is large and the bar is how the admin knows it's moving.
  const [upload, setUpload] = useState<{ name: string; fraction: number } | null>(null);
  const [uploadError, setUploadError] = useState("");
  const videoRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.all([api.tutorials.list(), api.products.list().catch(() => [] as Product[])])
      .then(([list, prods]) => {
        if (cancelled) return;
        setItems(list);
        setProducts(prods);
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : "خطا در بارگذاری"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  const productName = useMemo(() => new Map(products.map((p) => [p.id, p.name])), [products]);
  const shownProducts = useMemo(() => {
    const q = productSearch.trim().toLowerCase();
    return q ? products.filter((p) => p.name.toLowerCase().includes(q)) : products;
  }, [products, productSearch]);

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }));

  function openNew() {
    setEditingId(null);
    setDraft(emptyDraft());
    setSaveError("");
    setUploadError("");
    setProductSearch("");
    setOpen(true);
  }

  function openEdit(t: Tutorial) {
    setEditingId(t.id);
    setDraft({
      title: t.title,
      body: t.body,
      productIds: [...t.productIds],
      videos: t.videos.map((v) => ({ id: v.id, name: v.name })),
      sortOrder: t.sortOrder,
      isActive: t.isActive,
      videoMeta: Object.fromEntries(t.videos.map((v) => [v.id, { size: v.size, url: v.url }])),
    });
    setSaveError("");
    setUploadError("");
    setProductSearch("");
    setOpen(true);
  }

  function toggleProduct(id: number) {
    setDraft((d) => ({
      ...d,
      productIds: d.productIds.includes(id) ? d.productIds.filter((x) => x !== id) : [...d.productIds, id],
    }));
  }

  async function addVideo(file: File | undefined) {
    if (!file) return;
    setUploadError("");
    setUpload({ name: file.name, fraction: 0 });
    try {
      const v = await api.tutorials.uploadVideo(file, (fraction) => setUpload({ name: file.name, fraction }));
      setDraft((d) => ({
        ...d,
        videos: [...d.videos, { id: v.id, name: v.name }],
        videoMeta: { ...d.videoMeta, [v.id]: { size: v.size, url: v.url } },
      }));
    } catch (e) {
      setUploadError(e instanceof Error ? e.message : "آپلود ویدیو ناموفق بود.");
    } finally {
      setUpload(null);
    }
  }

  function moveVideo(i: number, dir: -1 | 1) {
    setDraft((d) => {
      const next = [...d.videos];
      const j = i + dir;
      if (j < 0 || j >= next.length) return d;
      [next[i], next[j]] = [next[j], next[i]];
      return { ...d, videos: next };
    });
  }

  async function save() {
    setSaving(true);
    setSaveError("");
    const body: TutorialInput = {
      title: draft.title,
      body: draft.body,
      productIds: draft.productIds,
      videos: draft.videos,
      sortOrder: draft.sortOrder,
      isActive: draft.isActive,
    };
    try {
      if (editingId === null) {
        const created = await api.tutorials.create(body);
        setItems((prev) => [...prev, created]);
      } else {
        const updated = await api.tutorials.update(editingId, body);
        setItems((prev) => prev.map((t) => (t.id === editingId ? updated : t)));
      }
      setOpen(false);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : "ذخیره آموزش ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  async function remove(t: Tutorial) {
    if (!confirm(`آموزش «${t.title}» و ویدیوهایش برای همیشه حذف شود؟`)) return;
    try {
      await api.tutorials.remove(t.id);
      setItems((prev) => prev.filter((x) => x.id !== t.id));
    } catch (e) {
      alert(e instanceof Error ? e.message : "حذف آموزش ناموفق بود.");
    }
  }

  const sorted = [...items].sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id);

  return (
    <div>
      <PageHeader
        title="آموزش محصولات"
        desc={`${formatNumber(items.length)} آموزش · بعد از تأیید پرداخت، در بخش سفارش‌های خریدار زیر همان محصول نمایش داده می‌شود`}
        action={
          <button
            onClick={openNew}
            className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            <AdminIcon name="plus" className="h-4 w-4" />
            افزودن آموزش
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : sorted.length === 0 ? (
        <Card className="p-10 text-center text-sm text-white/40">هنوز آموزشی ثبت نشده است.</Card>
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {sorted.map((t) => (
            <Card key={t.id} className="flex flex-col gap-3 p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-white">{t.title}</p>
                  <p className="mt-1 text-[11px] text-white/40">
                    {t.videos.length > 0 ? `${toFa(t.videos.length)} ویدیو` : "بدون ویدیو"}
                    {!t.isActive && " · غیرفعال"}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  <button onClick={() => openEdit(t)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff]">
                    <AdminIcon name="edit" className="h-4 w-4" />
                  </button>
                  <button onClick={() => remove(t)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-rose-500/50 hover:text-rose-400">
                    <AdminIcon name="trash" className="h-4 w-4" />
                  </button>
                </div>
              </div>
              <div className="flex flex-wrap gap-1.5">
                {t.productIds.length === 0 ? (
                  <span className="rounded-md bg-amber-500/15 px-2 py-0.5 text-[11px] font-bold text-amber-300">به هیچ محصولی وصل نیست</span>
                ) : (
                  t.productIds.map((id) => (
                    <span key={id} className="rounded-md bg-white/[0.06] px-2 py-0.5 text-[11px] text-white/70">
                      {productName.get(id) ?? `محصول #${toFa(id)}`}
                    </span>
                  ))
                )}
              </div>
            </Card>
          ))}
        </div>
      )}

      <Modal open={open} onClose={() => !saving && !upload && setOpen(false)} title={editingId === null ? "افزودن آموزش" : "ویرایش آموزش"} size="3xl">
        <div className="grid gap-5">
          <div className="grid gap-4 sm:grid-cols-[1fr_120px]">
            <Field label="عنوان آموزش">
              <input value={draft.title} onChange={(e) => set("title", e.target.value)} maxLength={150} placeholder="مثلاً: نصب و ورود به اپ روی گوشی" className={inputCls} />
            </Field>
            <Field label="ترتیب نمایش">
              <input type="number" dir="ltr" value={draft.sortOrder} onChange={(e) => set("sortOrder", Number(e.target.value) || 0)} className={`${inputCls} text-left`} />
            </Field>
          </div>

          <div className="rounded-xl border border-white/8 p-4">
            <p className="mb-1 text-sm font-bold text-white">محصولات مرتبط</p>
            <p className="mb-3 text-xs text-white/40">خریداران این محصولات بعد از تأیید پرداخت، این آموزش را در سفارش خود می‌بینند.</p>
            {products.length === 0 ? (
              <p className="text-xs text-white/40">فهرست محصولات بارگذاری نشد.</p>
            ) : (
              <>
                <input value={productSearch} onChange={(e) => setProductSearch(e.target.value)} placeholder="جستجوی محصول…" className={`${inputCls} mb-2 h-10`} />
                <div className="max-h-48 space-y-1 overflow-y-auto rounded-lg bg-white/[0.02] p-1">
                  {shownProducts.map((p) => (
                    <label key={p.id} className="flex cursor-pointer items-center gap-3 rounded-md px-2 py-1.5 text-sm text-white/80 hover:bg-white/5">
                      <input type="checkbox" checked={draft.productIds.includes(p.id)} onChange={() => toggleProduct(p.id)} className="h-4 w-4 accent-[#3a64f2]" />
                      <span className="flex-1 truncate">{p.name}</span>
                      {!p.isActive && <span className="text-[10px] text-white/35">غیرفعال</span>}
                    </label>
                  ))}
                  {shownProducts.length === 0 && <p className="px-2 py-3 text-xs text-white/40">محصولی پیدا نشد.</p>}
                </div>
                <p className="mt-2 text-[11px] text-white/45">{toFa(draft.productIds.length)} محصول انتخاب شده</p>
              </>
            )}
          </div>

          <div>
            <span className="mb-1.5 block text-xs font-medium text-white/55">متن آموزش</span>
            <MarkdownEditor
              value={draft.body}
              onChange={(v) => set("body", v)}
              rows={12}
              placeholder="مرحله‌به‌مرحله توضیح دهید… عکس را می‌توانید مستقیم کپی و اینجا پیست کنید."
            />
          </div>

          <div className="rounded-xl border border-white/8 p-4">
            <div className="mb-3 flex items-center justify-between gap-3">
              <div>
                <p className="text-sm font-bold text-white">ویدیوها</p>
                <p className="text-xs text-white/40">MP4 یا WebM، حداکثر ۵۰۰ مگابایت. فقط خریداران محصول می‌توانند پخش کنند.</p>
              </div>
              <button
                type="button"
                onClick={() => videoRef.current?.click()}
                disabled={!!upload}
                className="flex shrink-0 items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-50"
              >
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن ویدیو
              </button>
              <input
                ref={videoRef}
                type="file"
                accept="video/mp4,video/webm,.mp4,.m4v,.webm"
                className="hidden"
                onChange={(e) => {
                  void addVideo(e.target.files?.[0]);
                  e.target.value = "";
                }}
              />
            </div>

            {upload && (
              <div className="mb-3 rounded-lg bg-white/[0.03] p-3">
                <div className="mb-2 flex items-center justify-between gap-2 text-xs text-white/70">
                  <span className="truncate" dir="ltr">{upload.name}</span>
                  <span>{toFa(Math.round(upload.fraction * 100))}٪</span>
                </div>
                <div className="h-1.5 overflow-hidden rounded-full bg-white/10">
                  <div className="h-full rounded-full bg-[#3a64f2] transition-[width]" style={{ width: `${Math.round(upload.fraction * 100)}%` }} />
                </div>
                {upload.fraction >= 1 && <p className="mt-2 text-[11px] text-white/45">در حال ذخیره روی سرور…</p>}
              </div>
            )}
            {uploadError && <p className="mb-3 text-xs text-rose-300">{uploadError}</p>}

            {draft.videos.length === 0 ? (
              <p className="text-xs text-white/35">ویدیویی اضافه نشده است.</p>
            ) : (
              <div className="space-y-2">
                {draft.videos.map((v, i) => (
                  <div key={v.id} className="flex flex-wrap items-center gap-2 rounded-lg bg-white/[0.03] p-2">
                    <span className="grid h-7 w-7 shrink-0 place-items-center rounded-md bg-white/[0.06] text-[11px] font-bold text-white/70">{toFa(i + 1)}</span>
                    <input
                      value={v.name}
                      onChange={(e) => set("videos", draft.videos.map((x, k) => (k === i ? { ...x, name: e.target.value } : x)))}
                      placeholder="عنوان ویدیو"
                      maxLength={120}
                      className={`${inputCls} h-9 min-w-0 flex-1`}
                    />
                    {draft.videoMeta[v.id] && <span className="text-[11px] text-white/40">{sizeLabel(draft.videoMeta[v.id].size)}</span>}
                    {draft.videoMeta[v.id] && (
                      <a href={draft.videoMeta[v.id].url} target="_blank" rel="noopener noreferrer" className="rounded-md px-2 py-1 text-[11px] font-bold text-[#6f93ff] hover:bg-white/5">پخش</a>
                    )}
                    <button type="button" onClick={() => moveVideo(i, -1)} disabled={i === 0} className="rounded-md px-1.5 py-1 text-xs text-white/60 hover:bg-white/5 disabled:opacity-30" title="بالاتر">▲</button>
                    <button type="button" onClick={() => moveVideo(i, 1)} disabled={i === draft.videos.length - 1} className="rounded-md px-1.5 py-1 text-xs text-white/60 hover:bg-white/5 disabled:opacity-30" title="پایین‌تر">▼</button>
                    <button
                      type="button"
                      onClick={() => set("videos", draft.videos.filter((_, k) => k !== i))}
                      className="rounded-md px-2 py-1 text-[11px] font-bold text-rose-300/80 hover:bg-rose-500/10 hover:text-rose-300"
                    >
                      حذف
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
            <span className="text-sm text-white/80">فعال (برای خریداران نمایش داده شود)</span>
            <Toggle checked={draft.isActive} onChange={(v) => set("isActive", v)} />
          </label>

          {saveError && <p className="rounded-xl border border-rose-500/30 bg-rose-500/[0.08] px-4 py-3 text-sm text-rose-300">{saveError}</p>}

          <div className="flex gap-3">
            <button
              onClick={save}
              disabled={saving || !!upload || !draft.title.trim()}
              className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
            >
              {saving ? <Spinner /> : "ذخیره آموزش"}
            </button>
            <button onClick={() => setOpen(false)} disabled={saving || !!upload} className="h-11 rounded-xl border border-white/10 px-8 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-50">
              انصراف
            </button>
          </div>
          {upload && <p className="text-[11px] text-white/45">تا پایان آپلود ویدیو، ذخیره غیرفعال است.</p>}
        </div>
      </Modal>
    </div>
  );
}
