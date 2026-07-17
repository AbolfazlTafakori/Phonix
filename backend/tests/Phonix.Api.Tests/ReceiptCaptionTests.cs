using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The receipt is sent as a PHOTO with the details as its caption, and Telegram caps a caption at 1024 chars.
// Go one character over and sendPhoto returns 400 and the receipt never reaches the group — silently, because
// the send is fire-and-forget and only logs. These pin the caption inside the limit for the orders that grow
// it the most (many accounts, long names, every optional line present).
public class ReceiptCaptionTests
{
    private const int TelegramCaptionLimit = 1024;

    private static string BuildCaption(StoreData store, Transaction tx)
    {
        var svc = new TelegramReceiptService(store, null!, null!, null!, null!, null!, NullLogger<TelegramReceiptService>.Instance);
        var method = typeof(TelegramReceiptService).GetMethod("BuildCaption", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string)method.Invoke(svc, new object[] { tx })!;
    }

    [Fact]
    public void A_single_account_receipt_caption_fits_telegrams_limit()
    {
        var store = TestStore.Create();
        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;
        var tx = store.AddTransaction(new Transaction
        {
            UserId = 1, Type = TxTypes.OrderPayment, OrderCode = order.Code, Amount = order.Total,
            Status = TxStatus.Pending, SourceCard = "6037991234567893", SourceHolder = "علی محمدی",
            DestinationCard = "6104338638001863", DestinationHolder = "ابوالفضل تفکری", TrackingNumber = "532342342",
        });

        var caption = BuildCaption(store, tx);
        Assert.True(caption.Length <= TelegramCaptionLimit,
            $"caption is {caption.Length} chars, over Telegram's {TelegramCaptionLimit} limit:\n{caption}");
    }

    [Fact]
    public void A_multi_account_receipt_caption_still_fits_telegrams_limit()
    {
        var store = TestStore.Create();
        // Five accounts across two products — every one of these renders its own service block in the caption.
        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 3, (int?)null), (2, 2, (int?)null) },
            "کارت به کارت", fromWallet: false).Order!;
        var tx = store.AddTransaction(new Transaction
        {
            UserId = 1, Type = TxTypes.OrderPayment, OrderCode = order.Code, Amount = order.Total,
            Status = TxStatus.Pending, SourceCard = "6037991234567893", SourceHolder = "علی محمدی",
            DestinationCard = "6104338638001863", DestinationHolder = "ابوالفضل تفکری", TrackingNumber = "532342342",
        });

        var caption = BuildCaption(store, tx);
        Assert.True(caption.Length <= TelegramCaptionLimit,
            $"caption is {caption.Length} chars, over Telegram's {TelegramCaptionLimit} limit:\n{caption}");
    }
}
