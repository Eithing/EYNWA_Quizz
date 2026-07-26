import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface QaRoundConfig {
  validationMode: 'Auto' | 'Manual';
  autoAdvance: boolean;
  answerTimeSeconds: number;
  points: number;
  buzzerMode: boolean;
}

function defaultConfig(): QaRoundConfig {
  return { validationMode: 'Auto', autoAdvance: false, answerTimeSeconds: 20, points: 100, buzzerMode: false };
}

@Component({
  selector: 'app-qa-round-config',
  imports: [FormsModule],
  templateUrl: './qa-round-config.component.html',
  styleUrl: './qa-round-config.component.scss'
})
export class QaRoundConfigComponent {
  readonly configJson = input.required<string>();
  readonly configJsonChange = output<string>();

  protected readonly config = signal<QaRoundConfig>(defaultConfig());

  constructor() {
    effect(() => this.config.set(this.parse(this.configJson())));
  }

  private parse(json: string): QaRoundConfig {
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

  protected onPointsChange(value: number): void {
    this.config.update((c) => ({ ...c, points: value }));
    this.emit();
  }

  protected onBuzzerModeChange(value: boolean): void {
    this.config.update((c) => ({ ...c, buzzerMode: value }));
    this.emit();
  }
}
