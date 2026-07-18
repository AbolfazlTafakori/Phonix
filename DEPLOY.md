# استقرار Phonix (Docker)

کل سرویس (API + فروشگاه) با Docker Compose روی یک سرور Ubuntu/Debian بالا می‌آید.

## نصب یک‌خطی (سرور تازه، مثلاً Hetzner)

روی سروری که فقط IP دارد:

```bash
curl -fsSL https://raw.githubusercontent.com/<user>/<repo>/main/scripts/install.sh \
  | sudo REPO_URL=https://github.com/<user>/<repo>.git bash
```

اسکریپت Docker را نصب می‌کند، ریپو را در `/opt/phonix` کلون می‌کند، یک `.env` با IP سرور می‌سازد و کانتینرها را بالا می‌آورد. **وسط کار، نام‌کاربری و رمز اکانت مالک/ادمین را همین‌جا روی ترمینال از شما می‌پرسد** — این تنها لحظه‌ای‌ست که این اطلاعات تعیین می‌شوند، جای دیگری خودکار ساخته یا نمایش داده نمی‌شوند. در پایان آدرس فروشگاه و API را چاپ می‌کند.

با دامنه (پشت reverse proxy با TLS):

```bash
curl -fsSL https://raw.githubusercontent.com/<user>/<repo>/main/scripts/install.sh \
  | sudo REPO_URL=https://github.com/<user>/<repo>.git DOMAIN=example.com bash
```

## نصب دستی

```bash
git clone https://github.com/<user>/<repo>.git /opt/phonix
cd /opt/phonix
cp .env.example .env      # مقادیر را ویرایش کنید (URLها و پورت‌ها)
docker compose up -d --build
```

## اجرای محلی (توسعه)

برای اینکه هم‌زمان API و فروشگاه را با یک دستور بالا بیاوری (به همان ترتیبِ سرور):

```powershell
# ویندوز (PowerShell) — هرکدام در پنجره‌ی جدا با لاگ زنده
./scripts/dev.ps1
```

```bash
# لینوکس / مک / Git Bash — Ctrl+C هر دو را می‌بندد
bash scripts/dev.sh
```

API روی `http://localhost:5228` و فروشگاه روی `http://localhost:3000` بالا می‌آید. ورودهای تستی: مدیر `reza/1234`، کاربر `ali/1234`.

> اگر بک‌اند بالا نباشد، لاگین و هر صفحه‌ای که به API نیاز دارد کار نمی‌کند — حتماً هر دو با هم اجرا شوند.

## راه‌اندازی خودکار بعد از ریست سرور

روی سرور دو لایه این را تضمین می‌کنند و نیازی به دخالت دستی بعد از ریبوت نیست:

1. کانتینرها با `restart: unless-stopped` تعریف شده‌اند و سرویس Docker روی بوت `enable` است، پس بعد از ریبوت خودکار برمی‌گردند.
2. اسکریپت نصب یک یونیت systemd به نام `phonix.service` می‌سازد و enable می‌کند که موقع بوت (بعد از `docker.service`) دستور `docker compose up -d` را اجرا می‌کند.

کنترل دستی:

```bash
systemctl status phonix     # وضعیت
systemctl restart phonix    # رفرش کل استک
systemctl stop phonix       # توقف (docker compose down)
journalctl -u phonix        # لاگ راه‌اندازی
```

ترتیب اجرا (اول بک‌اند، بعد فرانت) را خودِ Compose با `depends_on` رعایت می‌کند.

## پیکربندی (`.env`)

| متغیر | توضیح |
|------|-------|
| `NEXT_PUBLIC_API_URL` | آدرس عمومی API که **مرورگر** صدا می‌زند. در زمان build داخل باندل کلاینت قرار می‌گیرد، پس قبل از build باید درست باشد. |
| `PHONIX_FRONTEND_URL` | آدرس عمومی فروشگاه؛ برای CORS و لینک‌های ایمیل (تأیید/بازنشانی) استفاده می‌شود. |
| `FRONTEND_PORT` / `BACKEND_PORT` | پورت‌های منتشرشده روی هاست (پیش‌فرض 3000 و 5228). |
| `PHONIX_BEHIND_PROXY` | اگر یک reverse proxy جلوی برنامه TLS را terminate می‌کند `true` کنید تا IP واقعی کاربر و scheme درست شناسایی شود. |
| `PHONIX_FORCE_HTTPS` | همراه proxy؛ ریدایرکت HTTPS و HSTS را فعال می‌کند. |
| `PHONIX_OWNER_USERNAME` / `PHONIX_OWNER_PASSWORD` | **الزامی برای اولین راه‌اندازی.** `install.sh` این‌ها را **روی همین ترمینال، به‌صورت تعاملی** از شما می‌پرسد (هیچ‌وقت خودکار تولید یا چاپ نمی‌شوند) — فقط اگر بدون این اسکریپت نصب می‌کنید لازم است خودتان این دو خط را در `.env` بنویسید. این تنها اکانتی‌ست که از ابتدا وجود دارد؛ همان مالک بعداً از داخل پنل ادمین بقیه‌ی اکانت‌های ادمین/کارمند را می‌سازد. هر بار سرور بالا می‌آید اعمال می‌شود — یعنی اگر رمز مدیر را گم کردید، کافی‌ست مقدار جدید در `.env` بگذارید و `docker compose up -d` را دوباره بزنید. |
| `PHONIX_BACKUP_KEY` | اختیاری ولی توصیه‌شده. بکاپ‌های خروجی از `/admin/backup` (دانلود یا تلگرام) را با AES-256-GCM رمزنگاری می‌کند. بدون آن بکاپ‌ها ساده (JSON/zip) ذخیره می‌شوند. یک‌بار قبل از اولین بکاپ واقعی تنظیم کنید و جایی خارج از سرور نگه دارید — گم‌کردن این کلید یعنی بکاپ‌های رمزنگاری‌شده با آن برای همیشه غیرقابل‌بازیابی می‌شوند. |

