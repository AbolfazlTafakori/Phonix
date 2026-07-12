"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { BlogPost, BlogPostInput } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import { useSiteContent } from "@/components/admin/useSiteContent";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

// Parse an uploaded article .md file into a BlogPostInput so posts can be bulk-loaded without
// retyping. Format: first `# ` line is the title; then optional `key: value` header lines
// (slug/نامک, tag/برچسب, excerpt/خلاصه, image/تصویر, date/تاریخ); everything after the first
// blank line following the headers is the article body (markdown).
function parseBlogMd(text: string, fileName: string): Omit<BlogPostInput, "featuredOnHome" | "sortOrder" | "isActive"> {
  const raw = text.replace(/\r\n/g, "\n").trim();
  const lines = raw.split("\n");
  let title = "";
  const meta: Record<string, string> = {};
  let bodyStart = 0;
  let sawMeta = false;
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i].trim();
    if (!title && l.startsWith("# ")) { title = l.slice(2).trim(); bodyStart = i + 1; continue; }
    const m = l.match(/^(slug|نامک|tag|برچسب|excerpt|خلاصه|image|تصویر|date|تاریخ)\s*:\s*(.+)$/i);
    if (title && m) {
      const key = m[1].toLowerCase();
      const map: Record<string, string> = { "نامک": "slug", "برچسب": "tag", "خلاصه": "excerpt", "تصویر": "image", "تاریخ": "date" };
      meta[map[key] ?? key] = m[2].trim();
      bodyStart = i + 1;
      sawMeta = true;
      continue;
    }
    // Blank lines between the title and the meta block are allowed; the blank line AFTER the
    // meta block ends the header. Any other non-meta line means the body has begun.
    if (title && l === "") {
      bodyStart = i + 1;
      if (sawMeta) break;
      continue;
    }
    if (title && l !== "") break;
  }
  const content = lines.slice(bodyStart).join("\n").trim();
  const fallbackSlug = fileName.replace(/\.md$/i, "").replace(/^\d+[-_.]?/, "").trim() || `post-${Date.now()}`;
  return {
    slug: meta.slug || fallbackSlug,
    title: title || fallbackSlug,
    tag: meta.tag || "مقاله",
    excerpt: meta.excerpt || content.split("\n").find((l) => l.trim() && !l.startsWith("#"))?.slice(0, 160) || "",
    image: meta.image || "/figma/blog-1.png",
    date: meta.date || "",
    content,
  };
}

