"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { BlogPost, BlogPostInput } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

export default function AdminBlogPage() {
  const [posts, setPosts] = useState<BlogPost[]>([]);
  const [drafts, setDrafts] = useState<Record<number, BlogPostInput>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);

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

  async function add() {
    setAdding(true);
    try {
      const created = await api.blog.create({
        slug: `post-${Date.now()}`,
        tag: "دسته | زمان مطالعه",
        title: "عنوان مطلب جدید",
        excerpt: "",
        content: "",
        date: "",
        image: "/figma/blog-1.png",
        sortOrder: posts.length + 1,
        isActive: true,
      });
      setPosts((prev) => [...prev, created]);
      setDrafts((prev) => ({ ...prev, [created.id]: toInput(created) }));
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
          <button
            onClick={add}
            disabled={adding}
            className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            {adding ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
            مطلب جدید
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <div className="grid gap-5 lg:grid-cols-2">
          {posts.map((p) => {
            const d = drafts[p.id];
            if (!d) return null;
            return (
              <Card key={p.id} className="p-5">
                <div className="flex items-start gap-4">
                  <ImageField aspect="wide" value={d.image} onChange={(v) => setField(p.id, "image", v)} className="w-40 shrink-0" />
                  <label className="ml-auto flex items-center gap-2 text-xs text-white/60">
                    نمایش
                    <Toggle checked={d.isActive} onChange={(v) => setField(p.id, "isActive", v)} />
                  </label>
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
