export type UserRole = "Customer" | "Support" | "Admin";

export type Category = {
  id: number;
  name: string;
  slug: string;
  icon: string;
  isActive: boolean;
  sortOrder: number;
  productCount: number;
};

export type CategoryInput = {
  name: string;
  slug: string;
  icon: string;
  isActive: boolean;
  sortOrder: number;
};

export type ProductFeature = { text: string; included: boolean };

export type ProductPlan = {
  id: number;
  type: string;
  months: number;
  price: number;
  discountPercent: number;
  isActive: boolean;
  finalPrice: number;
};

export type ProductPlanInput = {
  type: string;
  months: number;
  price: number;
  discountPercent: number;
  isActive: boolean;
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
  sku: string;
  description: string;
  warning: string;
  requiredLevel: number;
  deliveryTemplate: string;
  features: ProductFeature[];
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
  sku: string;
  description: string;
  warning: string;
  requiredLevel: number;
  deliveryTemplate: string;
  features: ProductFeature[];
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
};

export type PlanInput = {
  label: string;
  months: number;
  price: number;
  discountPercent: number;
};

export type HeroSlide = {
  id: number;
  title: string;
  description: string;
  image: string;
  logo: string;
  buttonText: string;
  buttonLink: string;
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
  sortOrder: number;
  isActive: boolean;
};

export type NavLinkItem = { label: string; href: string; hasMenu?: boolean };
export type StatItem = { value: string | null; label: string; icon: string | null };
export type SocialLink = { label: string; icon: string; href: string };

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
    socials: SocialLink[];
    copyright: string;
  };
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
  botToken: string;
  chatId: string;
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
  date: string;
};

export type CommentInput = {
  productId: number;
  body: string;
  rating: number;
  parentId?: number | null;
};

export type OrderStatus = "PendingApproval" | "Preparing" | "Completed" | "Cancelled";

export type OrderItem = {
  productId: number;
  name: string;
  image: string;
  plan: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
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
  deliveryContent: string | null;
  deliveredAt: string | null;
  deliveredAtUtc: string | null;
  renewalReminderSentUtc: string | null;
  history: OrderStatusHistory[];
};

export type TicketStatus = "Open" | "Answered" | "Closed";
export type TicketPriority = "Low" | "Medium" | "High";

export type TicketMessage = { author: string; body: string; isAdmin: boolean; date: string };

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
