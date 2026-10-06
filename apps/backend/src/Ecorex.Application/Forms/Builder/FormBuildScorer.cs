using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Builder;

/// <summary>Lo que se ESPERA encontrar en un formulario construido por el agente (fixture de un eval):
/// subcadenas que deben aparecer en algun label (p.ej. los numeros de casilla "29", "42" o los textos de
/// concepto "Honorarios"). Se mide cobertura = encontradas / esperadas.</summary>
public sealed record FormBuildExpectation(IReadOnlyList<string> ExpectedLabelFragments);

/// <summary>Resultado del scorer: un puntaje 0-100 y los contadores que lo explican. Cada contador es un defecto
/// que YA vimos producir al agente en corridas reales (F350), asi el numero es comparable corrida a corrida y
/// entre modelos/arneses, en vez de "me parecio bien" sobre n=1.</summary>
public sealed record FormBuildScore(
    int Score,
    int TotalFields,
    int TotalContainers,
    int DuplicateFieldCodes,
    int DuplicateTextLabelsInContainer,
    int MarkdownLabels,
    int OrphanQuestions,
    int EmptyContainers,
    int FullWidthTextInSharedRow,
    int ExpectedFound,
    int ExpectedTotal,
    IReadOnlyList<string> Notes);

/// <summary>Puntua DETERMINISTICAMENTE una definicion de formulario (pura, sin I/O). Base del eval del constructor:
/// correr el agente N veces sobre un fixture y comparar puntajes, no corridas sueltas.</summary>
public static class FormBuildScorer
{
    private static readonly FormControlType[] TextTypes = { FormControlType.Heading, FormControlType.Paragraph };

    public static FormBuildScore Score(FormDefinitionDetailDto d, FormBuildExpectation? expectation = null)
    {
        var notes = new List<string>();
        var containerIds = d.Containers.Select(c => c.Id).ToHashSet();
        var byContainer = d.Containers.ToDictionary(c => c.Id);

        // 1) field_code repetido: el self-heal / reintento duplico campos.
        var duplicateCodes = d.Questions
            .GroupBy(q => q.FieldCode, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

        // 2) Encabezado de matriz duplicado: mismo texto 3+ veces en UN contenedor (el legitimo repite a lo sumo 2).
        var duplicateText = d.Questions
            .Where(q => q.ContainerId is not null && TextTypes.Contains(q.ControlType) && !string.IsNullOrWhiteSpace(q.Label))
            .GroupBy(q => q.ContainerId!.Value)
            .Sum(g => g.GroupBy(q => q.Label.Trim(), StringComparer.OrdinalIgnoreCase).Count(x => x.Count() >= 3));

        // 3) Markdown literal en labels ("**Concepto**", "# Titulo"): el renderer lo pinta tal cual.
        var markdown = d.Questions.Count(q => q.Label.Contains("**", StringComparison.Ordinal) || q.Label.TrimStart().StartsWith('#'));

        // 4) Pregunta huerfana: container_id que no existe (el bug PUT-vs-PATCH de update_question).
        var orphans = d.Questions.Count(q => q.ContainerId is Guid cid && !containerIds.Contains(cid));

        // 5) Contenedor vacio: sin preguntas y sin hijos (una Row/Section que quedo colgando).
        var usedAsContainer = d.Questions.Where(q => q.ContainerId is not null).Select(q => q.ContainerId!.Value).ToHashSet();
        var usedAsParent = d.Containers.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();
        var empty = d.Containers.Count(c => !usedAsContainer.Contains(c.Id) && !usedAsParent.Contains(c.Id));

        // 6) Desalineacion: un texto a width 12 dentro de una Row con mas hermanos (empuja a los demas a otra linea;
        //    el "Concepto" a 12 del F350).
        var fullWidthText = d.Questions.Count(q =>
            q.ContainerId is Guid cid && TextTypes.Contains(q.ControlType) && q.Width >= 12
            && byContainer.TryGetValue(cid, out var c) && c.ContainerType == FormContainerType.Row
            && d.Questions.Count(o => o.ContainerId == cid) > 1);

        // 7) Cobertura contra lo esperado (fixture).
        var expectedFound = 0; var expectedTotal = 0;
        if (expectation is not null && expectation.ExpectedLabelFragments.Count > 0)
        {
            expectedTotal = expectation.ExpectedLabelFragments.Count;
            var labels = d.Questions.Select(q => q.Label).ToList();
            expectedFound = expectation.ExpectedLabelFragments.Count(e => labels.Any(l => l.Contains(e, StringComparison.OrdinalIgnoreCase)));
            var missing = expectation.ExpectedLabelFragments.Where(e => !labels.Any(l => l.Contains(e, StringComparison.OrdinalIgnoreCase))).Take(10).ToList();
            if (missing.Count > 0) { notes.Add("faltan: " + string.Join(", ", missing)); }
        }

        var score = 100;
        score -= Math.Min(30, duplicateCodes * 10);
        score -= Math.Min(30, duplicateText * 15);
        score -= Math.Min(10, markdown * 2);
        score -= Math.Min(20, orphans * 5);
        score -= Math.Min(10, empty * 2);
        score -= Math.Min(15, fullWidthText * 5);
        if (expectedTotal > 0)
        {
            var coverage = (double)expectedFound / expectedTotal;
            score -= (int)Math.Round((1 - coverage) * 40);
            notes.Insert(0, $"cobertura {expectedFound}/{expectedTotal}");
        }
        score = Math.Clamp(score, 0, 100);

        return new FormBuildScore(score, d.Questions.Count, d.Containers.Count, duplicateCodes, duplicateText, markdown,
            orphans, empty, fullWidthText, expectedFound, expectedTotal, notes);
    }
}
