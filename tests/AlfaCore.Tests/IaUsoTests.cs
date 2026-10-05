using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Medición del consumo de OpenAI por base (2026-10-05): lectura de "usage" en los dos formatos de la
/// API, registro con la base activa (incluida la del webhook) y armado del INSERT por lotes.
/// </summary>
public sealed class IaUsoTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Tokens_FormatoChatCompletions()
    {
        var tokens = IaUsoTokens.Desde(Json("""
            {"model":"gpt-4o-mini-2024-07-18","usage":{"prompt_tokens":1200,"completion_tokens":80,
             "prompt_tokens_details":{"cached_tokens":1024}}}
            """));

        Assert.Equal(new IaUsoTokens(1200, 1024, 80), tokens);
    }

    [Fact]
    public void Tokens_FormatoResponses()
    {
        var tokens = IaUsoTokens.Desde(Json("""
            {"usage":{"input_tokens":500,"output_tokens":40,"input_tokens_details":{"cached_tokens":0}}}
            """));

        Assert.Equal(new IaUsoTokens(500, 0, 40), tokens);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"usage":null}""")]
    [InlineData("""{"choices":[]}""")]
    public void Tokens_SinUsageDevuelveVacio(string json)
        => Assert.True(IaUsoTokens.Desde(Json(json)).Vacio);

    [Fact]
    public void Tokens_CacheadosNuncaSuperanLaEntrada()
        => Assert.Equal(10, IaUsoTokens.Desde(Json("""{"usage":{"prompt_tokens":10,"completion_tokens":1,"prompt_tokens_details":{"cached_tokens":50}}}""")).EntradaCacheada);

    [Fact]
    public void Modelo_PrefiereElDeLaRespuesta()
    {
        Assert.Equal("gpt-4o-mini-2024-07-18", IaUsoTokens.Modelo(Json("""{"model":"gpt-4o-mini-2024-07-18"}"""), "gpt-4o-mini"));
        Assert.Equal("gpt-4o-mini", IaUsoTokens.Modelo(Json("{}"), " gpt-4o-mini "));
    }

    [Fact]
    public async Task Recorder_RegistraConLaBaseActiva()
    {
        var cola = new IaUsoCola();
        var recorder = new IaUsoRecorder(new SesionFija(new SessionDto { BaseId = 4264, BaseDatos = "AW_112012800" }), cola);

        recorder.Registrar(IaUsoFunciones.Bot, Json("""{"model":"gpt-4o-mini","usage":{"prompt_tokens":100,"completion_tokens":20}}"""), "gpt-4o-mini", "conv:15");
        recorder.Registrar(IaUsoFunciones.Bot, Json("{}"), "gpt-4o-mini");

        var registro = await Leer(cola);
        Assert.Equal(4264, registro.IdBase);
        Assert.Equal("AW_112012800", registro.BaseDatos);
        Assert.Equal(IaUsoFunciones.Bot, registro.Funcion);
        Assert.Equal(100, registro.TokensEntrada);
        Assert.Equal(20, registro.TokensSalida);
        Assert.Equal("conv:15", registro.Referencia);
        Assert.False(cola.Lector.TryRead(out _)); // la respuesta sin usage no se registra
    }

    [Fact]
    public async Task Recorder_SinSesionRegistraSinBaseYNoFalla()
    {
        var cola = new IaUsoCola();
        var recorder = new IaUsoRecorder(new SesionFija(null), cola);

        recorder.RegistrarArchivos(IaUsoFunciones.ArchivosBusqueda, busquedas: 2, bytesAlmacenados: 0);

        var registro = await Leer(cola);
        Assert.Null(registro.IdBase);
        Assert.Equal("file_search", registro.Modelo);
        Assert.Equal(2, registro.Busquedas);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(IaUsoFlushService.MaxFilasPorInsert)]
    public void InsertSql_TieneUnaFilaDeValoresPorRegistroYNoSuperaElLimiteDeParametros(int filas)
    {
        var sql = IaUsoFlushService.BuildInsertSql(filas);

        Assert.Equal(filas, Regex.Matches(sql, @"\(@F\d+,").Count);
        var parametros = Regex.Matches(sql, @"@[A-Za-z]+\d+").Select(m => m.Value).Distinct().Count() + 1; // + @Servidor
        Assert.True(parametros < 2100, $"{parametros} parámetros");
        Assert.Contains("ORDER BY LEN(pm.Modelo) DESC", sql);
        Assert.Contains("LEFT JOIN dbo.bases b", sql);
    }

    private static async Task<IaUsoRegistro> Leer(IaUsoCola cola)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await cola.Lector.ReadAsync(cts.Token);
    }

    private sealed class SesionFija(SessionDto? sesion) : ISessionService
    {
        public event Action? SessionChanged { add { } remove { } }
        public string GetConnectionString() => string.Empty;
        public SessionDto? GetActiveSession() => sesion;
        public SessionDto? GetWebhookOverride(int expectedBaseId) => null;
        public void SetWebhookOverride(SessionDto session) { }
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => throw new NotSupportedException();
    }
}
