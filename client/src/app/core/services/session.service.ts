import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { JoinSessionResponse, PlayerStep, SessionState, SubmitAnswerResponse } from '../../models/session.model';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/sessions`;

  constructor(private readonly http: HttpClient) {}

  // Game Master

  start(quizId: number) {
    return this.http.post<SessionState>(`${this.baseUrl}/start/${quizId}`, {});
  }

  getStateAsGm(sessionId: number) {
    return this.http.get<SessionState>(`${this.baseUrl}/${sessionId}/state`);
  }

  nextStep(sessionId: number) {
    return this.http.post<SessionState>(`${this.baseUrl}/${sessionId}/next-step`, {});
  }

  getCurrentStepFull(sessionId: number) {
    return this.http.get<PlayerStep>(`${this.baseUrl}/${sessionId}/current-step-full`);
  }

  // Joueurs (anonyme)

  getPublicState(code: string) {
    return this.http.get<SessionState>(`${this.baseUrl}/by-code/${code}`);
  }

  join(code: string, name: string) {
    return this.http.post<JoinSessionResponse>(`${this.baseUrl}/by-code/${code}/join`, { name });
  }

  getCurrentStepForPlayer(code: string, clientToken: string) {
    return this.http.get<PlayerStep>(`${this.baseUrl}/by-code/${code}/current-step`, {
      params: { clientToken }
    });
  }

  submitAnswer(code: string, clientToken: string, answer: string) {
    return this.http.post<SubmitAnswerResponse>(`${this.baseUrl}/by-code/${code}/answer`, { clientToken, answer });
  }
}
