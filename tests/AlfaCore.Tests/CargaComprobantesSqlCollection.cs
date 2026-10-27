using Xunit;

namespace AlfaCore.Tests;

// Evita que los diagnósticos de lectura compitan con el ensayo Serializable y su rollback.
[CollectionDefinition("CargaComprobantes SQL", DisableParallelization = true)]
public sealed class CargaComprobantesSqlCollection;
