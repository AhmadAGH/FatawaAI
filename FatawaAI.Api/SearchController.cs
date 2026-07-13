using FatawaAI.Core;
using MediatR;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FatawaAI.Api;

[ApiController]
[Route("api/[controller]")]
[EnableCors]
public sealed class SearchController : ControllerBase
{
    private readonly IMediator _mediator;

    public SearchController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [EnableRateLimiting("search")]
    public async Task<ActionResult<SearchFatwasResponse>> SearchAsync(
        [FromBody] SearchDto request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest("Query is required.");
        }

        if (request.Query.Length > 500)
        {
            return BadRequest("Query must be at most 500 characters.");
        }

        var result = await _mediator.Send(
            new SearchFatwasQuery(
                request.Query,
                request.Category,
                request.CollectionType),
            cancellationToken);

        return Ok(result);
    }
}


