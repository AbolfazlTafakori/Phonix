"use client";

import { useState, type ReactNode } from "react";
import AdminSidebar from "./AdminSidebar";
import AdminTopbar from "./AdminTopbar";

export default function AdminShell({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="min-h-screen bg-[#0b0b12] text-white lg:pr-64">
      <AdminSidebar open={open} onClose={() => setOpen(false)} />
      <AdminTopbar onMenu={() => setOpen(true)} />
      <main className="p-5 lg:p-8">{children}</main>
    </div>
  );
}
