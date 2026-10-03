using System;
using System.Collections.Generic;
using System.Text;

namespace MarkUptv.Models
{
    public partial class PaymentInitiateResponse
    {
        public string RedirectUrl { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
    }
}
