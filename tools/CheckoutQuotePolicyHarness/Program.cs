using MarkUptv.Models;
using MarkUptv.Services;

var samples = new (DeviceStatusResponse? Value, bool Expected, string Case)[]
{
    (null, false, "Missing server response"),
    (new DeviceStatusResponse(), false, "Missing price"),
    (new DeviceStatusResponse { Price = 5000, Currency = "UGX" }, true, "Valid UGX quote"),
    (new DeviceStatusResponse { Price = 0, Currency = "UGX" }, false, "Zero price"),
    (new DeviceStatusResponse { Price = -1, Currency = "UGX" }, false, "Negative price"),
    (new DeviceStatusResponse { Price = 5000 }, false, "Missing currency"),
    (new DeviceStatusResponse { Price = 5000, Currency = "?" }, false, "Invalid currency"),
    (new DeviceStatusResponse { Price = 5000, Currency = "USD", CanWatch = true }, false, "Already entitled")
};
var failed = 0;
foreach (var (value, expected, title) in samples)
{
    var actual = CheckoutQuotePolicy.IsReady(value);
    if (actual != expected) { failed++; Console.Error.WriteLine($"FAIL {title}: {actual} != {expected}"); }
}
Console.WriteLine($"Checkout quote gate: {samples.Length-failed}/{samples.Length} passed");
return failed == 0 ? 0 : 1;
