import type {
  Category,
  CategoryInput,
  Product,
  ProductInput,
  StockItem,
  StockAccount,
  StockManagedAccount,
  StockWaitingOrder,
  Invoice,
  SeatSubmission,
  SeatUnitInfo,
  StockSummary,
  User,
  UserRole,
  UserUpdateInput,
  WalletInput,
  AdminNavGroup,
  PricingSettings,
  Plan,
  PlanInput,
  HeroSlide,
  HeroSlideInput,
  HomeCategory,
  HomeCategoryInput,
  Showcase,
  ShowcaseInput,
  BlogPost,
  BlogPostInput,
  SiteContent,
  AdvancedSettings,
  PaymentMethod,
  PaymentMethodInput,
  PaymentSettings,
  EmailSettings,
  MailFolder,
  MailPage,
  MailMessage,
  MailConversationPage,
  MailConversationDetail,
  MailboxSettings,
  MailboxSettingsInput,
  V2RayProviderInfo,
  V2RayPanelInfo,
  V2RayPanelInput,
  TelegramSettings,
  Transaction,
  TxStatus,
  BankCard,
  BankCardStatus,
  Notification,
  AdminNotification,
  Comment,
  CommentInput,
  CommentStatus,
  KycRequest,
  KycInput,
  KycStatus,
  Order,
  OrderStatus,
  Ticket,
  TicketStatus,
  TicketPriority,
  OverviewStats,
  TopProductStat,
  ServerStatus,
  ClusterStatus,
  AuditLogPage,
  SentEmailPage,
  AuthResult,
  LoginResult,
  TwoFactorStatus,
  TwoFactorSetup,
  StaffMember,
  PermissionInfo,
  AdminChatThread,
  CustomerChatThread,
  LogFile,
  LogView,
  ConversationSummary,
  ReferralReport,
  DiscountCode,
  DiscountCodeInput,
  DiscountResult,
  UsdRateInfo,
  PagedResult,
} from "./types";
import { getCsrfToken } from "./token";

// Client requests go same-origin (relative) so the app serves correctly behind any domain — including the
// p-ui fallback domain — with no rebuild. Server-side rendering and middleware can't use a relative URL, so
// on the server we target the API directly over the loopback (overridable via PHONIX_INTERNAL_API_URL).
const BASE =
  typeof window === "undefined"
    ? process.env.PHONIX_INTERNAL_API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://127.0.0.1:5228"
    : process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";

// A 401 means the session cookie is gone or no longer valid (expired, or the password changed and
// rotated the security stamp). The client may still hold a stale user in localStorage, so every guarded
// page would otherwise surface a raw "خطای ارتباط با سرور (401)". Instead, clear that stale state and
// bounce to the matching login once. Only the authenticated areas are touched; public pages are left alone.
function handleTwoFactorRequired() {
  if (typeof window === "undefined") return;
  if (window.location.pathname !== "/admin/settings/2fa") window.location.replace("/admin/settings/2fa");
}

function handleUnauthorized() {
  if (typeof window === "undefined") return;
  const path = window.location.pathname;
  if (path.startsWith("/admin")) {
    if (path === "/admin/login") return;
    try { localStorage.removeItem("phonix_admin"); } catch { /* ignore */ }
    window.location.replace("/admin/login");
  } else if (path.startsWith("/account")) {
    try { localStorage.removeItem("phonix_user"); } catch { /* ignore */ }
    window.location.replace("/login");
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const csrf = getCsrfToken();
  const res = await fetch(`${BASE}/api${path}`, {
    cache: "no-store",
    credentials: "include",
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(csrf ? { "X-CSRF-Token": csrf } : {}),
      ...init?.headers,
    },
  });
  if (!res.ok) {
    if (res.status === 401) handleUnauthorized();
    let msg = `خطای ارتباط با سرور (${res.status})`;
    try {
      const text = await res.text();
      if (text) {
        // The mandatory-2FA gate blocks staff actions until they enrol; bounce them to the security page.
        if (res.status === 403 && text.includes("requiresTwoFactorSetup")) handleTwoFactorRequired();
        msg = text.replace(/^"|"$/g, "");
      }
    } catch {
      // ignore
    }
    throw new Error(msg);
  }
  if (res.status === 204) return undefined as T;
  // Some endpoints reply 200 with an empty body (e.g. Ok() on toggle actions); avoid res.json() throwing.
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
}

// Uploads a single image as multipart/form-data to a protected endpoint (sends the CSRF header + cookie,
// never sets Content-Type so the browser writes the multipart boundary).
async function uploadForm<T>(path: string, file: File): Promise<T> {
  const csrf = getCsrfToken();
  const fd = new FormData();
  fd.append("file", file);
  const res = await fetch(`${BASE}/api${path}`, {
    method: "POST",
    cache: "no-store",
    credentials: "include",
    headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}) },
    body: fd,
  });
  if (!res.ok) {
    if (res.status === 401) handleUnauthorized();
    let msg = `خطای ارتباط با سرور (${res.status})`;
    try { const text = await res.text(); if (text) msg = text.replace(/^"|"$/g, ""); } catch { /* ignore */ }
    throw new Error(msg);
  }
  return (await res.json()) as T;
}

// Builds the src for a stored identity image. New images are opaque ids streamed from the authenticated,
// ownership-checked download endpoint (the cookie rides along — same site); legacy "/uploads/..." values
// from before protected storage are returned as-is so old records still render.
function protectedSrc(folder: "kyc" | "cards", value: string): string {
  return /^(https?:|\/uploads\/)/.test(value) ? value : `${BASE}/api/${folder}/download/${encodeURIComponent(value)}`;
}

