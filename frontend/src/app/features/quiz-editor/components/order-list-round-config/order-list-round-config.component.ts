import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface OrderListRoundConfig {
  answerTimeSeconds: number;
  pointsPerChainedItem: number;
}

function defaultConfig(): OrderListRoundConfig {
  return {
    answerTimeSeconds: 60,
    pointsPerChainedItem: 20
  };
}

@Component({
  selector: 'app-order-list-round-config',
  imports: [FormsModule],
  templateUrl: './order-list-round-config.component.html',
  styleUrl: './order-list-round-config.component.scss'
})
export class OrderListRoundConfigComponent {
  readonly configJson = input.required<string>();
  readonly configJsonChange = output<string>();

  protected readonly config = signal<OrderListRoundConfig>(defaultConfig());

  constructor() {
    effect(() => this.config.set(this.parse(this.configJson())));
  }

  private parse(json: string): OrderListRoundConfig {
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

  protected onPointsPerChainedItemChange(value: number): void {
    this.config.update((c) => ({ ...c, pointsPerChainedItem: value }));
    this.emit();
  }
}
