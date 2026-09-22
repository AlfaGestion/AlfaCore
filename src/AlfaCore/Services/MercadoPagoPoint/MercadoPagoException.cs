using System.Net;

namespace AlfaCore.Services.MercadoPagoPoint;

public class MercadoPagoException : Exception
{
    public MercadoPagoException(string message)
        : base(message)
    {
    }

    public MercadoPagoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public HttpStatusCode? StatusCode { get; set; }
}
