using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bug: tras el rediseño de Plantillas, un draft local (Borrador, sin MetaTemplateId) quedó sin
/// ningún camino visible para "Enviar a aprobación" -- ni en READ ni en EDIT. Estos tests cubren la
/// regla de visibilidad extraída a <see cref="ConversacionesPlantillas.CanSubmitDraftForApproval"/>
/// y <see cref="ConversacionesPlantillas.CanSyncTemplateStatus"/>, que ahora gobiernan los botones
/// "Enviar a aprobación" y "Sincronizar estado" tanto en la lista (READ) como en el formulario (EDIT).
///
/// No cubren acá (requeriría bUnit, que este repo no tiene): la transición de estado post-submit
/// exitoso (desaparece el botón porque SelectTemplateAsync recarga el DTO desde el server con el
/// nuevo EstadoMeta) y que un error de Meta conserve el draft sin falso éxito (SubmitAsync ya
/// envuelve el submit en try/catch y llama SetError, reutilizando el mismo flujo que ya tenía Create).
/// </summary>
public sealed class ConversacionesPlantillasSubmitActionTests
{
    private static ConversacionPlantillaDto BuildDto(
        bool activa = true,
        bool esMetaRemota = false,
        string metaTemplateId = "",
        string estadoMeta = "")
        => new()
        {
            IdPlantilla = 1,
            NombreVisible = "contacto-alfaclaro",
            NombreMeta = "contacto_alfaclaro",
            CuerpoTexto = "Hola! Esta es una plantilla de prueba, {{1}}",
            Activa = activa,
            EsMetaRemota = esMetaRemota,
            MetaTemplateId = metaTemplateId,
            EstadoMeta = estadoMeta
        };

    [Fact]
    public void LocalDraft_Active_WithoutMetaId_CanBeSubmitted()
    {
        var dto = BuildDto();

        Assert.True(ConversacionesPlantillas.CanSubmitDraftForApproval(dto));
    }

    [Theory]
    [InlineData("APPROVED")]
    [InlineData("PENDING")]
    [InlineData("REJECTED")]
    [InlineData("PAUSED")]
    [InlineData("DISABLED")]
    public void AlreadySubmittedStates_CannotBeSubmittedAgain(string estadoMeta)
    {
        var dto = BuildDto(metaTemplateId: "123456", estadoMeta: estadoMeta);

        Assert.False(ConversacionesPlantillas.CanSubmitDraftForApproval(dto));
    }

    [Fact]
    public void HavingAMetaTemplateId_CountsAsAlreadySubmitted_EvenWithoutEstadoMeta()
    {
        // Caso borde: se guardó el MetaTemplateId pero por lo que sea el estado local quedó vacío.
        var dto = BuildDto(metaTemplateId: "123456");

        Assert.False(ConversacionesPlantillas.CanSubmitDraftForApproval(dto));
    }

    [Fact]
    public void InactiveDraft_CannotBeSubmitted()
    {
        var dto = BuildDto(activa: false);

        Assert.False(ConversacionesPlantillas.CanSubmitDraftForApproval(dto));
    }

    [Fact]
    public void TemplateImportedFromMeta_IsNeverOfferedForResubmit()
    {
        // Plantilla EsMetaRemota (sincronizada desde el catálogo de Meta), sin MetaTemplateId local
        // ni EstadoMeta seteado en este DTO puntual -- igual no corresponde reenviarla.
        var dto = BuildDto(esMetaRemota: true);

        Assert.False(ConversacionesPlantillas.CanSubmitDraftForApproval(dto));
    }

    [Fact]
    public void NullSelection_CannotBeSubmitted_NeverThrows()
    {
        Assert.False(ConversacionesPlantillas.CanSubmitDraftForApproval(null));
    }

    [Fact]
    public void LocalDraft_WithoutMetaId_SyncStatusIsNotAvailable()
    {
        var dto = BuildDto();

        Assert.False(ConversacionesPlantillas.CanSyncTemplateStatus(dto));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankMetaId_SyncStatusIsNotAvailable(string metaTemplateId)
    {
        var dto = BuildDto(metaTemplateId: metaTemplateId);

        Assert.False(ConversacionesPlantillas.CanSyncTemplateStatus(dto));
    }

    [Fact]
    public void HavingAMetaTemplateId_MakesSyncStatusAvailable()
    {
        var dto = BuildDto(metaTemplateId: "123456", estadoMeta: "PENDING");

        Assert.True(ConversacionesPlantillas.CanSyncTemplateStatus(dto));
    }

    [Fact]
    public void NullSelection_SyncStatusIsNotAvailable_NeverThrows()
    {
        Assert.False(ConversacionesPlantillas.CanSyncTemplateStatus(null));
    }
}
