using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Extensions;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class QuizzesController(QuizDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<QuizSummaryDto>>> GetMine()
    {
        var ownerId = User.GetGameMasterId();

        var quizzes = await db.Quizzes
            .AsNoTracking()
            .Where(q => q.OwnerId == ownerId)
            .OrderByDescending(q => q.UpdatedAtUtc)
            .Select(q => new QuizSummaryDto(q.Id, q.Title, q.Description, q.InviteCode, q.UpdatedAtUtc, q.Steps.Count))
            .ToListAsync();

        return Ok(quizzes);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<QuizDetailDto>> GetById(int id)
    {
        var ownerId = User.GetGameMasterId();

        var quiz = await db.Quizzes
            .AsNoTracking()
            .Include(q => q.Steps)
            .SingleOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);

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

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Le titre du quiz est requis.");
        }

        var now = DateTime.UtcNow;
        var quiz = new Quiz
        {
            OwnerId = ownerId,
            Title = request.Title.Trim(),
            Description = request.Description,
            InviteCode = await GenerateUniqueInviteCode(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Steps = request.Steps.Select(ToStepEntity).ToList()
        };

        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = quiz.Id }, ToDetailDto(quiz));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<QuizDetailDto>> Update(int id, SaveQuizRequest request)
    {
        var ownerId = User.GetGameMasterId();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Le titre du quiz est requis.");
        }

        var quiz = await db.Quizzes
            .Include(q => q.Steps)
            .SingleOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);

        if (quiz is null)
        {
            return NotFound();
        }

        quiz.Title = request.Title.Trim();
        quiz.Description = request.Description;
        quiz.UpdatedAtUtc = DateTime.UtcNow;

        db.QuizSteps.RemoveRange(quiz.Steps);
        quiz.Steps = request.Steps.Select(ToStepEntity).ToList();

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

    private async Task<string> GenerateUniqueInviteCode()
    {
        string code;
        do
        {
            code = InviteCodeGenerator.Generate();
        } while (await db.Quizzes.AnyAsync(q => q.InviteCode == code));

        return code;
    }

    private static QuizStep ToStepEntity(QuizStepDto dto) => new()
    {
        OrderIndex = dto.OrderIndex,
        Type = dto.Type,
        Title = dto.Title,
        ConfigJson = dto.ConfigJson
    };

    private static QuizDetailDto ToDetailDto(Quiz quiz) => new(
        quiz.Id,
        quiz.Title,
        quiz.Description,
        quiz.InviteCode,
        quiz.CreatedAtUtc,
        quiz.UpdatedAtUtc,
        quiz.Steps
            .OrderBy(s => s.OrderIndex)
            .Select(s => new QuizStepDto(s.Id, s.OrderIndex, s.Type, s.Title, s.ConfigJson))
            .ToList());
}
