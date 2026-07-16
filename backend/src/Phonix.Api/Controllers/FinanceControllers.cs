using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/payment-methods")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("payments")]
public class PaymentMethodsController : ControllerBase
{
    private readonly IDataStore _store;
    public PaymentMethodsController(IDataStore store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<PaymentMethod> Get() => _store.GetPaymentMethods();

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<PaymentMethod> Get(int id) => _store.GetPaymentMethod(id) is { } m ? m : NotFound();

    [HttpPost]
    public ActionResult<PaymentMethod> Create(PaymentMethod input) => _store.AddPaymentMethod(input);

    [HttpPut("{id:int}")]
    public ActionResult<PaymentMethod> Update(int id, PaymentMethod input)
    {
        input.Id = id;
        return _store.UpdatePaymentMethod(input) ? _store.GetPaymentMethod(id)! : NotFound();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeletePaymentMethod(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/payment-settings")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("payments")]
public class PaymentSettingsController : ControllerBase
{
    private readonly IDataStore _store;
    public PaymentSettingsController(IDataStore store) => _store = store;

    [HttpGet]
    public PaymentSettings Get() => _store.GetPaymentSettings();

    [HttpPut]
    public PaymentSettings Update(PaymentSettings input)
    {
        _store.UpdatePaymentSettings(input);
        return _store.GetPaymentSettings();
    }
}

public record TxActionInput(string? Note);
public record WithdrawInput(long Amount, string? Destination);
public record TopUpInput(long Amount, int? CardId, string? Method, string? ReceiptUrl, string? TrackingNumber, string? PaymentDate, string? Description);

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IFileStorageService _files;
    private readonly ITelegramReceiptService _receiptBot;
    private readonly ITelegramOrderService _orderBot;
    private readonly IUserMailer _mailer;
    public TransactionsController(IDataStore store, IFileStorageService files, ITelegramReceiptService receiptBot,
        ITelegramOrderService orderBot, IUserMailer mailer)
    {
        _store = store;
        _files = files;
        _receiptBot = receiptBot;
        _orderBot = orderBot;
        _mailer = mailer;
    }

    // Uploads a bank-transfer receipt to protected storage (outside the web root) and returns its opaque
    // id, which the client then submits as the deposit/checkout receiptUrl. Owner is taken from the session.
    [Authorize]
    [HttpPost("upload-receipt")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<FileUploadResult>> UploadReceipt(IFormFile? file)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var result = await _files.SaveAsync(userId, "receipts", file);
        if (result.Error is not null) return BadRequest(result.Error);
        return new FileUploadResult(result.Id!);
    }

    // Streams a stored receipt. The uploader is encoded in the id; a customer may only read their own
    // receipts, staff may read anyone's — otherwise 403.
    [Authorize]
    [HttpGet("receipt/{id}")]
    public IActionResult Receipt(string id)
    {
        if (_files.OwnerOf(id) is not int ownerId) return BadRequest("شناسه فایل نامعتبر است.");
        if (!this.OwnsOrStaff(ownerId)) return Forbid();
        var stored = _files.Open("receipts", id);
        if (stored is null) return NotFound();
        return File(stored.Content, stored.ContentType);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("transactions")]
    [HttpGet]
    public IEnumerable<Transaction> Get([FromQuery] TxStatus? status) => _store.GetTransactions(status);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("transactions")]
    [HttpGet("page")]
    public PagedResult<Transaction> GetPage([FromQuery] TxStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        PagedResult<Transaction>.From(_store.GetTransactions(status), page, pageSize);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("transactions")]
    [HttpGet("{id:int}")]
    public ActionResult<Transaction> Get(int id) => _store.GetTransaction(id) is { } t ? t : NotFound();

    // Files an offline (card-to-card) wallet top-up request. It is created PENDING and credited to the
    // wallet ONLY when an admin approves it — the user transfers the money out of band, staff confirm
    // it, then approve. Nothing here can credit a balance, so a customer can never top up for free. The
    // payment must come from one of the user's own Approved registered cards, and the type, identity,
    // status and approval are all fixed server-side (never trusted from the client).
    [Authorize]
    [HttpPost]
    public ActionResult<Transaction> Create(TopUpInput input)
    {
        var user = this.CurrentUserId() is int uid ? _store.GetUser(uid) : null;
        if (user is null) return Unauthorized();

        var amount = Math.Abs(input.Amount);
        var min = _store.GetSettings().MinWalletCharge;
        if (amount < min) return BadRequest($"حداقل مبلغ شارژ کیف پول {min:N0} تومان است.");

        // the deposit must be paid from one of the user's own Approved cards (card-to-card only).
        if (input.CardId is not int cardId) return BadRequest("یک کارت بانکی ثبت‌شده را انتخاب کنید.");
        var card = _store.GetCard(cardId);
        if (card is null || card.UserId != user.Id || card.Status != BankCardStatus.Approved)
            return BadRequest("کارت انتخاب‌شده معتبر یا تأییدشده نیست.");

        var tracking = (input.TrackingNumber ?? "").Trim();
        if (tracking.Length == 0) return BadRequest("شماره پیگیری واریز را وارد کنید.");
        var payDate = (input.PaymentDate ?? "").Trim();
        if (payDate.Length == 0) return BadRequest("تاریخ پرداخت را وارد کنید.");
        if (!JalaliDate.IsValidAndNotFuture(payDate))
            return BadRequest("تاریخ پرداخت نامعتبر است یا از امروز جلوتر است.");

        // The receipt is the proof of the out-of-band transfer; when the store requires it, a top-up
        // can't be filed without one so staff always have something to verify against.
        var receipt = string.IsNullOrWhiteSpace(input.ReceiptUrl) ? null : input.ReceiptUrl.Trim();
        if (_store.GetPaymentSettings().RequireReceipt && receipt is null)
            return BadRequest("بارگذاری رسید واریز الزامی است.");

        var tx = new Transaction
        {
            Type = TxTypes.WalletTopUp,
            Amount = amount,
            Method = string.IsNullOrWhiteSpace(input.Method) ? "واریز آفلاین" : input.Method.Trim(),
            Status = TxStatus.Pending,
            UserId = user.Id,
            UserName = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
            ReceiptUrl = receipt,
            SourceCard = card.CardNumber,
            TrackingNumber = tracking,
            PaymentDate = payDate,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
        };
        var saved = _store.AddTransaction(tx);
        // Push the new receipt to the admin Telegram chat for one-tap approve/reject (no-op unless the
        // receipt bot is enabled). Fire-and-forget: filing the deposit never waits on or fails with Telegram.
        _ = _receiptBot.NotifyDepositAsync(saved, CancellationToken.None);
        return saved;
    }

    // Files a withdrawal request. The amount must meet the configured minimum and be covered by the
    // balance; the funds are held immediately (see StoreData.RequestWithdrawal) and paid out only after
    // an admin approves. Identity is taken from the session, never the client.
    [Authorize]
    [HttpPost("withdraw")]
    public ActionResult<Transaction> Withdraw(WithdrawInput input)
    {
        var user = this.CurrentUserId() is int uid ? _store.GetUser(uid) : null;
        if (user is null) return Unauthorized();

        var amount = Math.Abs(input.Amount);
        var min = _store.GetSettings().MinWithdraw;
        if (amount < min) return BadRequest($"حداقل مبلغ برداشت {min:N0} تومان است.");

        var destination = (input.Destination ?? "").Trim();
        if (destination.Length == 0) return BadRequest("شماره کارت یا شبای مقصد را وارد کنید.");

        var result = _store.RequestWithdrawal(user.Id, amount, destination);
        if (result.Error is not null) return BadRequest(result.Error);
        return result.Tx!;
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("transactions")]
    [HttpPost("{id:int}/approve")]
    public ActionResult<Transaction> Approve(int id, TxActionInput? input) => Decide(id, TxStatus.Approved, input?.Note);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("transactions")]
    [HttpPost("{id:int}/reject")]
    public ActionResult<Transaction> Reject(int id, TxActionInput? input) => Decide(id, TxStatus.Rejected, input?.Note);

    // Applies a staff decision and tells the customer. The mail only goes out on a real Pending → decided
    // transition, so re-approving an already-approved transaction (a double-click, a retried request) can't
    // send a second "your wallet was topped up".
    private ActionResult<Transaction> Decide(int id, TxStatus status, string? note)
    {
        var wasPending = _store.GetTransaction(id)?.Status == TxStatus.Pending;
        if (!_store.SetTransactionStatus(id, status, "site", note)) return NotFound();
        var updated = _store.GetTransaction(id)!;
        if (wasPending) _ = _mailer.TransactionDecidedAsync(updated);
        // Approving an order's payment advances that order to «آماده‌سازی», which is when its accounts go to
        // the orders group. The claim inside keeps a re-approval from posting them twice.
        if (wasPending) _ = _orderBot.AnnounceApprovedOrderAsync(updated);
        return updated;
    }
}
