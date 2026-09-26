using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TestJob.Api.Models;
using TestJob.Api.Services;
using TestJob.Api.Validation;

namespace TestJob.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class TestJobController : ControllerBase
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    private readonly TestJobService _service;

    public TestJobController(TestJobService service) => _service = service;

    [HttpPost("elements")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> Process(CancellationToken cancellationToken)
    {
        TestJobRequest request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<TestJobRequest>(Request.Body, RequestJsonOptions, cancellationToken)
                ?? new TestJobRequest();
        }
        catch (JsonException)
        {
            return Ok(TestJobResponse.Error("INVALID_JSON", "Request body is not a valid JSON object."));
        }

        var validationResult = new TestJobRequestValidator().Validate(request);
        if (!validationResult.IsValid)
        {
            return Ok(TestJobResponse.Error("VALIDATION_ERROR", string.Empty));
        }

        var response = await _service.ProcessAsync(request, cancellationToken);
        return Ok(response);
    }
}