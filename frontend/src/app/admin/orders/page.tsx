"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

// Orders were split into three sections (receipts / fulfillment / status). Send the old combined route to the
// receipt-approval section so existing links and bookmarks keep working.
export default function AdminOrdersRedirect() {
  const router = useRouter();
  useEffect(() => {
    router.replace("/admin/orders/receipts");
  }, [router]);
  return null;
}
