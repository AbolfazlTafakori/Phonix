"use client";

import { useEffect, useState } from "react";

export type CartItem = {
  productId: number;
  name: string;
  image: string;
  price: number;
  quantity: number;
  planId?: number | null;
  plan?: string | null;
};

const KEY = "phonix_cart";
const EVENT = "phonix-cart-change";

const sameLine = (item: CartItem, productId: number, planId?: number | null) =>
  item.productId === productId && (item.planId ?? null) === (planId ?? null);

export function getCart(): CartItem[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as CartItem[]) : [];
  } catch {
    return [];
  }
}

function save(items: CartItem[]) {
  localStorage.setItem(KEY, JSON.stringify(items));
  window.dispatchEvent(new Event(EVENT));
}

export function addToCart(item: Omit<CartItem, "quantity">, quantity = 1) {
  const items = getCart();
  const existing = items.find((i) => sameLine(i, item.productId, item.planId));
  if (existing) existing.quantity += quantity;
  else items.push({ ...item, quantity });
  save(items);
}

export function setQuantity(productId: number, quantity: number, planId?: number | null) {
  let items = getCart();
  if (quantity <= 0) items = items.filter((i) => !sameLine(i, productId, planId));
  else items = items.map((i) => (sameLine(i, productId, planId) ? { ...i, quantity } : i));
  save(items);
}

export function removeFromCart(productId: number, planId?: number | null) {
  save(getCart().filter((i) => !sameLine(i, productId, planId)));
}

export function clearCart() {
  save([]);
}

export function useCart() {
  const [items, setItems] = useState<CartItem[]>([]);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const sync = () => setItems(getCart());
    sync();
    setReady(true);
    window.addEventListener(EVENT, sync);
    window.addEventListener("storage", sync);
    return () => {
      window.removeEventListener(EVENT, sync);
      window.removeEventListener("storage", sync);
    };
  }, []);

  const count = items.reduce((s, i) => s + i.quantity, 0);
  const total = items.reduce((s, i) => s + i.price * i.quantity, 0);
  return { items, count, total, ready };
}
