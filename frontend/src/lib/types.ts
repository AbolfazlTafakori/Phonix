export type UserRole = "Customer" | "Support" | "Admin";

export type Category = {
  id: number;
  name: string;
  slug: string;
  icon: string;
  description: string;
  isActive: boolean;
  sortOrder: number;
  productCount: number;
};

export type CategoryInput = {
  name: string;
  slug: string;
  icon: string;
  description: string;
  isActive: boolean;
  sortOrder: number;
};

export type ProductFeature = { text: string; included: boolean };

export type ProductFaq = { question: string; answer: string };

export type PlanFieldType = "text" | "email" | "password" | "phone" | "textarea";

export type PlanInputField = {
  label: string;
  type: PlanFieldType;
  required: boolean;
  sensitive: boolean;
};

export type PlanTutorialMedia = {
  kind: "image" | "video";
  id: string;
};

// Per-plan "collect info from the customer" settings, shared by ProductPlan and ProductPlanInput.
export type PlanInfoSettings = {
  collectsInfo: boolean;
  inputFields: PlanInputField[];
  warningText: string;
  tutorialText: string;
  tutorialMedia: PlanTutorialMedia[];
  allowNotes: boolean;
};

export type ProductPlan = PlanInfoSettings & {
  id: number;
  type: string;
  months: number;
  price: number;
  priceUsd: number;
  discountPercent: number;
  isActive: boolean;
  userCount: number;
  rules: string;
  finalPrice: number;
};

export type ProductPlanInput = PlanInfoSettings & {
  type: string;
  months: number;
  price: number;
  priceUsd: number;
  discountPercent: number;
  isActive: boolean;
  userCount: number;
  rules: string;
};

export type Product = {
  id: number;
  name: string;
  categoryId: number;
  categoryName: string;
  price: number;
  discountPercent: number;
  finalPrice: number;
  stock: number;
  isActive: boolean;
  featured: boolean;
  image: string;
  logo: string;
  listImage: string;
  gallery: string[];
  sku: string;
  description: string;
  warning: string;
  requiredLevel: number;
  deliveryTemplate: string;
  priceUsd: number;
  features: ProductFeature[];
  faq: ProductFaq[];
  plans: ProductPlan[];
};

export type ProductInput = {
  name: string;
  categoryId: number;
  price: number;
  discountPercent: number;
  stock: number;
  isActive: boolean;
  featured: boolean;
  image: string;
  logo: string;
  listImage: string;
  gallery: string[];
  sku: string;
  description: string;
  warning: string;
  requiredLevel: number;
  deliveryTemplate: string;
  priceUsd: number;
  features: ProductFeature[];
  faq: ProductFaq[];
  plans: ProductPlanInput[];
};

export type User = {
  id: number;
  code: string;
  name: string;
  username: string;
  email: string;
  phone: string;
  avatar: string;
  role: UserRole;
  orders: number;
  totalSpent: number;
  wallet: number;
  verified: boolean;
  verificationLevel: number;
  emailVerified: boolean;
  blocked: boolean;
  joinedAt: string;
  note: string | null;
};

export type AuthResult = { token: string; user: User };

export type LoginResult = {
  requiresTwoFactor: boolean;
  challengeToken: string | null;
  token: string | null;
  user: User | null;
};

export type TwoFactorStatus = { enabled: boolean };
export type TwoFactorSetup = { secret: string; otpAuthUri: string };

export type StaffMember = {
  id: number;
  code: string;
  name: string;
  username: string;
  email: string;
  role: UserRole;
  blocked: boolean;
  twoFactorEnabled: boolean;
  permissions: string[];
};

export type PermissionInfo = { key: string; title: string; group: string };

export type ChatMessage = {
  id: number;
  fromAdmin: boolean;
  authorName: string;
  body: string;
  createdAtUtc: string;
};

// The full thread as the staff inbox sees it, including support-side read state.
export type AdminChatThread = {
  id: number;
  userId: number;
  userName: string;
  status: "Open" | "Closed";
  createdAtUtc: string;
  lastMessageAtUtc: string;
  userReadUpTo: number;
  adminReadUpTo: number;
  messages: ChatMessage[];
};

// What the customer widget receives — mirrors the server's ChatThreadDto. Includes adminReadUpTo so the
// widget can show delivered/read ticks and a "support is responding" hint.
export type CustomerChatThread = AdminChatThread;

