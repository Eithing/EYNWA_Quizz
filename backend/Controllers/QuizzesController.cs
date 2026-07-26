using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizParty.Api.Data;
using QuizParty.Api.Dtos;
using QuizParty.Api.Extensions;
using QuizParty.Api.Features;
using QuizParty.Api.Models;

namespace QuizParty.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class QuizzesController(QuizPartyDbContext db, FeatureRegistry featureRegistry) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<QuizSummaryDto>>> GetMine()
    {
        var ownerId = User.GetGameMasterId();

        var quizzes = await db.Quizzes
            .AsNoTracking()
            .Where(q => q.OwnerId == ownerId)
            .OrderByDescending(q => q.UpdatedAt)
            .Select(q => new QuizSummaryDto(q.Id, q.Title, q.Description, q.UpdatedAt, q.Rounds.Count))
            .ToListAsync();

        return Ok(quizzes);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<QuizDetailDto>> GetById(int id)
    {
        var quiz = await LoadOwnedQuiz(id);
        if (quiz is null)
        {
            return NotFound();
        }

        return Ok(ToDetailDto(quiz));
    }

    [HttpPost]
    public async Task<ActionResult<QuizDetailDto>> Create(SaveQuizRequest request)
    {
        var ownerId = User.GetGameMasterId();

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var now = DateTime.UtcNow;
        var quiz = new Quiz
        {
            OwnerId = ownerId,
            Title = request.Title.Trim(),
            Description = request.Description,
            CreatedAt = now,
            UpdatedAt = now,
            Rounds = request.Rounds.Select(ToRoundEntity).ToList()
        };

        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = quiz.Id }, ToDetailDto(quiz));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<QuizDetailDto>> Update(int id, SaveQuizRequest request)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var quiz = await LoadOwnedQuiz(id);
        if (quiz is null)
        {
            return NotFound();
        }

        quiz.Title = request.Title.Trim();
        quiz.Description = request.Description;
        quiz.UpdatedAt = DateTime.UtcNow;

        db.Rounds.RemoveRange(quiz.Rounds);
        quiz.Rounds = request.Rounds.Select(ToRoundEntity).ToList();

        await db.SaveChangesAsync();

        return Ok(ToDetailDto(quiz));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ownerId = User.GetGameMasterId();

        var quiz = await db.Quizzes.SingleOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);
        if (quiz is null)
        {
            return NotFound();
        }

        db.Quizzes.Remove(quiz);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:int}/duplicate")]
    public async Task<ActionResult<QuizDetailDto>> Duplicate(int id)
    {
        var source = await LoadOwnedQuiz(id);
        if (source is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var copy = new Quiz
        {
            OwnerId = source.OwnerId,
            Title = $"{source.Title} (copie)",
            Description = source.Description,
            CreatedAt = now,
            UpdatedAt = now,
            Rounds = source.Rounds
                .OrderBy(r => r.Order)
                .Select(r => new Round
                {
                    Order = r.Order,
                    FeatureTypeKey = r.FeatureTypeKey,
                    Title = r.Title,
                    ConfigJson = r.ConfigJson,
                    Questions = r.Questions
                        .OrderBy(q => q.Order)
                        .Select(q => new Question { Order = q.Order, PayloadJson = q.PayloadJson })
                        .ToList()
                })
                .ToList()
        };

        db.Quizzes.Add(copy);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = copy.Id }, ToDetailDto(copy));
    }

    private async Task<Quiz?> LoadOwnedQuiz(int id)
    {
        var ownerId = User.GetGameMasterId();

        return await db.Quizzes
            .Include(q => q.Rounds).ThenInclude(r => r.Questions)
            .SingleOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);
    }

    private string? ValidateRequest(SaveQuizRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Le titre du quiz est requis.";
        }

        var unknownFeature = request.Rounds.FirstOrDefault(r => !featureRegistry.Exists(r.FeatureTypeKey));
        if (unknownFeature is not null)
        {
            return $"Type de manche inconnu : {unknownFeature.FeatureTypeKey}.";
        }

        return null;
    }

    private static Round ToRoundEntity(RoundDto dto) => new()
    {
        Order = dto.Order,
        FeatureTypeKey = dto.FeatureTypeKey,
        Title = dto.Title,
        ConfigJson = dto.ConfigJson,
        Questions = dto.Questions.Select(q => new Question { Order = q.Order, PayloadJson = q.PayloadJson }).ToList()
    };

    private static QuizDetailDto ToDetailDto(Quiz quiz) => new(
        quiz.Id,
        quiz.Title,
        quiz.Description,
        quiz.CreatedAt,
        quiz.UpdatedAt,
        quiz.Rounds
            .OrderBy(r => r.Order)
            .Select(r => new RoundDto(
                r.Id,
                r.Order,
                r.FeatureTypeKey,
                r.Title,
                r.ConfigJson,
                r.Questions.OrderBy(q => q.Order).Select(q => new QuestionDto(q.Id, q.Order, q.PayloadJson)).ToList()))
            .ToList());
}
