"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { SiteContent } from "@/lib/types";

export function useSiteContent() {
  const [content, setContent] = useState<SiteContent | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setContent(await api.siteContent.get());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  async function save() {
    if (!content) return;
    setSaving(true);
    setSaved(false);
    try {
      const updated = await api.siteContent.update(content);
      setContent(updated);
      setSaved(true);
    } finally {
      setSaving(false);
    }
  }

  return { content, setContent, loading, error, saving, saved, save };
}