export default function AdminBlogPage() {
  const [posts, setPosts] = useState<BlogPost[]>([]);
  const [drafts, setDrafts] = useState<Record<number, BlogPostInput>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);
  const site = useSiteContent();

  useEffect(() => {
    (async () => {
      try {
        const data = await api.blog.list();
        setPosts(data);
        setDrafts(Object.fromEntries(data.map((p) => [p.id, toInput(p)])));
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setField = <K extends keyof BlogPostInput>(id: number, key: K, value: BlogPostInput[K]) =>
    setDrafts((prev) => ({ ...prev, [id]: { ...prev[id], [key]: value } }));

  const dirty = (p: BlogPost) => JSON.stringify(drafts[p.id]) !== JSON.stringify(toInput(p));

  async function save(p: BlogPost) {
    setBusy(p.id);
    try {
      const updated = await api.blog.update(p.id, drafts[p.id]);
      setPosts((prev) => prev.map((x) => (x.id === p.id ? updated : x)));
      setDrafts((prev) => ({ ...prev, [p.id]: toInput(updated) }));
    } finally {
      setBusy(null);
    }
  }

  async function remove(p: BlogPost) {
    if (!confirm("این مطلب حذف شود؟")) return;
    setBusy(p.id);
    try {
      await api.blog.remove(p.id);
      setPosts((prev) => prev.filter((x) => x.id !== p.id));
    } finally {
      setBusy(null);
    }
  }

  // Create a post directly from an uploaded .md file (no manual typing).
  const [importing, setImporting] = useState(false);
  function importMd(file: File) {
    setImporting(true);
    setError("");
    const reader = new FileReader();
    reader.onload = async () => {
      try {
        const parsed = parseBlogMd(String(reader.result ?? ""), file.name);
        if (!parsed.content) throw new Error("متن مقاله در فایل پیدا نشد؛ قالب فایل را بررسی کنید.");
        const created = await api.blog.create({ ...parsed, featuredOnHome: false, sortOrder: posts.length + 1, isActive: true });
        setPosts((prev) => [created, ...prev]);
        setDrafts((prev) => ({ ...prev, [created.id]: toInput(created) }));
        if (typeof window !== "undefined") window.scrollTo({ top: 0, behavior: "smooth" });
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری فایل");
      } finally {
        setImporting(false);
      }
    };
    reader.onerror = () => { setError("خواندن فایل ناموفق بود."); setImporting(false); };
    reader.readAsText(file, "utf-8");
  }

  async function add() {
    setAdding(true);
    setError("");
    try {
      const created = await api.blog.create({
        slug: `post-${Date.now()}`,
        tag: "دسته | زمان مطالعه",
        title: "عنوان مطلب جدید",
        excerpt: "",
        content: "",
        date: "",
        image: "/figma/blog-1.png",
        featuredOnHome: false,
        sortOrder: posts.length + 1,
        isActive: true,
      });
      // show the new (empty) post at the TOP and scroll up to it, so it's immediately visible and editable
      // instead of being appended to the bottom of a long list.
      setPosts((prev) => [created, ...prev]);
      setDrafts((prev) => ({ ...prev, [created.id]: toInput(created) }));
      if (typeof window !== "undefined") window.scrollTo({ top: 0, behavior: "smooth" });
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در ایجاد مطلب جدید");
    } finally {
      setAdding(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="بلاگ"
        desc="مطالب بخش وبلاگ در صفحه اصلی"
        action={
          <div className="flex items-center gap-2">
            <label className="flex cursor-pointer items-center gap-2 rounded-xl border border-white/15 px-5 py-2.5 text-sm font-bold text-white/85 transition hover:bg-white/5">
              {importing ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
              مطلب از فایل .md
              <input
                type="file"
                accept=".md,.markdown,text/markdown,text/plain"
                className="hidden"
                onChange={(e) => { const f = e.target.files?.[0]; if (f) importMd(f); e.target.value = ""; }}
              />
            </label>
            <button
              onClick={add}
              disabled={adding}
              className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
            >
              {adding ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
              مطلب جدید
            </button>
          </div>
        }
      />

      {error && (
        <Card className="mb-5 p-4 text-center text-rose-400">{error}</Card>
      )}

      {site.content && (
        <Card className="mb-5 p-5">
          <div className="flex flex-wrap items-center gap-4">
            <div className="min-w-0 flex-1">
              <h3 className="text-sm font-bold text-white">زمان تعویض خودکار نمایش وبلاگ</h3>
              <p className="mt-1 text-xs leading-6 text-white/45">بر حسب ثانیه؛ عدد <span className="font-bold text-white/70">۰</span> یعنی تعویض خودکار خاموش است (فقط انتخاب دستی).</p>
            </div>
            <input
              type="number"
              min={0}
              dir="ltr"
              value={site.content.blogAutoplaySeconds}
              onChange={(e) => site.setContent((c) => (c ? { ...c, blogAutoplaySeconds: Math.max(0, Math.round(Number(e.target.value) || 0)) } : c))}
              className={`${inputCls} w-24 shrink-0 text-left`}
            />
            <button
              onClick={site.save}
              disabled={site.saving}
              className="flex h-11 shrink-0 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110"
            >
              {site.saving ? <Spinner /> : "ذخیره زمان"}
            </button>
            {site.saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
          </div>
        </Card>
      )}

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : (
        <div className="grid gap-5 lg:grid-cols-2">
          {posts.map((p) => {
            const d = drafts[p.id];
            if (!d) return null;
            return (
              <Card key={p.id} className="p-5">
                <div className="flex items-start gap-4">
                  <ImageField aspect="wide" value={d.image} onChange={(v) => setField(p.id, "image", v)} className="w-40 shrink-0" />
                  <div className="ml-auto flex flex-col items-end gap-2.5">
                    <label className="flex items-center gap-2 text-xs text-white/60">
                      نمایش
                      <Toggle checked={d.isActive} onChange={(v) => setField(p.id, "isActive", v)} />
                    </label>
                    <label className="flex items-center gap-2 text-xs text-white/60">
                      صفحه اصلی
                      <Toggle checked={d.featuredOnHome} onChange={(v) => setField(p.id, "featuredOnHome", v)} />
                    </label>
                  </div>
                </div>

                <div className="mt-4 grid gap-4">
                  <Field label="عنوان">
                    <input value={d.title} onChange={(e) => setField(p.id, "title", e.target.value)} className={inputCls} />
                  </Field>
                  <div className="grid gap-4 sm:grid-cols-3">
                    <Field label="برچسب">
                      <input value={d.tag} onChange={(e) => setField(p.id, "tag", e.target.value)} className={inputCls} />
                    </Field>
                    <Field label="تاریخ">
                      <input value={d.date} onChange={(e) => setField(p.id, "date", e.target.value)} className={inputCls} />
                    </Field>
                    <Field label="نامک (در آدرس)">
                      <input value={d.slug} onChange={(e) => setField(p.id, "slug", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="my-post" />
                    </Field>
                  </div>
                  <Field label="خلاصه (در کارت‌ها نمایش داده می‌شود)">
                    <textarea rows={2} value={d.excerpt} onChange={(e) => setField(p.id, "excerpt", e.target.value)} className={`${inputCls} h-auto py-3`} />
                  </Field>
                  <Field label="متن کامل مطلب">
                    <textarea rows={6} value={d.content} onChange={(e) => setField(p.id, "content", e.target.value)} className={`${inputCls} h-auto py-3 leading-7`} />
                  </Field>

                  <div className="flex items-center gap-3">
                    <button
                      onClick={() => save(p)}
                      disabled={!dirty(p) || busy === p.id}
                      className={`flex h-11 items-center gap-2 rounded-xl px-8 text-sm font-bold transition ${
                        dirty(p) ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white hover:brightness-110" : "cursor-default border border-white/10 text-white/30"
                      }`}
                    >
                      {busy === p.id ? <Spinner /> : "ذخیره"}
                    </button>
                    <button
                      onClick={() => remove(p)}
                      className="grid h-11 w-11 place-items-center rounded-xl border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
                    >
                      <AdminIcon name="trash" className="h-5 w-5" />
                    </button>
                  </div>
                </div>
              </Card>
            );
          })}

          {posts.length === 0 && <Card className="p-12 text-center text-white/40 lg:col-span-2">هنوز مطلبی اضافه نشده است</Card>}
        </div>
      )}
    </div>
  );
}

function toInput(p: BlogPost): BlogPostInput {
  const { id, ...rest } = p;
  void id;
  return rest;
}
