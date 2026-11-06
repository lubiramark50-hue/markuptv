using MarkUptv.Services;

Uri backend = new("https://api.markuptv.example/");
var samples = new (string Url, PaymentNavigationOutcome Expected)[]
{
    ("markuptv://payment-success", PaymentNavigationOutcome.Success),
    ("markuptv://payment-failed", PaymentNavigationOutcome.Failure),
    ("https://api.markuptv.example/payment-success", PaymentNavigationOutcome.Success),
    ("https://api.markuptv.example/payment-failed", PaymentNavigationOutcome.Failure),
    ("https://api.markuptv.example/api/device/payment-callback?status=ok", PaymentNavigationOutcome.Success),
    ("https://api.markuptv.example/api/donation/callback", PaymentNavigationOutcome.Success),
    ("https://api.markuptv.example/complete", PaymentNavigationOutcome.None),
    ("https://evil.example/payment-success", PaymentNavigationOutcome.None),
    ("https://api.markuptv.example.evil.example/payment-success", PaymentNavigationOutcome.None),
    ("https://api.markuptv.example/redirect?next=/payment-success", PaymentNavigationOutcome.None),
    ("https://api.markuptv.example/path/payment-success", PaymentNavigationOutcome.None),
    ("http://api.markuptv.example/payment-success", PaymentNavigationOutcome.None),
    ("javascript:alert('payment-success')", PaymentNavigationOutcome.None),
    ("markuptv://payment-success.evil.example", PaymentNavigationOutcome.None),
    ("markuptv://payment-success/other", PaymentNavigationOutcome.None),
    ("https://evil.example@api.markuptv.example/payment-success", PaymentNavigationOutcome.None),
    ("", PaymentNavigationOutcome.None)
};
int failed=0;
foreach (var (url, expected) in samples)
{
    var result=PaymentNavigationGuard.Classify(url, backend);
    if (result != expected)
    {
        Console.Error.WriteLine($"FAIL {url}: expected {expected}, actual {result}");
        failed++;
    }
}
Console.WriteLine($"Payment callback guard: {samples.Length-failed}/{samples.Length} passed.");
return failed == 0 ? 0 : 1;
