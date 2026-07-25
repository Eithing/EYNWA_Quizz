using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Extensions;
using Server.Hubs;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController(QuizDbContext db, IHubContext<QuizHub> hub) : ControllerBase
{
    [Authorize]
    [HttpPost("start/{quizId:int}")]
    public async Task<ActionResult<SessionStateDto>> StartSession(int quizId)
    {
        var ownerId = User.GetGameMasterId();

        var quiz = await db.Quizzes
            .Include(q => q.Steps)
            .SingleOrDefaultAsync(q => q.Id == quizId && q.OwnerId == ownerId);

        if (quiz is null)
        {
            return NotFound();
        }

        var session = await db.QuizSessions
            .Include(s => s.Players)
            .SingleOrDefaultAsync(s => s.QuizId == quizId);

        if (session is null)
        {
            session = new QuizSession
            {
                QuizId = quizId,
                Status = SessionStatus.Lobby,
                CurrentStepIndex = -1,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.QuizSessions.Add(session);
            await db.SaveChangesAsync();
        }

        return Ok(ToStateDto(session, quiz));
    }

    [Authorize]
    [HttpGet("{sessionId:int}/state")]
    public async Task<ActionResult<SessionStateDto>> GetStateAsGm(int sessionId)
    {
        var ownerId = User.GetGameMasterId();
        var session = await LoadOwnedSession(sessionId, ownerId);
        if (session is null)
        {
            return NotFound();
        }

        return Ok(ToStateDto(session, session.Quiz!));
    }

    [Authorize]
    [HttpPost("{sessionId:int}/next-step")]
    public async Task<ActionResult<SessionStateDto>> NextStep(int sessionId)
    {
        var ownerId = User.GetGameMasterId();
        var session = await LoadOwnedSession(sessionId, ownerId);
        if (session is null)
        {
            return NotFound();
        }

        var stepCount = session.Quiz!.Steps.Count;
        session.CurrentStepIndex = Math.Min(session.CurrentStepIndex + 1, stepCount);
        session.Status = session.CurrentStepIndex >= stepCount ? SessionStatus.Finished : SessionStatus.InProgress;
        await db.SaveChangesAsync();

        var state = ToStateDto(session, session.Quiz!);
        await hub.Clients.Group(session.Quiz!.InviteCode).SendAsync("StepChanged", state);

        return Ok(state);
    }

    [Authorize]
    [HttpGet("{sessionId:int}/current-step-full")]
    public async Task<ActionResult<PlayerStepDto>> GetCurrentStepFull(int sessionId)
    {
        var ownerId = User.GetGameMasterId();
        var session = await LoadOwnedSession(sessionId, ownerId);
        if (session is null)
        {
            return NotFound();
        }

        var step = session.Quiz!.Steps.OrderBy(s => s.OrderIndex).ElementAtOrDefault(session.CurrentStepIndex);
        if (step is null)
        {
            return NotFound();
        }

        return Ok(new PlayerStepDto(step.Id, step.OrderIndex, step.Type, step.Title, step.ConfigJson, false));
    }

    [AllowAnonymous]
    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<SessionStateDto>> GetPublicState(string code)
    {
        var quiz = await db.Quizzes.Include(q => q.Steps).SingleOrDefaultAsync(q => q.InviteCode == code);
        if (quiz is null)
        {
            return NotFound();
        }

        var session = await db.QuizSessions.Include(s => s.Players).SingleOrDefaultAsync(s => s.QuizId == quiz.Id);
        if (session is null)
        {
            return NotFound("La session n'a pas encore été lancée par l'hôte.");
        }

        return Ok(ToStateDto(session, quiz));
    }

    [AllowAnonymous]
    [HttpPost("by-code/{code}/join")]
    public async Task<ActionResult<JoinSessionResponse>> Join(string code, JoinSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Pseudo requis.");
        }

        var quiz = await db.Quizzes.SingleOrDefaultAsync(q => q.InviteCode == code);
        if (quiz is null)
        {
            return NotFound();
        }

        var session = await db.QuizSessions.SingleOrDefaultAsync(s => s.QuizId == quiz.Id);
        if (session is null)
        {
            return NotFound("La session n'a pas encore été lancée par l'hôte.");
        }

        var player = new Player
        {
            SessionId = session.Id,
            Name = request.Name.Trim(),
            Score = 0,
            ClientToken = Guid.NewGuid(),
            JoinedAtUtc = DateTime.UtcNow
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        await hub.Clients.Group(quiz.InviteCode).SendAsync("PlayerJoined", new PlayerDto(player.Id, player.Name, player.Score));

        return Ok(new JoinSessionResponse(player.Id, player.ClientToken, session.Id));
    }

    [AllowAnonymous]
    [HttpGet("by-code/{code}/current-step")]
    public async Task<ActionResult<PlayerStepDto>> GetCurrentStepForPlayer(string code, [FromQuery] Guid clientToken)
    {
        var quiz = await db.Quizzes.Include(q => q.Steps).SingleOrDefaultAsync(q => q.InviteCode == code);
        if (quiz is null)
        {
            return NotFound();
        }

        var session = await db.QuizSessions.SingleOrDefaultAsync(s => s.QuizId == quiz.Id);
        if (session is null)
        {
            return NotFound();
        }

        var step = quiz.Steps.OrderBy(s => s.OrderIndex).ElementAtOrDefault(session.CurrentStepIndex);
        if (step is null)
        {
            return NotFound("Aucune épreuve en cours.");
        }

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ClientToken == clientToken);
        var hasAnswered = player is not null &&
            await db.PlayerAnswers.AnyAsync(a => a.PlayerId == player.Id && a.QuizStepId == step.Id);

        return Ok(new PlayerStepDto(step.Id, step.OrderIndex, step.Type, step.Title, StepConfigSanitizer.StripAnswer(step.ConfigJson), hasAnswered));
    }

    [AllowAnonymous]
    [HttpPost("by-code/{code}/answer")]
    public async Task<ActionResult<SubmitAnswerResponse>> SubmitAnswer(string code, SubmitAnswerRequest request)
    {
        var quiz = await db.Quizzes.Include(q => q.Steps).SingleOrDefaultAsync(q => q.InviteCode == code);
        if (quiz is null)
        {
            return NotFound();
        }

        var session = await db.QuizSessions.SingleOrDefaultAsync(s => s.QuizId == quiz.Id);
        if (session is null)
        {
            return NotFound();
        }

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ClientToken == request.ClientToken);
        if (player is null)
        {
            return Unauthorized();
        }

        var step = quiz.Steps.OrderBy(s => s.OrderIndex).ElementAtOrDefault(session.CurrentStepIndex);
        if (step is null)
        {
            return BadRequest("Aucune épreuve en cours.");
        }

        var alreadyAnswered = await db.PlayerAnswers.AnyAsync(a => a.PlayerId == player.Id && a.QuizStepId == step.Id);
        if (alreadyAnswered)
        {
            return Conflict("Réponse déjà envoyée pour cette épreuve.");
        }

        var evaluation = AnswerEvaluator.Evaluate(step.ConfigJson, request.Answer);

        db.PlayerAnswers.Add(new PlayerAnswer
        {
            PlayerId = player.Id,
            QuizStepId = step.Id,
            SubmittedAnswer = request.Answer,
            IsCorrect = evaluation.IsCorrect,
            PointsAwarded = evaluation.PointsAwarded,
            SubmittedAtUtc = DateTime.UtcNow
        });

        player.Score += evaluation.PointsAwarded;
        await db.SaveChangesAsync();

        await hub.Clients.Group(quiz.InviteCode).SendAsync("ScoreUpdated", new PlayerDto(player.Id, player.Name, player.Score));

        return Ok(new SubmitAnswerResponse(evaluation.IsCorrect, evaluation.PointsAwarded, player.Score));
    }

    private async Task<QuizSession?> LoadOwnedSession(int sessionId, int ownerId)
    {
        var session = await db.QuizSessions
            .Include(s => s.Players)
            .Include(s => s.Quiz!).ThenInclude(q => q.Steps)
            .SingleOrDefaultAsync(s => s.Id == sessionId);

        return session is not null && session.Quiz?.OwnerId == ownerId ? session : null;
    }

    private static SessionStateDto ToStateDto(QuizSession session, Quiz quiz) => new(
        session.Id,
        quiz.InviteCode,
        quiz.Title,
        session.Status,
        session.CurrentStepIndex,
        quiz.Steps.Count,
        session.Players.OrderByDescending(p => p.Score).Select(p => new PlayerDto(p.Id, p.Name, p.Score)).ToList());
}
