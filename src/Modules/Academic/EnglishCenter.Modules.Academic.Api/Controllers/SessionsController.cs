using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Application.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController, Route("api/academic/sessions"), Authorize(Roles = AcademicRoles.Readers), AcademicErrors]
public sealed class SessionsController(SessionService sessions, ClassAccess access) : ControllerBase
{
    [HttpPost, Authorize(Roles = AcademicRoles.Managers)]
    public async Task<IActionResult> Create(CreateSessionBody body, CancellationToken ct)
    {
        var created = await sessions.CreateAsync(new(body.ClassId, body.StartsAtUtc, body.EndsAtUtc,
            body.Unit, body.Lesson, body.Skills, body.SessionType, body.ExamSlot), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public Task<SessionResponse> Get(Guid id, CancellationToken ct) => sessions.GetAsync(id, ct);

    [HttpGet]
    public async Task<IReadOnlyList<SessionResponse>> Range([FromQuery] string? fromUtc, [FromQuery] string? toUtc,
        [FromQuery] Guid? classId, CancellationToken ct)
    {
        await access.RequireUserAsync(ct);
        var errors = new Dictionary<string, string[]>();
        if (!ExplicitOffsetDateTimeConverter.TryParse(fromUtc, out var from)) errors[nameof(fromUtc)] = ["An ISO timestamp with an explicit offset is required."];
        if (!ExplicitOffsetDateTimeConverter.TryParse(toUtc, out var to)) errors[nameof(toUtc)] = ["An ISO timestamp with an explicit offset is required."];
        if (errors.Count > 0) throw new AcademicFailure(400, "One or more validation errors occurred.", errors);
        return await sessions.RangeAsync(new(from, to, classId), ct);
    }
}

// The boundary converter rejects offsetless JSON before machine-local time can be inferred.
public sealed record CreateSessionBody(
    [Required] Guid? ClassId,
    [Required] [property: JsonConverter(typeof(ExplicitOffsetDateTimeConverter))] DateTimeOffset? StartsAtUtc,
    [Required] [property: JsonConverter(typeof(ExplicitOffsetDateTimeConverter))] DateTimeOffset? EndsAtUtc,
    [Required] string? Unit, [Required] string? Lesson, [Required] string[]? Skills,
    [Required] string? SessionType, string? ExamSlot = null);

public sealed partial class ExplicitOffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public static bool TryParse(string? value, out DateTimeOffset result)
    {
        result = default;
        return value is not null && IsoTimestamp().IsMatch(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result) && result.UtcDateTime != default;
    }

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !TryParse(reader.GetString(), out var result))
            throw new JsonException("An ISO timestamp with an explicit offset is required.");
        return result.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime());

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex IsoTimestamp();
}
