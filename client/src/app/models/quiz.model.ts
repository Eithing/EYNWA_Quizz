import { StepType } from './quiz-config.model';

export interface QuizStep {
  id?: number;
  orderIndex: number;
  type: StepType;
  title: string;
  configJson: string;
}

export interface QuizSummary {
  id: number;
  title: string;
  description?: string | null;
  inviteCode: string;
  updatedAtUtc: string;
  stepCount: number;
}

export interface QuizDetail {
  id: number;
  title: string;
  description?: string | null;
  inviteCode: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  steps: QuizStep[];
}

export interface SaveQuizRequest {
  title: string;
  description?: string | null;
  steps: QuizStep[];
}
