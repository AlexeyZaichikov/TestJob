using Microsoft.AspNetCore.Mvc;
using TestJob.Api.Models;
using TestJob.Api.Services;
using TestJob.Api.Validation;

namespace TestJob.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class TestJobController : ControllerBase
{
    private readonly TestJobService _service;

    public TestJobController(TestJobService service) => _service = service;

    [HttpPost("elements")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(TestJobResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Process([FromBody] TestJobRequest request, CancellationToken cancellationToken)
    {
        var validationResult = new TestJobRequestValidator().Validate(request);
        if (!validationResult.IsValid)
        {
            return Ok(TestJobResponse.Error(
                "VALIDATION_ERROR",
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));
        }

        var response = await _service.ProcessAsync(request, cancellationToken);
        return Ok(response);
    }
}
