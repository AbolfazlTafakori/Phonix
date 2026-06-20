"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";

export default function FavoriteButton({ productId }: { productId: number }) {
  const { user } = useAuth();
  const router = useRouter();
  const [fav, setFav] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!user) return;
    api.favorites.ids(user.id).then((ids) => setFav(ids.includes(productId))).catch(() => {});
  }, [user, productId]);

  async function toggle() {
    if (!user) {
      router.push("/login");
      return;
    }
    setBusy(true);
    try {
      const r = await api.favorites.toggle(productId);
      setFav(r.favorited);
    } finally {
      setBusy(false);
    }
  }

  return (
    <button
      onClick={toggle}
      disabled={busy}
      className={`flex items-center gap-2 rounded-xl border px-4 py-2 text-sm font-bold transition ${
        fav ? "border-[#e60053]/50 bg-[#e60053]/10 text-[#ff5a8a]" : "border-white/10 text-white/70 hover:bg-white/5"
      }`}
    >
      <span className="text-base">{fav ? "♥" : "♡"}</span>
      {fav ? "در علاقه‌مندی‌ها" : "افزودن به علاقه‌مندی"}
    </button>
  );
}
