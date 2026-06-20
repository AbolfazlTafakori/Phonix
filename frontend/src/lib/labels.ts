import type { OrderStatus, TicketStatus } from "./types";

export const orderStatusLabel: Record<OrderStatus, string> = {
  PendingApproval: "در انتظار تأیید",
  Preparing: "در حال آماده‌سازی",
  Completed: "تکمیل شده",
  Cancelled: "لغو شده",
};

export const ticketStatusLabel: Record<TicketStatus, string> = {
  Open: "باز",
  Answered: "پاسخ داده شده",
  Closed: "بسته شده",
};