export type ConversationSummary = {
  id: number;
  userId: number;
  userName: string;
  status: string;
  lastMessageAtUtc: string;
  lastPreview: string;
  unread: number;
};

export type ReferralEarning = {
  referrerId: number;
  referredName: string;
  orderCode: string;
  orderAmount: number;
  commission: number;
  date: string;
};

export type ReferralReport = { totalEarned: number; referredCount: number; earnings: ReferralEarning[] };

export type DiscountType = "Percent" | "Fixed";

export type DiscountCode = {
  id: number;
  code: string;
  type: DiscountType;
  value: number;
  minOrder: number;
  maxDiscount: number;
  usageLimit: number;
  usedCount: number;
  isActive: boolean;
  expiresAt: string | null;
};

export type DiscountCodeInput = Omit<DiscountCode, "id" | "usedCount">;

export type DiscountResult = { valid: boolean; amount: number; finalTotal: number; message: string | null };

export type UserUpdateInput = Partial<{
  name: string;
  email: string;
  phone: string;
  role: UserRole;
  verified: boolean;
  blocked: boolean;
  note: string;
  verificationLevel: number;
}>;

export type WalletInput = { amount: number; reason?: string };

// Admin sidebar served by GET /api/admin/menu — role-filtered and badge-counted server-side.
export type AdminNavItem = { key: string; title: string; icon: string; route: string; comingSoon: boolean; badge: number };
export type AdminNavGroup = { key: string; title: string; items: AdminNavItem[] };

