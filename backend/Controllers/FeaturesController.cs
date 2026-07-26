using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizParty.Api.Dtos;
using QuizParty.Api.Features;

namespace QuizParty.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class FeaturesController(FeatureRegistry registry) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<FeatureMetaDto>> GetAll()
    {
        var features = registry.All
            .Select(f => new FeatureMetaDto(f.TypeKey, f.DisplayName, f.Description))
            .ToList();

        return Ok(features);
    }
}
