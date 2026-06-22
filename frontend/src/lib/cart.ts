"use client";

import { useEffect, useState } from "react";
import { getCurrentUser, AUTH_EVENT } from "./auth";

export type CartItem = {
  productId: number;
  name: string;
  image: string;
  price: number;
  quantity: number;
  planId?: number | null;
  plan?: string | null;
};

const BASE = "phonix_cart";
const EVENT = "phonix-cart-change";

// The cart is scoped per account so it never leaks between logins: each user keeps
// their own basket and a logged-out visitor gets a separate "guest" basket.
function cartKey(): string {
  const user = getCurrentUser();
  return `${BASE}:${user ? user.id : "guest"}`;
}

// One-time migration from the old global key (a single shared basket). Park any
// leftover items in the guest basket so they don't surface inside a logged-in account.
function migrateLegacy() {
  const legacy = localStorage.getItem(BASE);
  if (legacy === null) return;
  const guestKey = `${BASE}:guest`;
  if (localStorage.getItem(guestKey) === null) localStorage.setItem(guestKey, legacy);
  localStorage.removeItem(BASE);
}

const sameLine = (item: CartItem, productId: number, planId?: number | null) =>
  item.productId === productId && (item.planId ?? null) === (planId ?? null);

export function getCart(): CartItem[] {
  if (typeof window === "undefined") return [];
  try {
    migrateLegacy();
    const raw = localStorage.getItem(cartKey());
    return raw ? (JSON.parse(raw) as CartItem[]) : [];
  } catch {
    return [];
  }
}

function save(items: CartItem[]) {
  localStorage.setItem(cartKey(), JSON.stringify(items));
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
    window.addEventListener(AUTH_EVENT, sync); // switch baskets when the account changes
    window.addEventListener("storage", sync);
    return () => {
      window.removeEventListener(EVENT, sync);
      window.removeEventListener(AUTH_EVENT, sync);
      window.removeEventListener("storage", sync);
    };
  }, []);

  const count = items.reduce((s, i) => s + i.quantity, 0);
  const total = items.reduce((s, i) => s + i.price * i.quantity, 0);
  return { items, count, total, ready };
}
