import type {
  Category,
  CategoryInput,
  Product,
  ProductInput,
  User,
  UserRole,
  UserUpdateInput,
  WalletInput,
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
  Transaction,
  TxStatus,
  Comment,
  CommentInput,
  CommentStatus,
  KycRequest,
  KycInput,
  KycStatus,
} from "./types";

const BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}/api${path}`, {
    headers: { "Content-Type": "application/json" },
    cache: "no-store",
    ...init,
  });
  if (!res.ok) {
    let msg = `خطای ارتباط با سرور (${res.status})`;
    try {
      const text = await res.text();
      if (text) msg = text.replace(/^"|"$/g, "");
    } catch {
      // ignore
    }
    throw new Error(msg);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
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
    updatePrice: (id: number, body: { price: number; discountPercent: number }) =>
      request<Product>(`/products/${id}/price`, { method: "PUT", body: json(body) }),
    remove: (id: number) => request<void>(`/products/${id}`, { method: "DELETE" }),
  },
  users: {
    list: (params?: { search?: string; role?: UserRole; blocked?: boolean }) => request<User[]>(`/users${qs(params)}`),
    get: (id: number) => request<User>(`/users/${id}`),
    update: (id: number, body: UserUpdateInput) => request<User>(`/users/${id}`, { method: "PUT", body: json(body) }),
    adjustWallet: (id: number, body: WalletInput) => request<User>(`/users/${id}/wallet`, { method: "POST", body: json(body) }),
    remove: (id: number) => request<void>(`/users/${id}`, { method: "DELETE" }),
  },
  pricing: {
    getSettings: () => request<PricingSettings>("/pricing/settings"),
    updateSettings: (body: PricingSettings) => request<PricingSettings>("/pricing/settings", { method: "PUT", body: json(body) }),
    getPlans: () => request<Plan[]>("/pricing/plans"),
    createPlan: (body: PlanInput) => request<Plan>("/pricing/plans", { method: "POST", body: json(body) }),
    updatePlan: (id: number, body: PlanInput) => request<Plan>(`/pricing/plans/${id}`, { method: "PUT", body: json(body) }),
    removePlan: (id: number) => request<void>(`/pricing/plans/${id}`, { method: "DELETE" }),
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
  kyc: {
    list: (params?: { status?: KycStatus }) => request<KycRequest[]>(`/kyc${qs(params)}`),
    getForUser: (userId: number) => request<KycRequest | null>(`/kyc/user/${userId}`),
    submit: (body: KycInput) => request<KycRequest>("/kyc", { method: "POST", body: json(body) }),
    approve: (id: number) => request<KycRequest>(`/kyc/${id}/approve`, { method: "POST" }),
    reject: (id: number, note?: string) => request<KycRequest>(`/kyc/${id}/reject`, { method: "POST", body: json({ note: note ?? null }) }),
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
  transactions: {
    list: (params?: { status?: TxStatus }) => request<Transaction[]>(`/transactions${params?.status ? `?status=${params.status}` : ""}`),
    create: (body: { userName: string; type: string; amount: number; method: string }) =>
      request<Transaction>("/transactions", { method: "POST", body: json(body) }),
    approve: (id: number, note?: string) => request<Transaction>(`/transactions/${id}/approve`, { method: "POST", body: json({ note: note ?? null }) }),
    reject: (id: number, note?: string) => request<Transaction>(`/transactions/${id}/reject`, { method: "POST", body: json({ note: note ?? null }) }),
  },
  auth: {
    register: (body: { name: string; username: string; email: string; phone: string; password: string }) =>
      request<User>("/auth/register", { method: "POST", body: json(body) }),
    login: (body: { identifier: string; password: string }) =>
      request<User>("/auth/login", { method: "POST", body: json(body) }),
  },
  comments: {
    list: (params?: { status?: CommentStatus; productId?: number }) => request<Comment[]>(`/comments${qs(params)}`),
    forProduct: (productId: number) => request<Comment[]>(`/products/${productId}/comments`),
    submit: (body: CommentInput) => request<Comment>("/comments", { method: "POST", body: json(body) }),
    approve: (id: number) => request<void>(`/comments/${id}/approve`, { method: "POST" }),
    reject: (id: number) => request<void>(`/comments/${id}/reject`, { method: "POST" }),
    reply: (id: number, body: string) => request<Comment>(`/comments/${id}/reply`, { method: "POST", body: json({ body }) }),
    remove: (id: number) => request<void>(`/comments/${id}`, { method: "DELETE" }),
  },
};
