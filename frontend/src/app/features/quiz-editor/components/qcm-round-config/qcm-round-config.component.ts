import { Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface QcmRoundConfig {
  answerTimeSeconds: number;
  autoAdvance: boolean;
  points: number;
  pointsMode: 'Uniform' | 'PerAnswer';
}

function defaultConfig(): QcmRoundConfig {
  return {
    answerTimeSeconds: 30,
    autoAdvance: false,
    points: 100,
    pointsMode: 'Uniform'
  };
}

@Component({
  selector: 'app-qcm-round-config',
  imports: [FormsModule],
  templateUrl: './qcm-round-config.component.html',
  styleUrl: './qcm-round-config.component.scss'
})
export class QcmRoundConfigComponent {
  readonly configJson = input.required<string>();
  readonly configJsonChange = output<string>();

  protected readonly config = signal<QcmRoundConfig>(defaultConfig());

  protected readonly isPerAnswer = computed(() => this.config().pointsMode === 'PerAnswer');

  constructor() {
    effect(() => this.config.set(this.parse(this.configJson())));
  }

  private parse(json: string): QcmRoundConfig {
    try {
      return { ...defaultConfig(), ...JSON.parse(json) };
    } catch {
      return defaultConfig();
    }
  }

  private emit(): void {
    this.configJsonChange.emit(JSON.stringify(this.config()));
  }

  protected onAnswerTimeChange(value: number): void {
    this.config.update((c) => ({ ...c, answerTimeSeconds: value }));
    this.emit();
  }

  protected onAutoAdvanceChange(value: boolean): void {
    this.config.update((c) => ({ ...c, autoAdvance: value }));
    this.emit();
  }

  protected onPointsChange(value: number): void {
    this.config.update((c) => ({ ...c, points: value }));
    this.emit();
  }

  protected onPointsModeChange(value: 'Uniform' | 'PerAnswer'): void {
    this.config.update((c) => ({ ...c, pointsMode: value }));
    this.emit();
  }
}
