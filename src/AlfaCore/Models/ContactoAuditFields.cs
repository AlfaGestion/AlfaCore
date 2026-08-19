namespace AlfaCore.Models;

/// <summary>Claves estables y etiquetas amigables de los campos auditables de Contactos.</summary>
public static class ContactoAuditFields
{
    public const string NombreApellido = nameof(NombreApellido);
    public const string Domicilio = nameof(Domicilio);
    public const string Localidad = nameof(Localidad);
    public const string Provincia = nameof(Provincia);
    public const string CodigoPostal = nameof(CodigoPostal);
    public const string NumeroDocumento = nameof(NumeroDocumento);
    public const string Telefono = nameof(Telefono);
    public const string TelefonoAlternativo = nameof(TelefonoAlternativo);
    public const string Celular = nameof(Celular);
    public const string Email = nameof(Email);
    public const string Website = nameof(Website);
    public const string Cargo = nameof(Cargo);
    public const string Observaciones = nameof(Observaciones);
    public const string MailPagosCobranzas = nameof(MailPagosCobranzas);
    public const string MailOrdenCompra = nameof(MailOrdenCompra);
    public const string MailOrdenTrabajo = nameof(MailOrdenTrabajo);
    public const string Activo = nameof(Activo);

    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [NombreApellido] = "Nombre y apellido",
        [Domicilio] = "Domicilio",
        [Localidad] = "Localidad",
        [Provincia] = "Provincia",
        [CodigoPostal] = "Código postal",
        [NumeroDocumento] = "Número de documento",
        [Telefono] = "Teléfono",
        [TelefonoAlternativo] = "Teléfono alternativo",
        [Celular] = "Celular",
        [Email] = "Correo electrónico",
        [Website] = "Página web",
        [Cargo] = "Cargo",
        [Observaciones] = "Observaciones",
        [MailPagosCobranzas] = "Pagos/Cobranzas",
        [MailOrdenCompra] = "Órdenes de compra",
        [MailOrdenTrabajo] = "Órdenes de trabajo",
        [Activo] = "Activo"
    };
}
