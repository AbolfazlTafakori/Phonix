"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "./api";
import { repriceCart } from "./cart";
import type { Product } from "./types";

export type PriceMove = { name: string; from: number; to: number };

// How often an open page re-reads the catalogue. The server re-fetches the USD rate every 30 seconds and
// re-prices USD products immediately, so this is the same beat: a page left open never shows a price the
// shop has stopped charging.
const INTERVAL_MS = 30_000;

// Keeps an open storefront page priced at what the server will actually charge.
//
// Prices here move on their own — they are computed from a live USD rate, not typed by hand — so a page that
// priced itself once at load drifts away from the truth the longer it stays open. That matters most at
// checkout, where someone can sit for many minutes: the order is priced by the SERVER when it is placed, so
// a stale page quotes one total and takes another.
//
// Each pass re-reads the catalogue, hands it to the caller, and re-prices the basket against it. The lines
// whose price moved are returned so the page can say what changed instead of the number silently shifting
// under the buyer.
export function useLivePrices(onProducts?: (products: Product[]) => void): PriceMove[] {
  const [moved, setMoved] = useState<PriceMove[]>([]);
  // Held in a ref so a caller passing an inline arrow doesn't restart the polling on every render.
  const onProductsRef = useRef(onProducts);
  onProductsRef.current = onProducts;

  useEffect(() => {
    let alive = true;

    async function sync() {
      if (typeof document !== "undefined" && document.hidden) return; // don't poll a tab nobody is looking at
      const products = await api.products.list().catch(() => null);
      if (!products || !alive) return;
      onProductsRef.current?.(products);

      const byId = new Map(products.map((p) => [p.id, p]));
      const changed = repriceCart((line) => {
        const product = byId.get(line.productId);
        if (!product) return null;
        if (line.planId == null) return product.finalPrice;
        return product.plans.find((pl) => pl.id === line.planId)?.finalPrice ?? null;
      });
      if (changed.length > 0 && alive) setMoved(changed);
    }

    sync();
    const id = setInterval(sync, INTERVAL_MS);
    // A backgrounded tab is skipped above and would otherwise show its stale prices for up to a full interval
    // after the buyer returns to it — so sync the moment it comes back to the front.
    const onWake = () => sync();
    window.addEventListener("focus", onWake);
    document.addEventListener("visibilitychange", onWake);

    return () => {
      alive = false;
      clearInterval(id);
      window.removeEventListener("focus", onWake);
      document.removeEventListener("visibilitychange", onWake);
    };
  }, []);

  return moved;
}