> اگر `NEXT_PUBLIC_API_URL` را عوض کردید، فرانت باید دوباره build شود: `docker compose up -d --build frontend`.

## ماندگاری داده‌ها (Persistence)

کل state داخل volume به نام `phonix-data` (مسیر `/app/App_Data`) ذخیره می‌شود و با rebuild از بین نمی‌رود. این شامل **همهٔ** موارد زیر است: فایل `store.json`، لاگ ممیزی (`audit_store.json`)، **تصاویر و فایل‌های آپلودشده** (`ProtectedUploads/` — عکس پروفایل/محصول/سایت و مدارک احراز هویت)، کلیدهای نشست (`keys/`) و لاگ‌ها.

> ⚠️ بکاپِ فقط `store.json` **کافی نیست** — تصاویر جدا از store.json روی دیسک‌اند. برای بکاپ کامل از گزینهٔ «بکاپ کامل» پنل `/admin/backup` (که store.json + همهٔ مدیا را در یک zip می‌گذارد) یا بات بکاپ تلگرام استفاده کنید.

## TLS / دامنه

برای HTTPS یک reverse proxy (مثلاً Caddy یا nginx) جلوی سرویس بگذارید که دامنه را به `frontend:3000` و مسیر `/api` را به `backend:5228` پراکسی کند، سپس در `.env` دو گزینه‌ی `PHONIX_BEHIND_PROXY` و `PHONIX_FORCE_HTTPS` را `true` کنید و فرانت را با `NEXT_PUBLIC_API_URL=https://<domain>/api` دوباره build کنید.

## مانیتورینگ و هشدار

- **Health check:** بک‌اند مسیر `GET /health` را دارد (وضعیت + بررسی بارگذاری store، خروجی JSON). هم Docker از آن برای healthcheck استفاده می‌کند و هم می‌توانید یک مانیتور بیرونی (مثل UptimeRobot) را روی همین آدرس تنظیم کنید.
- **Docker healthcheck:** هر دو سرویس healthcheck دارند؛ فرانت تا زمانی که بک‌اند `healthy` نشود بالا نمی‌آید و کانتینرِ ناسالم طبق سیاست `restart` راه‌اندازی مجدد می‌شود.
- **هشدار تلگرام:** در پنل `/admin/backup` گزینه‌ی «هشدار خطا و راه‌اندازی سرور» را روشن کنید (از همان توکن بات و چت بکاپ استفاده می‌کند). با فعال‌بودن، هر خطای داخلی سرور (۵۰۰) و هر بار راه‌اندازی مجدد به تلگرام اطلاع داده می‌شود؛ هشدارهای تکراری حداکثر هر ۵ دقیقه یک‌بار ارسال می‌شوند. با دکمه‌ی «ارسال هشدار آزمایشی» اتصال را بسنجید.

## تست خودکار

```bash
cd backend
dotnet test
```

پوشه‌ی `backend/tests/Phonix.Api.Tests` شامل تست‌های واحد (هش گذرواژه، صفحه‌بندی، منطق سفارش/تخفیف/لغو و کنترل موجودی) و تست‌های یکپارچه‌ی HTTP (ورود، حساب مسدود، سلامت سرویس) است. این تست‌ها در CI هم اجرا می‌شوند.

## CI/CD

- **CI** (`.github/workflows/ci.yml`): با هر push/PR روی `main`، بک‌اند (Release) + **تست‌های خودکار** و فرانت (type-check + build) و هر دو ایمیج Docker ساخته/اجرا می‌شوند.
- **CD** (`.github/workflows/deploy.yml`): غیرفعال است تا زمانی که فعالش کنید. در Settings → Secrets and variables → Actions:
  - متغیر (Variable): `DEPLOY_ENABLED=true` و در صورت نیاز `DEPLOY_PATH`
  - Secretها: `DEPLOY_HOST`، `DEPLOY_USER`، `DEPLOY_SSH_KEY`

  بعد از آن هر push روی `main` به‌صورت خودکار روی سرور `git pull` و `docker compose up -d --build` اجرا می‌کند (سرور باید یک‌بار با اسکریپت نصب راه‌اندازی شده باشد).
