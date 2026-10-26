using System.Text.Json;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Features.Ingestion;
using CarWash.Hub.UnitTests.TestSupport;

namespace CarWash.Hub.UnitTests.Ingestion;

public class IngestMessageRequestValidatorTests
{
    private static readonly IngestMessageRequestValidator Validator = new(new EchoLocalizer());

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> ErrorsFor(string? machineId, JsonElement? data, string property) =>
        [.. Validator.Validate(new IngestMessageRequest(machineId, data)).Errors
            .Where(error => error.PropertyName == property)
            .Select(error => error.ErrorMessage)];

    [Theory]
    [InlineData("""{"status":"Idle","coins":12}""")]
    [InlineData("""[{"t":1},{"t":2}]""")]
    [InlineData("\"{\\\"status\\\":\\\"Idle\\\"}\"")]
    [InlineData("\"plain text status\"")]
    [InlineData("42")]
    [InlineData("true")]
    public void Any_json_data_with_a_valid_machine_id_passes(string data) =>
        Validator.Validate(new IngestMessageRequest("CW-RUH-0001", Json(data))).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_machine_id_is_required(string? machineId) =>
        ErrorsFor(machineId, Json("{}"), "machineId").ShouldBe([$"{ResourceKeys.Validation.Required}|{ResourceKeys.Fields.MachineId}"]);

    [Theory]
    [InlineData("CW/01")]
    [InlineData("CW 01")]
    [InlineData("-CW-01")]
    [InlineData("مكينة-1")]
    public void A_malformed_machine_id_is_refused(string machineId) =>
        ErrorsFor(machineId, Json("{}"), "machineId").Single().ShouldStartWith(ResourceKeys.Ingestion.MachineIdInvalid);

    [Fact]
    public void A_machine_id_of_the_maximum_length_passes_and_one_more_fails()
    {
        ErrorsFor(new string('A', MachineIdRules.MaxLength), Json("{}"), "machineId").ShouldBeEmpty();
        ErrorsFor(new string('A', MachineIdRules.MaxLength + 1), Json("{}"), "machineId").ShouldNotBeEmpty();
    }

    [Fact]
    public void Surrounding_spaces_around_a_machine_id_are_ignored() =>
        ErrorsFor("  CW-1  ", Json("{}"), "machineId").ShouldBeEmpty();

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Null_or_blank_data_is_required(string data) =>
        ErrorsFor("CW-1", Json(data), "data").ShouldBe([$"{ResourceKeys.Validation.Required}|{ResourceKeys.Fields.Data}"]);

    [Fact]
    public void Missing_data_is_required() =>
        ErrorsFor("CW-1", null, "data").ShouldBe([$"{ResourceKeys.Validation.Required}|{ResourceKeys.Fields.Data}"]);

    [Fact]
    public void Data_over_64_KB_is_refused_and_exactly_64_KB_is_accepted()
    {
        // A JSON string's stored text is its content, so the limit applies to the content itself.
        ErrorsFor("CW-1", Json($"\"{new string('x', StoredPayload.MaxBytes)}\""), "data").ShouldBeEmpty();
        ErrorsFor("CW-1", Json($"\"{new string('x', StoredPayload.MaxBytes + 1)}\""), "data")
            .ShouldBe([$"{ResourceKeys.Ingestion.DataTooLarge}|{StoredPayload.MaxBytes}"]);
    }
}
