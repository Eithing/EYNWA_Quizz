import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { QuizDetail, QuizSummary, SaveQuizRequest } from '../../models/quiz.model';

@Injectable({ providedIn: 'root' })
export class QuizService {
  private readonly baseUrl = `${environment.apiBaseUrl}/quizzes`;

  constructor(private readonly http: HttpClient) {}

  getMine() {
    return this.http.get<QuizSummary[]>(this.baseUrl);
  }

  getById(id: number) {
    return this.http.get<QuizDetail>(`${this.baseUrl}/${id}`);
  }

  create(request: SaveQuizRequest) {
    return this.http.post<QuizDetail>(this.baseUrl, request);
  }

  update(id: number, request: SaveQuizRequest) {
    return this.http.put<QuizDetail>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
