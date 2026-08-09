"use client";

import { useSyncExternalStore } from "react";
import { getCurrentUser, AUTH_EVENT } from "./auth";

export type CartItem = {
  productId: number;
  name: string;
  image: string;
  price: number;
  quantity: number;
  planId?: number | null;
  plan?: string | null;
  // Set when this line extends a V2Ray config the buyer already holds: the config page's own token. Such a
  // line is always exactly one service and never merges with anything, so it stays paired with the service
  // it renews all the way to checkout.
  renewToken?: string | null;
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

// What makes two lines "the same basket row". The renewal token is part of the identity: two renewals of two
// different services are the same product and plan, and folding them together would charge for one and
// extend only one.
const sameLine = (item: CartItem, productId: number, planId?: number | null, renewToken?: string | null) =>
  item.productId === productId
  && (item.planId ?? null) === (planId ?? null)
  && (item.renewToken ?? null) === (renewToken ?? null);

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
  const existing = items.find((i) => sameLine(i, item.productId, item.planId, item.renewToken));
  // A renewal is one service and the checkout refuses any other quantity, so adding the same one twice
  // replaces it rather than stacking a second charge onto it.
  if (existing) existing.quantity = item.renewToken ? 1 : existing.quantity + quantity;
  else items.push({ ...item, quantity: item.renewToken ? 1 : quantity });
  save(items);
}

export function setQuantity(productId: number, quantity: number, planId?: number | null, renewToken?: string | null) {
  let items = getCart();
  if (quantity <= 0) items = items.filter((i) => !sameLine(i, productId, planId, renewToken));
  else items = items.map((i) => (sameLine(i, productId, planId, renewToken) ? { ...i, quantity } : i));
  save(items);
}

export function removeFromCart(productId: number, planId?: number | null, renewToken?: string | null) {
  save(getCart().filter((i) => !sameLine(i, productId, planId, renewToken)));
}

export function clearCart() {
  save([]);
}

// Re-prices the basket from the catalogue. A line stores the price it was added at, but an order is always
// charged at the price the server reads when it is placed — so a basket left sitting while the shop repriced
// showed one total and took another off the buyer's wallet. `priceFor` returns the current price of a line,
// or null when the product/plan is gone (left untouched: the order attempt reports it properly).
// Returns the lines whose price moved, so the page can say so rather than silently changing the number.
export function repriceCart(
  priceFor: (item: CartItem) => number | null,
): { name: string; from: number; to: number }[] {
  const items = getCart();
  const changed: { name: string; from: number; to: number }[] = [];
  const next = items.map((i) => {
    const current = priceFor(i);
    if (current === null || current === i.price) return i;
    changed.push({ name: i.name, from: i.price, to: current });
    return { ...i, price: current };
  });
  if (changed.length > 0) save(next);
  return changed;
}

// The basket is localStorage, an external store, so it is read through React's own API for that instead of
// being copied into state by an effect. Both the key and the stored text are cached: the snapshot must return
// the SAME array while nothing has changed, or every render would look like a change and loop.
const EMPTY: CartItem[] = [];
let cachedKey = "";
let cachedRaw: string | null = null;
let cachedItems: CartItem[] = EMPTY;

function cartSnapshot(): CartItem[] {
  try {
    migrateLegacy();
    const key = cartKey();
    const raw = localStorage.getItem(key);
    if (key !== cachedKey || raw !== cachedRaw) {
      cachedKey = key;
      cachedRaw = raw;
      cachedItems = raw ? (JSON.parse(raw) as CartItem[]) : EMPTY;
    }
    return cachedItems;
  } catch {
    return EMPTY;
  }
}

function subscribeCart(onChange: () => void): () => void {
  window.addEventListener(EVENT, onChange);
  window.addEventListener(AUTH_EVENT, onChange); // switch baskets when the account changes
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener(EVENT, onChange);
    window.removeEventListener(AUTH_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}

// The server has no basket, so it renders empty and not-yet-ready; React swaps in the real one after
// hydration. `ready` is what stops the cart page flashing "empty" before it has been read.
const serverItems = () => EMPTY;
const serverReady = () => false;
const clientReady = () => true;

export function useCart() {
  const items = useSyncExternalStore(subscribeCart, cartSnapshot, serverItems);
  const ready = useSyncExternalStore(subscribeCart, clientReady, serverReady);

  const count = items.reduce((s, i) => s + i.quantity, 0);
  const total = items.reduce((s, i) => s + i.price * i.quantity, 0);
  return { items, count, total, ready };
}
