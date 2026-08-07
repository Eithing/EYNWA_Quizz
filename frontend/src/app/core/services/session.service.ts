import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import {
  AnswerFeedItem,
  CurrentQuestionAdmin,
  GameSessionState,
  JoinSessionResponse,
  Player,
  PlayerQuestion,
  OrderSubmitResponse,
  RoundPreview,
  SubmitAnswerResponse,
  Team
} from '../../models/session.model';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/sessions`;

  constructor(private readonly http: HttpClient) {}

  // Game Master

  start(quizId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/start/${quizId}`, {});
  }

  getStateAsGm(sessionId: number) {
    return this.http.get<GameSessionState>(`${this.baseUrl}/${sessionId}/state`);
  }

  begin(sessionId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/begin`, {});
  }

  pause(sessionId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/pause`, {});
  }

  resume(sessionId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/resume`, {});
  }

  next(sessionId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/next`, {});
  }

  setScoreboardVisible(sessionId: number, visible: boolean) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/scoreboard`, { visible });
  }

  setRoundParticipants(sessionId: number, playerIds: number[], teamIds: number[]) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/round-participants`, { playerIds, teamIds });
  }

  setTeams(sessionId: number, teams: { name: string; playerIds: number[] }[]) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/teams`, { teams });
  }

  setTeamScoring(sessionId: number, enabled: boolean) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/team-scoring`, { enabled });
  }

  setRoundTeamMode(sessionId: number, enabled: boolean) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/round-team-mode`, { enabled });
  }

  adjustTeamScore(sessionId: number, teamId: number, delta: number, reason: string) {
    return this.http.post<Team>(`${this.baseUrl}/${sessionId}/teams/${teamId}/score-adjustments`, { delta, reason });
  }

  chooseTheme(sessionId: number, subRoundId: number, playerIds: number[], teamIds: number[]) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/themes/${subRoundId}/choose`, { playerIds, teamIds });
  }

  skipTheme(sessionId: number, subRoundId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/themes/${subRoundId}/skip`, {});
  }

  revealThemes(sessionId: number, subRoundId?: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/themes/reveal`, { subRoundId: subRoundId ?? null });
  }

  revealDeferredScoring(sessionId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/reveal-deferred-scoring`, {});
  }

  setPartnerGuessAnswerer(sessionId: number, playerId: number) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/partner-guess/set-answerer`, { playerId });
  }

  startPartnerGuessGuessing(sessionId: number, playerIds: number[], teamIds: number[]) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/partner-guess/start-guessing`, { playerIds, teamIds });
  }

  resolveBuzz(sessionId: number, isCorrect: boolean) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/${sessionId}/buzzer/resolve`, { isCorrect });
  }

  getPendingRoundPreview(sessionId: number) {
    return this.http.get<RoundPreview>(`${this.baseUrl}/${sessionId}/pending-round-preview`);
  }

  getCurrentQuestionFull(sessionId: number) {
    return this.http.get<CurrentQuestionAdmin>(`${this.baseUrl}/${sessionId}/current-question-full`);
  }

  getCurrentQuestionAnswers(sessionId: number) {
    return this.http.get<AnswerFeedItem[]>(`${this.baseUrl}/${sessionId}/current-question-answers`);
  }

  validateAnswer(sessionId: number, answerId: number, isCorrect: boolean) {
    return this.http.post<Player>(`${this.baseUrl}/${sessionId}/answers/${answerId}/validate`, { isCorrect });
  }

  adjustScore(sessionId: number, playerId: number, delta: number, reason: string, questionId?: number) {
    return this.http.post<Player>(`${this.baseUrl}/${sessionId}/score-adjustments`, {
      playerId,
      questionId: questionId ?? null,
      delta,
      reason
    });
  }

  // Joueurs (anonyme)

  getPublicState(token: string) {
    return this.http.get<GameSessionState>(`${this.baseUrl}/by-token/${token}`);
  }

  join(token: string, pseudo: string) {
    return this.http.post<JoinSessionResponse>(`${this.baseUrl}/by-token/${token}/join`, { pseudo });
  }

  getCurrentQuestionForPlayer(token: string, connectionToken: string) {
    return this.http.get<PlayerQuestion>(`${this.baseUrl}/by-token/${token}/current-question`, {
      params: { connectionToken }
    });
  }

  submitAnswer(token: string, connectionToken: string, rawAnswer: string) {
    return this.http.post<SubmitAnswerResponse>(`${this.baseUrl}/by-token/${token}/answer`, {
      connectionToken,
      rawAnswer
    });
  }

  buzz(token: string, connectionToken: string) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/by-token/${token}/buzz`, { connectionToken });
  }

  submitOrderDraft(token: string, connectionToken: string, itemOrder: string[]) {
    return this.http.post<GameSessionState>(`${this.baseUrl}/by-token/${token}/order-draft`, {
      connectionToken,
      itemOrder
    });
  }

  submitOrderFinal(token: string, connectionToken: string) {
    return this.http.post<OrderSubmitResponse>(`${this.baseUrl}/by-token/${token}/order-submit`, { connectionToken });
  }
}
