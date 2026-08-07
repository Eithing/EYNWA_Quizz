import { Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface ZoomStep {
  level: number;
  durationSeconds: number;
  points: number;
}

interface ZoomRoundConfig {
  validationMode: 'Auto' | 'Manual';
  autoAdvance: boolean;
  answerTimeSeconds: number;
  zoomSteps: ZoomStep[];
  finalLevel: number;
  allowRetry: boolean;
  retryCooldownSeconds: number;
  rankBasedScoring: boolean;
  rankMaxPoints: number;
  rankPointsDecrement: number;
  pointsMode: 'Uniform' | 'PerAnswer';
}

function defaultConfig(): ZoomRoundConfig {
  return {
    validationMode: 'Auto',
    autoAdvance: false,
    answerTimeSeconds: 30,
    zoomSteps: [],
    finalLevel: 1,
    allowRetry: false,
    retryCooldownSeconds: 0,
    rankBasedScoring: false,
    rankMaxPoints: 100,
    rankPointsDecrement: 10,
    pointsMode: 'Uniform'
  };
}

export type ZoomScoringMode = 'Dezoom' | 'RankBased' | 'PerAnswer';

@Component({
  selector: 'app-zoom-round-config',
  imports: [FormsModule],
  templateUrl: './zoom-round-config.component.html',
  styleUrl: './zoom-round-config.component.scss'
})
export class ZoomRoundConfigComponent {
  readonly configJson = input.required<string>();
  readonly configJsonChange = output<string>();

  protected readonly config = signal<ZoomRoundConfig>(defaultConfig());

  protected readonly totalDurationSeconds = computed(() => {
    const c = this.config();
    return c.zoomSteps.reduce((sum, step) => sum + step.durationSeconds, 0) + c.answerTimeSeconds;
  });

  protected readonly scoringMode = computed<ZoomScoringMode>(() => {
    const c = this.config();
    if (c.pointsMode === 'PerAnswer') {
      return 'PerAnswer';
    }
    return c.rankBasedScoring ? 'RankBased' : 'Dezoom';
  });

  constructor() {
    effect(() => this.config.set(this.parse(this.configJson())));
  }

  private parse(json: string): ZoomRoundConfig {
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

  protected onAutoAdvanceChange(value: boolean): void {
    this.config.update((c) => ({ ...c, autoAdvance: value }));
    this.emit();
  }

  protected onAnswerTimeChange(value: number): void {
    this.config.update((c) => ({ ...c, answerTimeSeconds: value }));
    this.emit();
  }

  protected onFinalLevelChange(value: number): void {
    this.config.update((c) => ({ ...c, finalLevel: value }));
    this.emit();
  }

  protected onAllowRetryChange(value: boolean): void {
    this.config.update((c) => ({ ...c, allowRetry: value }));
    this.emit();
  }

  protected onRetryCooldownChange(value: number): void {
    this.config.update((c) => ({ ...c, retryCooldownSeconds: value }));
    this.emit();
  }

  protected onScoringModeChange(value: ZoomScoringMode): void {
    this.config.update((c) => ({
      ...c,
      rankBasedScoring: value === 'RankBased',
      pointsMode: value === 'PerAnswer' ? 'PerAnswer' : 'Uniform'
    }));
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

  protected onStepChange(index: number, field: keyof ZoomStep, value: number): void {
    this.config.update((c) => {
      const zoomSteps = [...c.zoomSteps];
      zoomSteps[index] = { ...zoomSteps[index], [field]: value };
      return { ...c, zoomSteps };
    });
    this.emit();
  }

  protected addStep(): void {
    this.config.update((c) => ({
      ...c,
      zoomSteps: [...c.zoomSteps, { level: 2, durationSeconds: 10, points: 50 }]
    }));
    this.emit();
  }

  protected removeStep(index: number): void {
    this.config.update((c) => ({ ...c, zoomSteps: c.zoomSteps.filter((_, i) => i !== index) }));
    this.emit();
  }
}