// Receipts use the same scheme but live under the transactions controller's receipt endpoint.
function receiptSrc(value: string): string {
  return /^(https?:|\/uploads\/)/.test(value) ? value : `${BASE}/api/transactions/receipt/${encodeURIComponent(value)}`;
}

function qs(params?: Record<string, string | number | boolean | undefined>): string {
  if (!params) return "";
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const out = search.toString();
  return out ? `?${out}` : "";
}

const json = (body: unknown) => JSON.stringify(body);

export const api = {
  categories: {
    list: () => request<Category[]>("/categories"),
    create: (body: CategoryInput) => request<Category>("/categories", { method: "POST", body: json(body) }),
    update: (id: number, body: CategoryInput) => request<Category>(`/categories/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/categories/${id}`, { method: "DELETE" }),
  },
  products: {
    list: (params?: { categoryId?: number; search?: string }) => request<Product[]>(`/products${qs(params)}`),
    create: (body: ProductInput) => request<Product>("/products", { method: "POST", body: json(body) }),
    update: (id: number, body: ProductInput) => request<Product>(`/products/${id}`, { method: "PUT", body: json(body) }),
    updatePrice: (id: number, body: { price: number; discountPercent: number; priceUsd?: number }) =>
      request<Product>(`/products/${id}/price`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/products/${id}`, { method: "DELETE" }),
  },
  stock: {
    summary: () => request<StockSummary[]>("/stock/summary"),
    items: (productId: number) => request<StockItem[]>(`/stock${qs({ productId })}`),
    content: (id: number) => request<{ content: string }>(`/stock/${id}/content`),
    add: (productId: number, lines: string[]) =>
      request<StockItem[]>("/stock", { method: "POST", body: json({ productId, lines }) }),
    disable: (id: number) => request<void>(`/stock/${id}/disable`, { method: "POST" }),
    enable: (id: number) => request<void>(`/stock/${id}/enable`, { method: "POST" }),
    release: (id: number) => request<void>(`/stock/${id}/release`, { method: "POST" }),
    remove: (id: number) => request<void>(`/stock/${id}`, { method: "DELETE" }),
    autoDeliver: (productId: number, enabled: boolean) =>
      request<void>("/stock/auto-deliver", { method: "POST", body: json({ productId, enabled }) }),
    pull: (orderId: number, unitId: number) =>
      request<{ stockItemId?: number; stockAccountId?: number; content: string }>(
        "/stock/pull", { method: "POST", body: json({ orderId, unitId }) }),
    accounts: (productId: number) => request<StockAccount[]>(`/stock/accounts${qs({ productId })}`),
    manageAccounts: (productId: number) => request<StockManagedAccount[]>(`/stock/accounts/manage${qs({ productId })}`),
    waiting: () => request<StockWaitingOrder[]>("/stock/waiting"),
    addAccount: (input: { productId: number; username: string; password: string; plan: string; planType: string; capacity: number; months: number }) =>
      request<StockAccount>("/stock/accounts", { method: "POST", body: json(input) }),
    serviceName: (productId: number, serviceName: string) =>
      request<void>("/stock/service-name", { method: "POST", body: json({ productId, serviceName }) }),
    reformatDeliveries: () =>
      request<{ updated: number }>("/stock/reformat-deliveries", { method: "POST" }),
    accountContent: (id: number) =>
      request<{ username: string; password: string }>(`/stock/accounts/${id}/content`),
    disableAccount: (id: number) => request<void>(`/stock/accounts/${id}/disable`, { method: "POST" }),
    enableAccount: (id: number) => request<void>(`/stock/accounts/${id}/enable`, { method: "POST" }),
    // A blank password keeps the stored one. Delivered units on this account are rebuilt server-side, so the
    // edit reaches the customers' panels right away.
    updateAccount: (id: number, input: { username: string; password: string; plan: string; planType: string; capacity: number; months: number }) =>
      request<StockAccount>(`/stock/accounts/${id}`, { method: "PUT", body: json(input) }),
    // `force` also removes accounts that hold delivered seats (expired subscription, test account).
    removeAccount: (id: number, force = false) =>
      request<void>(`/stock/accounts/${id}${force ? "?force=true" : ""}`, { method: "DELETE" }),
    slotAction: (accountId: number, slotId: number, action: "disable" | "enable" | "release") =>
      request<void>(`/stock/accounts/${accountId}/slots/${slotId}/${action}`, { method: "POST" }),
    slotFulfillment: (productId: number, enabled: boolean) =>
      request<void>("/stock/slot-fulfillment", { method: "POST", body: json({ productId, enabled }) }),
  },
  admin: {
    // Role-filtered sidebar + live badge counts for the signed-in staff member.
    menu: () => request<AdminNavGroup[]>("/admin/menu"),
  },
  chat: {
    mine: () => request<CustomerChatThread | null>("/chat/me"),
    myUnread: () => request<number>("/chat/me/unread"),
    send: (body: string) => request<CustomerChatThread>("/chat/me/messages", { method: "POST", body: json({ body }) }),
    readMine: () => request<void>("/chat/me/read", { method: "POST" }),
    resetMine: () => request<void>("/chat/me/reset", { method: "POST" }),
    list: () => request<ConversationSummary[]>("/chat"),
    get: (id: number) => request<AdminChatThread>(`/chat/${id}`),
    reply: (id: number, body: string) => request<AdminChatThread>(`/chat/${id}/messages`, { method: "POST", body: json({ body }) }),
    read: (id: number) => request<void>(`/chat/${id}/read`, { method: "POST" }),
    close: (id: number) => request<void>(`/chat/${id}/close`, { method: "POST" }),
  },
  staff: {
    list: () => request<StaffMember[]>("/staff"),
    permissions: () => request<PermissionInfo[]>("/staff/permissions"),
    // Grants staff access to an EXISTING account by username — no new email/password is created here.
    create: (body: { username: string; role: UserRole; permissions: string[] }) =>
      request<StaffMember>("/staff", { method: "POST", body: json(body) }),
    update: (id: number, body: { name?: string; email?: string; role?: UserRole; blocked?: boolean; permissions?: string[] }) =>
      request<StaffMember>(`/staff/${id}`, { method: "PUT", body: json(body) }),
    resetPassword: (id: number, password: string) =>
      request<void>(`/staff/${id}/password`, { method: "POST", body: json({ password }) }),
    // Owner rescue when a staff member loses their authenticator: turn their 2FA off without a code.
    disableTwoFactor: (id: number) => request<void>(`/staff/${id}/2fa/disable`, { method: "POST" }),
    remove: (id: number) => request<void>(`/staff/${id}`, { method: "DELETE" }),
  },
  users: {
    list: (params?: { search?: string; role?: UserRole; blocked?: boolean }) => request<User[]>(`/users${qs(params)}`),
    get: (id: number) => request<User>(`/users/${id}`),
    update: (id: number, body: UserUpdateInput) => request<User>(`/users/${id}`, { method: "PUT", body: json(body) }),
    adjustWallet: (id: number, body: WalletInput) => request<User>(`/users/${id}/wallet`, { method: "POST", body: json(body) }),
    remove: (id: number) => request<void>(`/users/${id}`, { method: "DELETE" }),
  },
  account: {
    me: () => request<User>("/account/me"),
    updateMe: (body: { name?: string; email?: string; phone?: string; username?: string; avatar?: string }) =>
      request<User>("/account/me", { method: "PUT", body: json(body) }),
    transactions: () => request<Transaction[]>("/account/transactions"),
    referrals: () => request<ReferralReport>("/account/referrals"),
    changePassword: (body: { currentPassword: string; newPassword: string }) =>
      request<void>("/account/password", { method: "PUT", body: json(body) }),
  },
  // Public image upload (avatars, site/admin imagery). Goes through the authenticated, CSRF-protected
  // backend endpoint and returns an absolute URL usable directly as an <img src>.
  media: {
    upload: (file: File) => uploadForm<{ url: string }>("/upload", file).then((r) => r.url),
  },
  // Per-seat information a buyer files after delivery (a picture + a note, one per seat of a shared account).
  // Images live in protected storage: they're referenced by opaque id and streamed from an ownership-checked
  // endpoint, never a public URL.
  seatInfo: {
    forUnit: (orderId: number, unitId: number) =>
      request<SeatUnitInfo>(`/seat-info/unit/${orderId}/${unitId}`),
    upload: (file: File) => uploadForm<{ id: string }>("/seat-info/upload", file).then((r) => r.id),
    save: (input: { orderId: number; unitId: number; seatIndex: number; seatLabel: string; imageId: string | null; text: string }) =>
      request<SeatSubmission>("/seat-info", { method: "POST", body: json(input) }),
    imageSrc: (id: string) => `${BASE}/api/seat-info/image/${encodeURIComponent(id)}`,
    // staff-only
    all: (status?: "Pending" | "Reviewed") => request<SeatSubmission[]>(`/seat-info${qs({ status })}`),
    review: (id: number, note?: string) =>
      request<SeatSubmission>(`/seat-info/${id}/review`, { method: "POST", body: json({ note: note ?? null }) }),
    reopen: (id: number, note?: string) =>
      request<SeatSubmission>(`/seat-info/${id}/reopen`, { method: "POST", body: json({ note: note ?? null }) }),
  },
  discounts: {
    list: () => request<DiscountCode[]>("/discounts"),
    create: (body: DiscountCodeInput) => request<DiscountCode>("/discounts", { method: "POST", body: json(body) }),
    update: (id: number, body: DiscountCodeInput) => request<DiscountCode>(`/discounts/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/discounts/${id}`, { method: "DELETE" }),
    validate: (code: string, subtotal: number) =>
      request<DiscountResult>("/discounts/validate", { method: "POST", body: json({ code, subtotal }) }),
  },
  planTypes: {
    list: () => request<string[]>("/plan-types"),
    add: (name: string) => request<string[]>("/plan-types", { method: "POST", body: json({ name }) }),
    rename: (oldName: string, newName: string) => request<string[]>("/plan-types/rename", { method: "PUT", body: json({ oldName, newName }) }),
    remove: (name: string) => request<string[]>(`/plan-types/${encodeURIComponent(name)}`, { method: "DELETE" }),
  },
  pricing: {
    getSettings: () => request<PricingSettings>("/pricing/settings"),
    updateSettings: (body: PricingSettings) => request<PricingSettings>("/pricing/settings", { method: "PUT", body: json(body) }),
    getPlans: () => request<Plan[]>("/pricing/plans"),
    createPlan: (body: PlanInput) => request<Plan>("/pricing/plans", { method: "POST", body: json(body) }),
    updatePlan: (id: number, body: PlanInput) => request<Plan>(`/pricing/plans/${id}`, { method: "PUT", body: json(body) }),
    removePlan: (id: number) => request<void>(`/pricing/plans/${id}`, { method: "DELETE" }),
    usdRate: () => request<UsdRateInfo>("/pricing/usd-rate"),
    refreshUsdRate: () => request<UsdRateInfo>("/pricing/usd-rate/refresh", { method: "POST" }),
    setManualUsdRate: (rate: number, auto: boolean) =>
      request<UsdRateInfo>("/pricing/usd-rate/manual", { method: "PUT", body: json({ rate, auto }) }),
  },
  hero: {
    list: () => request<HeroSlide[]>("/hero"),
    create: (body: HeroSlideInput) => request<HeroSlide>("/hero", { method: "POST", body: json(body) }),
    update: (id: number, body: HeroSlideInput) => request<HeroSlide>(`/hero/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/hero/${id}`, { method: "DELETE" }),
  },
  homeCategories: {
    list: () => request<HomeCategory[]>("/home-categories"),
    create: (body: HomeCategoryInput) => request<HomeCategory>("/home-categories", { method: "POST", body: json(body) }),
    update: (id: number, body: HomeCategoryInput) => request<HomeCategory>(`/home-categories/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/home-categories/${id}`, { method: "DELETE" }),
  },
  showcase: {
    list: () => request<Showcase[]>("/showcase"),
    create: (body: ShowcaseInput) => request<Showcase>("/showcase", { method: "POST", body: json(body) }),
    update: (id: number, body: ShowcaseInput) => request<Showcase>(`/showcase/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/showcase/${id}`, { method: "DELETE" }),
  },
  blog: {
    list: () => request<BlogPost[]>("/blog"),
    create: (body: BlogPostInput) => request<BlogPost>("/blog", { method: "POST", body: json(body) }),
    update: (id: number, body: BlogPostInput) => request<BlogPost>(`/blog/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/blog/${id}`, { method: "DELETE" }),
  },
  stats: {
    overview: () => request<OverviewStats>("/stats/overview"),
    topProducts: () => request<TopProductStat[]>("/stats/top-products"),
  },
  serverStatus: {
    get: () => request<ServerStatus>("/admin/server-status"),
  },
  cluster: {
    status: () => request<ClusterStatus>("/cluster/status"),
    promote: () => request<void>("/cluster/promote", { method: "POST" }),
    recover: () => request<void>("/cluster/recover", { method: "POST" }),
    resync: () => request<void>("/cluster/resync", { method: "POST" }),
    bootstrap: () => request<void>("/cluster/bootstrap", { method: "POST" }),
  },
  // The record of what the shop has sent. info@ receives nothing, so this is the only way to answer
  // "did this customer actually get their email?".
  emailLog: {
    list: (params?: {
      search?: string;
      status?: "sent" | "failed";
      from?: string;
      to?: string;
      page?: number;
      pageSize?: number;
    }) => request<SentEmailPage>(`/admin/email-log${qs(params)}`),
  },
  auditLogs: {
    list: (params?: {
      search?: string;
      action?: string;
      from?: string;
      to?: string;
      page?: number;
      pageSize?: number;
    }) => request<AuditLogPage>(`/admin/audit-logs${qs(params)}`),
  },
  logs: {
    list: () => request<LogFile[]>("/admin/logs"),
    // View/search a file's contents without downloading. tail = 0 means "all" (server-capped).
    view: (params: { name: string; tail?: number; search?: string }) =>
      request<LogView>(`/admin/logs/view${qs(params)}`),
    // SameSite=Strict cookie auth means a plain <a download> wouldn't carry the session — fetch as a blob.
    download: async (name: string): Promise<{ blob: Blob; filename: string }> => {
      const res = await fetch(`${BASE}/api/admin/logs/download?name=${encodeURIComponent(name)}`, {
        credentials: "include",
        cache: "no-store",
      });
      if (!res.ok) throw new Error(`خطا در دانلود فایل لاگ (${res.status})`);
      return { blob: await res.blob(), filename: name };
    },
    // Every log file as one zip.
    downloadAll: async (): Promise<{ blob: Blob; filename: string }> => {
      const res = await fetch(`${BASE}/api/admin/logs/download-all`, {
        credentials: "include",
        cache: "no-store",
      });
      if (!res.ok) throw new Error(`خطا در دانلود لاگ‌ها (${res.status})`);
      return { blob: await res.blob(), filename: "phonix-logs.zip" };
    },
  },
  orders: {
    list: (params?: { status?: OrderStatus }) => request<Order[]>(`/orders${qs(params)}`),
    // Issued invoices (completed orders only). `q` matches invoice number, order code or buyer name.
    invoices: (params?: { q?: string; page?: number; pageSize?: number }) =>
      request<PagedResult<Order>>(`/orders/invoices${qs(params)}`),
    forUser: (userId: number) => request<Order[]>(`/orders/user/${userId}`),
    get: (id: number) => request<Order>(`/orders/${id}`),
    invoice: (id: number) => request<Invoice>(`/orders/${id}/invoice`),
    place: (body: { items: { productId: number; quantity: number; planId?: number | null; units?: { inputs?: { label: string; value: string }[]; note?: string | null }[]; inputs?: { label: string; value: string }[]; note?: string | null }[]; paymentMethod: string; fromWallet?: boolean; discountCode?: string | null; paymentMethodId?: number | null; cardId?: number | null; receiptUrl?: string | null; trackingNumber?: string | null; paymentDate?: string | null; description?: string | null }) =>
      request<Order>("/orders", { method: "POST", body: json(body) }),
    approve: (id: number) => request<Order>(`/orders/${id}/approve`, { method: "POST" }),
    reject: (id: number, reason?: string) => request<Order>(`/orders/${id}/reject`, { method: "POST", body: json({ reason: reason ?? null }) }),
    complete: (id: number) => request<Order>(`/orders/${id}/complete`, { method: "POST" }),
    cancel: (id: number) => request<Order>(`/orders/${id}/cancel`, { method: "POST" }),
    deliver: (id: number, body: { content: string; email: boolean; emailSubject?: string; emailBody?: string }) =>
      request<Order>(`/orders/${id}/deliver`, { method: "POST", body: json(body) }),
    saveUnitDraft: (id: number, unitId: number, body: { content: string }) =>
      request<Order>(`/orders/${id}/units/${unitId}/draft`, { method: "POST", body: json({ ...body, email: false }) }),
    deliverUnit: (id: number, unitId: number, body: { content: string; email: boolean; emailSubject?: string; emailBody?: string; final?: boolean }) =>
      request<Order>(`/orders/${id}/units/${unitId}/deliver`, { method: "POST", body: json(body) }),
  },
  tickets: {
    list: (params?: { status?: TicketStatus }) => request<Ticket[]>(`/tickets${qs(params)}`),
    forUser: (userId: number) => request<Ticket[]>(`/tickets/user/${userId}`),
    get: (id: number) => request<Ticket>(`/tickets/${id}`),
    create: (body: { subject: string; department: string; body: string; priority?: TicketPriority; attachment?: string }) =>
      request<Ticket>("/tickets", { method: "POST", body: json(body) }),
    // Staff opens a ticket on behalf of a user; it lands in that user's account already answered.
    createForUser: (body: { userId: number; subject: string; department: string; body: string; priority?: TicketPriority; attachment?: string }) =>
      request<Ticket>("/tickets/admin", { method: "POST", body: json(body) }),
    reply: (id: number, body: string, isAdmin: boolean, attachment?: string) =>
      request<Ticket>(`/tickets/${id}/reply`, { method: "POST", body: json({ body, isAdmin, attachment: attachment || undefined }) }),
    close: (id: number) => request<void>(`/tickets/${id}/close`, { method: "POST" }),
  },
  kyc: {
    list: (params?: { status?: KycStatus }) => request<KycRequest[]>(`/kyc${qs(params)}`),
    getForUser: (userId: number) => request<KycRequest | null>(`/kyc/user/${userId}`),
    submit: (body: KycInput) => request<KycRequest>("/kyc", { method: "POST", body: json(body) }),
    approve: (id: number) => request<KycRequest>(`/kyc/${id}/approve`, { method: "POST" }),
    reject: (id: number, note?: string) => request<KycRequest>(`/kyc/${id}/reject`, { method: "POST", body: json({ note: note ?? null }) }),
    // uploads a KYC image to protected storage and returns its opaque id (stored as cardImage/selfieImage).
    upload: (file: File) => uploadForm<{ id: string }>("/kyc/upload", file).then((r) => r.id),
    imageSrc: (value: string) => protectedSrc("kyc", value),
  },
  siteContent: {
    get: () => request<SiteContent>("/site-content"),
    update: (body: SiteContent) => request<SiteContent>("/site-content", { method: "PUT", body: json(body) }),
  },
  advancedSettings: {
    get: () => request<AdvancedSettings>("/advanced-settings"),
    update: (body: AdvancedSettings) => request<AdvancedSettings>("/advanced-settings", { method: "PUT", body: json(body) }),
  },
  paymentMethods: {
    list: () => request<PaymentMethod[]>("/payment-methods"),
    create: (body: PaymentMethodInput) => request<PaymentMethod>("/payment-methods", { method: "POST", body: json(body) }),
    update: (id: number, body: PaymentMethodInput) => request<PaymentMethod>(`/payment-methods/${id}`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/payment-methods/${id}`, { method: "DELETE" }),
  },
  paymentSettings: {
    get: () => request<PaymentSettings>("/payment-settings"),
    update: (body: PaymentSettings) => request<PaymentSettings>("/payment-settings", { method: "PUT", body: json(body) }),
  },
  mailbox: {
    folders: () => request<MailFolder[]>("/mailbox/folders"),
    // The inbox as topic threads (INBOX + Sent merged), the way the panel's main view shows it.
    conversations: (params?: { page?: number; pageSize?: number; search?: string; unreadOnly?: boolean }) =>
      request<MailConversationPage>(`/mailbox/conversations${qs(params)}`),
    conversation: (id: string) => request<MailConversationDetail>(`/mailbox/conversations/${encodeURIComponent(id)}`),
    list: (params: { folder: string; page?: number; pageSize?: number; search?: string; unreadOnly?: boolean }) =>
      request<MailPage>(`/mailbox/messages${qs(params)}`),
    get: (folder: string, uid: number) => request<MailMessage>(`/mailbox/messages/${uid}${qs({ folder })}`),
    setSeen: (folder: string, uid: number, value: boolean) =>
      request<{ ok: boolean }>(`/mailbox/messages/${uid}/seen${qs({ folder })}`, { method: "POST", body: json({ value }) }),
    setFlagged: (folder: string, uid: number, value: boolean) =>
      request<{ ok: boolean }>(`/mailbox/messages/${uid}/flagged${qs({ folder })}`, { method: "POST", body: json({ value }) }),
    move: (folder: string, uid: number, target: string) =>
      request<{ ok: boolean }>(`/mailbox/messages/${uid}/move${qs({ folder })}`, { method: "POST", body: json({ target }) }),

    // The href for an attachment. It is a normal authenticated GET, so a plain <a download> works only
    // because the panel and API share the cookie; the endpoint forces a download disposition regardless.
    attachmentUrl: (folder: string, uid: number, index: number) =>
      `${BASE}/api/mailbox/messages/${uid}/attachments/${index}${qs({ folder })}`,

    // multipart, because a reply carries files. Content-Type is left to the browser so it can write the
    // multipart boundary (same reason as uploadForm above).
    send: async (input: {
      to: string[];
      cc?: string[];
      subject: string;
      body: string;
      replyToFolder?: string;
      inReplyToUid?: number;
      files?: File[];
    }): Promise<{ ok: boolean }> => {
      const csrf = getCsrfToken();
      const fd = new FormData();
      input.to.forEach((address) => fd.append("to", address));
      (input.cc ?? []).forEach((address) => fd.append("cc", address));
      fd.append("subject", input.subject);
      fd.append("body", input.body);
      if (input.replyToFolder) fd.append("replyToFolder", input.replyToFolder);
      if (input.inReplyToUid !== undefined) fd.append("inReplyToUid", String(input.inReplyToUid));
      (input.files ?? []).forEach((file) => fd.append("files", file));

      const res = await fetch(`${BASE}/api/mailbox/send`, {
        method: "POST",
        credentials: "include",
        cache: "no-store",
        headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}) },
        body: fd,
      });
      if (!res.ok) {
        if (res.status === 401) handleUnauthorized();
        let msg = `خطا در ارسال ایمیل (${res.status})`;
        try { const t = await res.text(); if (t) msg = t.replace(/^"|"$/g, ""); } catch { /* ignore */ }
        throw new Error(msg);
      }
      return (await res.json()) as { ok: boolean };
    },

    settings: {
      get: () => request<MailboxSettings>("/mailbox/settings"),
      update: (body: MailboxSettingsInput) =>
        request<MailboxSettings>("/mailbox/settings", { method: "PUT", body: json(body) }),
      test: () => request<{ ok: boolean }>("/mailbox/settings/test", { method: "POST" }),
    },
  },
  v2ray: {
    providers: () => request<V2RayProviderInfo[]>("/v2ray/providers"),
    panels: () => request<V2RayPanelInfo[]>("/v2ray/panels"),
    // Verify a URL + credentials without saving (the wizard's login/test button).
    test: (body: V2RayPanelInput) => request<{ ok: boolean; inboundCount: number }>("/v2ray/test", { method: "POST", body: json(body) }),
    add: (body: V2RayPanelInput) => request<V2RayPanelInfo>("/v2ray/panels", { method: "POST", body: json(body) }),
    testStored: (id: number) => request<{ ok: boolean; inboundCount: number }>(`/v2ray/panels/${id}/test`, { method: "POST" }),
    addClient: (id: number, body: { email: string; totalGb: number; limitIp: number; durationDays: number }) =>
      request<{ ok: boolean; uuid: string; subId: string; inboundsAdded: number }>(`/v2ray/panels/${id}/client`, { method: "POST", body: json(body) }),
    remove: (id: number) => request<{ ok: boolean }>(`/v2ray/panels/${id}`, { method: "DELETE" }),
  },
  emailSettings: {
    get: () => request<EmailSettings>("/email-settings"),
    update: (body: EmailSettings) => request<EmailSettings>("/email-settings", { method: "PUT", body: json(body) }),
    test: (to: string) => request<{ ok: boolean }>("/email-settings/test", { method: "POST", body: json({ to }) }),
  },
  backup: {
    // cookie auth is SameSite=Strict + cross-origin, so a plain <a download> link wouldn't carry it — fetch as a blob.
    // The server names the file (.json or, when encrypted, .phxbak); honor that name on the download.
    download: async (): Promise<{ blob: Blob; filename: string }> => {
      const res = await fetch(`${BASE}/api/backup/export`, { credentials: "include", cache: "no-store" });
      if (!res.ok) throw new Error(`خطا در دانلود پشتیبان (${res.status})`);
      const match = (res.headers.get("Content-Disposition") ?? "").match(/filename\*?="?([^";]+)"?/i);
      return { blob: await res.blob(), filename: match?.[1] ?? "phonix-backup.json" };
    },
    // Restore is gated server-side by a triple check: the backup file, manual re-entry of PHONIX_BACKUP_KEY,
    // and a fresh 2FA code. Sent as multipart so the file rides alongside the two re-auth factors.
    restore: async (file: File, backupKey: string, twoFactorCode: string): Promise<{ ok: boolean }> => {
      const csrf = getCsrfToken();
      const fd = new FormData();
      fd.append("file", file);
      fd.append("backupKey", backupKey);
      fd.append("twoFactorCode", twoFactorCode);
      const res = await fetch(`${BASE}/api/backup/restore`, {
        method: "POST",
        credentials: "include",
        cache: "no-store",
        headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}) },
        body: fd,
      });
      if (!res.ok) {
        let msg = `خطا در بازیابی (${res.status})`;
        try { const t = await res.text(); if (t) msg = t.replace(/^"|"$/g, ""); } catch { /* ignore */ }
        throw new Error(msg);
      }
      return (await res.json()) as { ok: boolean };
    },
    // Per-section backup: list of sections, recent backup history, and whether encryption is on.
    sections: () => request<{
      sections: { key: string; label: string }[];
      history: { section: string; target: string; ok: boolean; error: string; atUtc: string }[];
      encrypted: boolean;
    }>("/backup/sections"),
    downloadSection: async (key: string): Promise<{ blob: Blob; filename: string }> => {
      const res = await fetch(`${BASE}/api/backup/export/${key}`, { credentials: "include", cache: "no-store" });
      if (!res.ok) throw new Error(`خطا در دانلود پشتیبان (${res.status})`);
      const match = (res.headers.get("Content-Disposition") ?? "").match(/filename\*?="?([^";]+)"?/i);
      return { blob: await res.blob(), filename: match?.[1] ?? `phonix-${key}.phxbak` };
    },
    restoreSection: async (key: string, file: File, backupKey: string, twoFactorCode: string): Promise<{ ok: boolean }> => {
      const csrf = getCsrfToken();
      const fd = new FormData();
      fd.append("file", file);
      fd.append("backupKey", backupKey);
      fd.append("twoFactorCode", twoFactorCode);
      const res = await fetch(`${BASE}/api/backup/restore/${key}`, {
        method: "POST", credentials: "include", cache: "no-store",
        headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}) }, body: fd,
      });
      if (!res.ok) {
        let msg = `خطا در بازیابی (${res.status})`;
        try { const t = await res.text(); if (t) msg = t.replace(/^"|"$/g, ""); } catch { /* ignore */ }
        throw new Error(msg);
      }
      return (await res.json()) as { ok: boolean };
    },
    // Sends a real test message with the saved settings; rejects with Telegram's own error text on failure.
    testBot: (bot: "receipt" | "order") => request<{ ok: boolean }>(`/backup/telegram/test/${bot}`, { method: "POST" }),
    sendSection: (key: string) => request<{ ok: boolean }>(`/backup/telegram/send/${key}`, { method: "POST" }),
    sendAll: () => request<{ ok: boolean }>("/backup/telegram/send-all", { method: "POST" }),
    // kind: "site" (public images) | "documents" (users' encrypted cards/KYC/receipts). Sending media off
    // the server re-authenticates with the same two factors as a restore: the server's backup key and a fresh
    // 2FA code, sent as multipart alongside the request.
    sendMedia: async (kind: "site" | "documents", backupKey: string, twoFactorCode: string): Promise<{ ok: boolean }> => {
      const csrf = getCsrfToken();
      const fd = new FormData();
      fd.append("backupKey", backupKey);
      fd.append("twoFactorCode", twoFactorCode);
      const res = await fetch(`${BASE}/api/backup/telegram/media/${kind}`, {
        method: "POST", credentials: "include", cache: "no-store",
        headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}) }, body: fd,
      });
      if (!res.ok) {
        let msg = `خطا در ارسال (${res.status})`;
        try { const t = await res.text(); if (t) msg = t.replace(/^"|"$/g, ""); } catch { /* ignore */ }
        throw new Error(msg);
      }
      return (await res.json()) as { ok: boolean };
    },
    downloadMedia: async (kind: "public" | "sensitive"): Promise<{ blob: Blob; filename: string }> => {
      const res = await fetch(`${BASE}/api/backup/media/${kind}`, { credentials: "include", cache: "no-store" });
      if (!res.ok) throw new Error(`خطا در دانلود رسانه (${res.status})`);
      const match = (res.headers.get("Content-Disposition") ?? "").match(/filename\*?="?([^";]+)"?/i);
      return { blob: await res.blob(), filename: match?.[1] ?? `phonix-media-${kind}.zip` };
    },
    // full manual backup — everything (data + all media) in one file
    downloadFull: async (): Promise<{ blob: Blob; filename: string }> => {
      const res = await fetch(`${BASE}/api/backup/full`, { credentials: "include", cache: "no-store" });
      if (!res.ok) throw new Error(`خطا در دانلود پشتیبان کامل (${res.status})`);
      const match = (res.headers.get("Content-Disposition") ?? "").match(/filename\*?="?([^";]+)"?/i);
      return { blob: await res.blob(), filename: match?.[1] ?? "phonix-full.phxbak" };
    },
    restoreUpload: async (path: string, file: File, backupKey: string, twoFactorCode: string): Promise<{ ok: boolean }> => {
      const csrf = getCsrfToken();
      const fd = new FormData();
      fd.append("file", file);
      fd.append("backupKey", backupKey);
      fd.append("twoFactorCode", twoFactorCode);
      const res = await fetch(`${BASE}/api/backup/${path}`, {
        method: "POST", credentials: "include", cache: "no-store",
        headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}) }, body: fd,
      });
      if (!res.ok) {
        let msg = `خطا در بازیابی (${res.status})`;
        try { const t = await res.text(); if (t) msg = t.replace(/^"|"$/g, ""); } catch { /* ignore */ }
        throw new Error(msg);
      }
      return (await res.json()) as { ok: boolean };
    },
    telegram: {
      get: () => request<TelegramSettings>("/backup/telegram"),
      update: (body: TelegramSettings) => request<TelegramSettings>("/backup/telegram", { method: "PUT", body: json(body) }),
      test: () => request<{ ok: boolean }>("/backup/telegram/test", { method: "POST" }),
      testAlert: () => request<{ ok: boolean }>("/backup/telegram/test-alert", { method: "POST" }),
    },
  },
  transactions: {
    list: (params?: { status?: TxStatus }) => request<Transaction[]>(`/transactions${params?.status ? `?status=${params.status}` : ""}`),
    create: (body: { amount: number; cardId: number; method?: string; receiptUrl?: string | null; trackingNumber: string; paymentDate: string; description?: string | null }) =>
      request<Transaction>("/transactions", { method: "POST", body: json(body) }),
    withdraw: (body: { amount: number; destination: string }) =>
      request<Transaction>("/transactions/withdraw", { method: "POST", body: json(body) }),
    approve: (id: number, note?: string) => request<Transaction>(`/transactions/${id}/approve`, { method: "POST", body: json({ note: note ?? null }) }),
    reject: (id: number, note?: string) => request<Transaction>(`/transactions/${id}/reject`, { method: "POST", body: json({ note: note ?? null }) }),
    // uploads a bank-transfer receipt to protected storage and returns its opaque id (stored as receiptUrl).
    uploadReceipt: (file: File) => uploadForm<{ id: string }>("/transactions/upload-receipt", file).then((r) => r.id),
    receiptSrc: (value: string) => receiptSrc(value),
  },
  notifications: {
    mine: () => request<Notification[]>("/notifications"),
    unreadCount: () => request<number>("/notifications/unread-count"),
    markRead: () => request<void>("/notifications/read", { method: "POST" }),
    send: (body: { userId?: number | null; title: string; body: string; link?: string | null }) =>
      request<AdminNotification>("/notifications", { method: "POST", body: json(body) }),
    all: () => request<AdminNotification[]>("/notifications/all"),
    remove: (id: number) => request<void>(`/notifications/${id}`, { method: "DELETE" }),
  },
  cards: {
    forUser: (userId: number) => request<BankCard[]>(`/cards/user/${userId}`),
    list: (params?: { status?: BankCardStatus }) => request<BankCard[]>(`/cards${qs(params)}`),
    add: (body: { cardNumber: string; holderName: string; cardImage: string }) => request<BankCard>("/cards", { method: "POST", body: json(body) }),
    remove: (id: number) => request<void>(`/cards/${id}`, { method: "DELETE" }),
    approve: (id: number) => request<BankCard>(`/cards/${id}/approve`, { method: "POST" }),
    reject: (id: number, note?: string) => request<BankCard>(`/cards/${id}/reject`, { method: "POST", body: json({ note: note ?? null }) }),
    // uploads a bank-card photo to protected storage and returns its opaque id (stored as cardImage).
    upload: (file: File) => uploadForm<{ id: string }>("/cards/upload", file).then((r) => r.id),
    imageSrc: (value: string) => protectedSrc("cards", value),
  },
  captcha: {
    get: () => request<{ id: string; image: string }>("/captcha"),
  },
  auth: {
    register: (body: { name: string; username: string; email: string; password: string; referralCode?: string; captchaId?: string; captchaText?: string }) =>
      request<AuthResult>("/auth/register", { method: "POST", body: json(body) }),
    // `admin: true` marks an admin-PANEL login (requires 2FA, yields an admin-scoped session). The main-site
    // login omits it, so an admin signing into the public site is never asked for a second factor.
    login: (body: { identifier: string; password: string; captchaId?: string; captchaText?: string; admin?: boolean; remember?: boolean }) =>
      request<LoginResult>("/auth/login", { method: "POST", body: json(body) }),
    // Google Identity Services sign-in: posts the ID token (credential) for server-side verification.
    google: (credential: string) =>
      request<AuthResult>("/auth/google", { method: "POST", body: json({ credential }) }),
    // Confirms the current session is admin-scoped staff (403 otherwise). The admin shell uses this as its gate.
    adminContext: () => request<{ id: number; name: string; username: string; role: UserRole }>("/auth/admin-context"),
    verifyTwoFactor: (token: string, code: string) =>
      request<LoginResult>("/auth/2fa/verify", { method: "POST", body: json({ token, code }) }),
    twoFactor: {
      status: () => request<TwoFactorStatus>("/auth/2fa/status"),
      setup: () => request<TwoFactorSetup>("/auth/2fa/setup", { method: "POST" }),
      enable: (code: string) => request<void>("/auth/2fa/enable", { method: "POST", body: json({ code }) }),
      disable: (code: string) => request<void>("/auth/2fa/disable", { method: "POST", body: json({ code }) }),
    },
    logout: () => request<void>("/auth/logout", { method: "POST" }),
    forgot: (email: string) => request<{ ok: boolean }>("/auth/forgot", { method: "POST", body: json({ email }) }),
    verifyEmail: (token: string) => request<{ ok: boolean }>("/auth/verify-email", { method: "POST", body: json({ token }) }),
    resendVerification: () => request<{ ok: boolean }>("/auth/resend-verification", { method: "POST" }),
    resetPassword: (token: string, newPassword: string) =>
      request<{ ok: boolean }>("/auth/reset-password", { method: "POST", body: json({ token, newPassword }) }),
  },
  favorites: {
    ids: (userId: number) => request<number[]>(`/favorites/user/${userId}`),
    toggle: (productId: number) =>
      request<{ favorited: boolean }>("/favorites/toggle", { method: "POST", body: json({ productId }) }),
  },
  comments: {
    list: (params?: { status?: CommentStatus; productId?: number }) => request<Comment[]>(`/comments${qs(params)}`),
    forProduct: (productId: number) => request<Comment[]>(`/products/${productId}/comments`),
    submit: (body: CommentInput) => request<Comment>("/comments", { method: "POST", body: json(body) }),
    approve: (id: number) => request<void>(`/comments/${id}/approve`, { method: "POST" }),
    reject: (id: number) => request<void>(`/comments/${id}/reject`, { method: "POST" }),
    reply: (id: number, body: string) => request<Comment>(`/comments/${id}/reply`, { method: "POST", body: json({ body }) }),
    setHome: (id: number, on: boolean) => request<void>(`/comments/${id}/home`, { method: "POST", body: json({ on }) }),
    remove: (id: number) => request<void>(`/comments/${id}`, { method: "DELETE" }),
  },
  // Public: approved reviews the admin flagged for the home-page carousel.
  testimonials: {
    list: () => request<Comment[]>("/testimonials"),
  },
};
