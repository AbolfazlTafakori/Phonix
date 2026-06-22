namespace Phonix.Api.Models;

// Canonical transaction-type labels. Centralised so the wallet-crediting logic and the API can never
// drift from a copy-pasted string literal — a typo in one of those places would otherwise silently
// stop a wallet top-up from ever being credited. The values are the on-the-wire strings the frontend
// and the stored data already use, so this only removes duplication, it does not change the contract.
public static class TxTypes
{
    public const string WalletTopUp = "شارژ کیف پول";
    public const string Purchase = "خرید";
    public const string Referral = "پورسانت";
    public const string Refund = "بازگشت وجه";
    public const string Withdraw = "برداشت";
    // a card-to-card payment for an order's gateway remainder; pending until staff verify the receipt,
    // and approving it advances the linked order to preparing (it does NOT credit the wallet).
    public const string OrderPayment = "پرداخت سفارش";
}
