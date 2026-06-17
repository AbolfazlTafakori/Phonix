using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/payment-methods")]
public class PaymentMethodsController : ControllerBase
{
    private readonly StoreData _store;
    public PaymentMethodsController(StoreData store) => _store = store;

    [HttpGet]
    public IEnumerable<PaymentMethod> Get() => _store.GetPaymentMethods();

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
public class PaymentSettingsController : ControllerBase
{
    private readonly StoreData _store;
    public PaymentSettingsController(StoreData store) => _store = store;

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

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly StoreData _store;
    public TransactionsController(StoreData store) => _store = store;

    [HttpGet]
    public IEnumerable<Transaction> Get([FromQuery] TxStatus? status) => _store.GetTransactions(status);

    [HttpGet("{id:int}")]
    public ActionResult<Transaction> Get(int id) => _store.GetTransaction(id) is { } t ? t : NotFound();

    [HttpPost]
    public ActionResult<Transaction> Create(Transaction input) => _store.AddTransaction(input);

    [HttpPost("{id:int}/approve")]
    public ActionResult<Transaction> Approve(int id, TxActionInput? input) =>
        _store.SetTransactionStatus(id, TxStatus.Approved, "site", input?.Note) ? _store.GetTransaction(id)! : NotFound();

    [HttpPost("{id:int}/reject")]
    public ActionResult<Transaction> Reject(int id, TxActionInput? input) =>
        _store.SetTransactionStatus(id, TxStatus.Rejected, "site", input?.Note) ? _store.GetTransaction(id)! : NotFound();
}
