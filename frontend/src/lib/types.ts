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
  features: ProductFeature[];
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
  features: ProductFeature[];
};

export type User = {
  id: number;
  code: string;
  name: string;
  username: string;
  email: string;
  phone: string;
  role: UserRole;
  orders: number;
  totalSpent: number;
  wallet: number;
  verified: boolean;
  blocked: boolean;
  joinedAt: string;
  note: string | null;
};

export type UserUpdateInput = Partial<{
  name: string;
  email: string;
  phone: string;
  role: UserRole;
  verified: boolean;
  blocked: boolean;
  note: string;
}>;

export type WalletInput = { amount: number; reason?: string };

export type PricingSettings = {
  referralCommissionPercent: number;
  vatPercent: number;
  gatewayFeePercent: number;
  minWalletCharge: number;
  minWithdraw: number;
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
};

export type PaymentType = "Card" | "Crypto" | "Gateway";

export type PaymentMethod = {
  id: number;
  type: PaymentType;
  title: string;
  holder: string;
  value: string;
  network: string;
  instructions: string;
  isActive: boolean;
  sortOrder: number;
};

export type PaymentMethodInput = Omit<PaymentMethod, "id">;

export type PaymentSettings = {
  telegramEnabled: boolean;
  telegramBotToken: string;
  telegramChatId: string;
  requireReceipt: boolean;
  autoApproveUnder: number;
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
  userName: string;
  body: string;
  rating: number;
  parentId?: number | null;
};

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
  userId: number;
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
  userName: string;
  type: string;
  amount: number;
  status: TxStatus;
  method: string;
  receiptUrl: string | null;
  approvedVia: string | null;
  date: string;
  note: string | null;
};

export type HeroSlideInput = Omit<HeroSlide, "id">;
export type HomeCategoryInput = Omit<HomeCategory, "id">;
export type ShowcaseInput = Omit<Showcase, "id">;
export type BlogPostInput = Omit<BlogPost, "id">;