// Mirrors the backend PagedResult<T> (Dtos.cs) — totalPages is computed server-side.
export type PagedResult<T> = {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

export type PricingSettings = {
  referralCommissionPercent: number;
  vatPercent: number;
  gatewayFeePercent: number;
  cancellationPenaltyPercent: number;
  minWalletCharge: number;
  minWithdraw: number;
  subscriptionReminderHoursBefore: number;
  currency: string;
  showOriginalPrice: boolean;
};

export type Plan = {
  id: number;
  label: string;
  months: number;
  price: number;
  discountPercent: number;
  finalPrice: number;
  priceUsd: number;
};

export type PlanInput = {
  label: string;
  months: number;
  price: number;
  discountPercent: number;
  priceUsd?: number;
};

export type UsdRateInfo = {
  tomanPerUsd: number; // effective rate prices use
  nobitex: number; // last live value from Nobitex (0 if unavailable)
  manual: number; // admin-set manual rate
  auto: boolean; // true = use Nobitex (fallback to manual), false = always manual
  updatedAtUnixMs: number;
  lastError: string; // why the last auto-fetch failed (empty when ok)
};

export type HeroSlide = {
  id: number;
  title: string;
  description: string;
  image: string;
  logo: string;
  buttonText: string;
  buttonLink: string;
  // Premium hero extras — all optional; the slide renders fine when they're blank/null.
  eyebrow: string;
  badge: string;
  priceFrom: number | null;
  oldPrice: number | null;
  secondaryButtonText: string;
  secondaryButtonLink: string;
  accentColor: string;
  // Size multiplier for the accent glow halo; null → default 1×.
  accentScale: number | null;
  // Per-banner trust badges + the colour of their chip halo/icon (empty → falls back to accentColor).
  trust: TrustItem[];
  trustColor: string;
  sortOrder: number;
  isActive: boolean;
};

export type HomeCategory = {
  id: number;
  title: string;
  icon: string;
  href: string;
  iconClass: string;
  sortOrder: number;
  isActive: boolean;
};

export type Showcase = {
  id: number;
  name: string;
  image: string;
  logo: string | null;
  href: string;
  sortOrder: number;
  isActive: boolean;
};

export type BlogPost = {
  id: number;
  slug: string;
  tag: string;
  title: string;
  excerpt: string;
  content: string;
  date: string;
  image: string;
  featuredOnHome: boolean;
  sortOrder: number;
  isActive: boolean;
};

export type NavLinkItem = { label: string; href: string; hasMenu?: boolean };
export type StatItem = { value: string | null; label: string; icon: string | null };
export type TrustItem = { icon: string; label: string };
export type SocialLink = { label: string; icon: string; href: string };
export type FooterColumn = { title: string; links: NavLinkItem[] };
export type FooterContact = { phone: string; email: string; hours: string; address: string };
export type TrustSeal = { title: string; subtitle: string; link: string; enabled: boolean };

export type SiteContent = {
  brand: { siteName: string; logoLine1: string; logoLine2: string; logo: string };
  header: {
    searchPlaceholder: string;
    cartLabel: string;
    cartLink: string;
    accountLabel: string;
    accountLink: string;
    navLinks: NavLinkItem[];
  };
  stats: StatItem[];
  sections: { categoriesTitle: string; bestSellersTitle: string; blogTitle: string };
  footer: {
    aboutTitle: string;
    aboutText: string;
    linksTitle: string;
    links: NavLinkItem[];
    columns: FooterColumn[];
    contact: FooterContact;
    trustSeals: TrustSeal[];
    socials: SocialLink[];
    copyright: string;
  };
  blogAutoplaySeconds: number;
  testimonialsEnabled: boolean;
  testimonialsAutoplaySeconds: number;
};

export type AdvancedSettings = {
  metaTitle: string;
  metaDescription: string;
  metaKeywords: string;
  maintenanceMode: boolean;
  maintenanceTitle: string;
  maintenanceMessage: string;
  analyticsId: string;
  customHeadScript: string;
  terms: string;
};

// Admin audit trail (GET /api/admin/audit-logs). ActionType drives the row badge colour.
export type AuditAction = "Create" | "Update" | "Delete" | "Other";

export type AuditLog = {
  id: number;
  actionType: AuditAction;
  entity: string;
  entityId: string | null;
  actorId: number | null;
  actorName: string;
  actorRole: string;
  method: string;
  path: string;
  ip: string;
  statusCode: number;
  success: boolean;
  timestamp: string;
};

export type AuditLogPage = {
  items: AuditLog[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

// Live process metrics for the dashboard server-status widget (GET /api/admin/server-status).
export type ServerStatus = {
  cpuPercent: number;
  ramUsedMb: number;
  ramTotalMb: number;
  uptimeDays: number;
  uptimeHours: number;
  uptimeMinutes: number;
  status: string;
};

// One Serilog file surfaced by the admin "system logs" page (GET /api/admin/logs).
export type LogFile = {
  name: string;
  sizeBytes: number;
  lastModifiedUtc: string;
};

// A single parsed log entry shown in the in-page viewer.
export type LogLine = {
  timestamp: string;
  level: string;
  message: string;
  raw: string;
};

// The result of viewing/searching a log file (newest entries first; GET /api/admin/logs/view).
export type LogView = {
  name: string;
  totalMatches: number;
  returned: number;
  lines: LogLine[];
};

export type PaymentType = "Card" | "Crypto" | "Gateway";

export type PaymentMethod = {
  id: number;
  type: PaymentType;
  title: string;
  holder: string;
  value: string;
  network: string;
  sheba: string;
  accountNumber: string;
  instructions: string;
  feePercent: number;
  isActive: boolean;
  sortOrder: number;
};

export type PaymentMethodInput = Omit<PaymentMethod, "id">;

export type EmailSettings = {
  enabled: boolean;
  host: string;
  port: number;
  username: string;
  password: string;
  fromEmail: string;
  fromName: string;
  useSsl: boolean;
};

export type PaymentSettings = {
  telegramEnabled: boolean;
  telegramBotToken: string;
  telegramChatId: string;
  requireReceipt: boolean;
  autoApproveUnder: number;
};

export type TelegramSettings = {
  backupEnabled: boolean;
  alertsEnabled: boolean;
  receiptBotEnabled: boolean;
  botToken: string;
  chatId: string;
  receiptBotToken: string;
  receiptChatId: string;
  intervalHours: number;
  lastBackupAtUtc: string | null;
  lastBackupError: string;
};

export type CommentStatus = "Pending" | "Approved" | "Rejected";

export type Comment = {
  id: number;
  productId: number;
  userName: string;
  body: string;
  rating: number;
  status: CommentStatus;
  parentId: number | null;
  isAdminReply: boolean;
  featuredOnHome: boolean;
  date: string;
};

export type CommentInput = {
  productId: number;
  body: string;
  rating: number;
  parentId?: number | null;
};

export type OrderStatus = "PendingApproval" | "Preparing" | "Completed" | "Cancelled";

export type OrderInputValue = {
  label: string;
  value: string;
  sensitive: boolean;
};

export type OrderItem = {
  productId: number;
  name: string;
  image: string;
  plan: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  customerInputs: OrderInputValue[];
  customerNote: string | null;
};

export type OrderUnit = {
  id: number;
  productId: number;
  name: string;
  image: string;
  plan: string | null;
  unitIndex: number;
  customerInputs: OrderInputValue[];
  customerNote: string | null;
  deliveryContent: string;
  delivered: boolean;
  deliveredAt: string | null;
  deliveredAtUtc: string | null;
  handledBy: string | null;
};

export type OrderStatusHistory = {
  id: number;
  orderId: number;
  changedByUsername: string;
  fromStatus: OrderStatus;
  toStatus: OrderStatus;
  reason: string | null;
  changedAtUtc: string;
};

export type Order = {
  id: number;
  code: string;
  userId: number;
  userName: string;
  items: OrderItem[];
  units: OrderUnit[];
  subtotal: number;
  discountCode: string | null;
  discountAmount: number;
  walletPaid: number;
  vatAmount: number;
  feeAmount: number;
  total: number;
  status: OrderStatus;
  paymentMethod: string;
  receiptUrl: string | null;
  date: string;
  note: string | null;
  // 16-digit invoice number, present only once the order is completed (delivered).
  invoiceNumber: string | null;
  deliveryContent: string | null;
  deliveredAt: string | null;
  deliveredAtUtc: string | null;
  renewalReminderSentUtc: string | null;
  history: OrderStatusHistory[];
};

export type TicketStatus = "Open" | "Answered" | "Closed";
export type TicketPriority = "Low" | "Medium" | "High";

export type TicketMessage = { author: string; body: string; isAdmin: boolean; date: string; attachment?: string };

export type Ticket = {
  id: number;
  code: string;
  userId: number;
  userName: string;
  subject: string;
  department: string;
  priority: TicketPriority;
  attachment: string;
  status: TicketStatus;
  messages: TicketMessage[];
  date: string;
};

export type OverviewStats = {
  revenue: number;
  ordersCount: number;
  pendingOrders: number;
  preparingOrders: number;
  completedOrders: number;
  usersCount: number;
  productsCount: number;
  openTickets: number;
  pendingComments: number;
  pendingKyc: number;
};

export type TopProductStat = { productId: number; name: string; image: string; sold: number; revenue: number };

export type KycStatus = "Pending" | "Approved" | "Rejected";

export type KycRequest = {
  id: number;
  userId: number;
  fullName: string;
  nationalId: string;
  birthDate: string;
  cardImage: string;
  selfieImage: string;
  status: KycStatus;
  note: string | null;
  date: string;
};

export type KycInput = {
  fullName: string;
  nationalId: string;
  birthDate: string;
  cardImage: string;
  selfieImage: string;
};

export type TxStatus = "Pending" | "Approved" | "Rejected";

export type Transaction = {
  id: number;
  code: string;
  userId: number;
  userName: string;
  type: string;
  amount: number;
  status: TxStatus;
  method: string;
  receiptUrl: string | null;
  sourceCard: string | null;
  trackingNumber: string | null;
  paymentDate: string | null;
  description: string | null;
  approvedVia: string | null;
  date: string;
  note: string | null;
};

export type Notification = {
  id: number;
  title: string;
  body: string;
  link: string | null;
  isPublic: boolean;
  isRead: boolean;
  createdAtUtc: string;
};

export type AdminNotification = {
  id: number;
  userId: number | null;
  title: string;
  body: string;
  link: string | null;
  createdAtUtc: string;
  readBy: number[];
};

export type BankCardStatus = "Pending" | "Approved" | "Rejected";

export type BankCard = {
  id: number;
  userId: number;
  userName: string;
  cardNumber: string;
  holderName: string;
  cardImage: string;
  bank: string;
  sheba: string | null;
  status: BankCardStatus;
  note: string | null;
  date: string;
};

export type HeroSlideInput = Omit<HeroSlide, "id">;
export type HomeCategoryInput = Omit<HomeCategory, "id">;
export type ShowcaseInput = Omit<Showcase, "id">;
export type BlogPostInput = Omit<BlogPost, "id">;
