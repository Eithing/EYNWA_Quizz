import { Component, effect, signal } from '@angular/core';
import { LoadingService } from '../../../core/services/loading.service';

const SHOW_DELAY_MS = 150;

@Component({
  selector: 'app-loading-overlay',
  imports: [],
  templateUrl: './loading-overlay.component.html',
  styleUrl: './loading-overlay.component.scss'
})
export class LoadingOverlayComponent {
  protected readonly visible = signal(false);
  private timeoutId?: ReturnType<typeof setTimeout>;

  constructor(loadingService: LoadingService) {
    effect(() => {
      clearTimeout(this.timeoutId);

      if (loadingService.isLoading()) {
        this.timeoutId = setTimeout(() => this.visible.set(true), SHOW_DELAY_MS);
      } else {
        this.visible.set(false);
      }
    });
  }
}
