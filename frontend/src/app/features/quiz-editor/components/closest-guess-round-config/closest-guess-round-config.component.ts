import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface ClosestGuessRoundConfig {
  validationMode: 'Auto' | 'Manual';
  answerTimeSeconds: number;
  points: number;
  rankBasedScoring: boolean;
  rankMaxPoints: number;
  rankPointsDecrement: number;
}

function defaultConfig(): ClosestGuessRoundConfig {
  return {
    validationMode: 'Auto',
    answerTimeSeconds: 30,
    points: 100,
    rankBasedScoring: false,
    rankMaxPoints: 100,
    rankPointsDecrement: 10
  };
}

@Component({
  selector: 'app-closest-guess-round-config',
  imports: [FormsModule],
  templateUrl: './closest-guess-round-config.component.html',
  styleUrl: './closest-guess-round-config.component.scss'
})
export class ClosestGuessRoundConfigComponent {
  readonly configJson = input.required<string>();
  readonly configJsonChange = output<string>();

  protected readonly config = signal<ClosestGuessRoundConfig>(defaultConfig());

  constructor() {
    effect(() => this.config.set(this.parse(this.configJson())));
  }

  private parse(json: string): ClosestGuessRoundConfig {
    try {
      return { ...defaultConfig(), ...JSON.parse(json) };
    } catch {
      return defaultConfig();
    }
  }

  private emit(): void {
    this.configJsonChange.emit(JSON.stringify(this.config()));
  }

  protected onValidationModeChange(value: 'Auto' | 'Manual'): void {
    this.config.update((c) => ({ ...c, validationMode: value }));
    this.emit();
  }

  protected onAnswerTimeChange(value: number): void {
    this.config.update((c) => ({ ...c, answerTimeSeconds: value }));
    this.emit();
  }

  protected onPointsChange(value: number): void {
    this.config.update((c) => ({ ...c, points: value }));
    this.emit();
  }

  protected onRankBasedScoringChange(value: boolean): void {
    this.config.update((c) => ({ ...c, rankBasedScoring: value }));
    this.emit();
  }

  protected onRankMaxPointsChange(value: number): void {
    this.config.update((c) => ({ ...c, rankMaxPoints: value }));
    this.emit();
  }

  protected onRankPointsDecrementChange(value: number): void {
    this.config.update((c) => ({ ...c, rankPointsDecrement: value }));
    this.emit();
  }
}
