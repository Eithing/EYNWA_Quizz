import { Component, ElementRef, effect, input, signal, viewChild } from '@angular/core';

@Component({
  selector: 'app-audio-player',
  imports: [],
  templateUrl: './audio-player.component.html',
  styleUrl: './audio-player.component.scss'
})
export class AudioPlayerComponent {
  readonly audioUrl = input.required<string>();
  /** Position de lecture autoritaire (secondes) envoyée par le serveur — utilisée uniquement aux
   * transitions de syncPaused pour resynchroniser tout le monde, jamais suivie en continu (sinon
   * ça saccade la lecture de chacun au moindre écart d'arrondi). */
  readonly syncElapsedSeconds = input<number | undefined>(undefined);
  /** Vrai pendant une pause côté serveur (buzz en attente de jugement, pause GM…) : le son doit se
   * figer pour tout le monde au même endroit, et personne ne peut relancer la lecture localement. */
  readonly syncPaused = input(false);
  /** Réservé à la prévisu host : une timeline manuelle, purement locale (n'affecte jamais les joueurs). */
  readonly showTimeline = input(false);

  protected readonly audioRef = viewChild.required<ElementRef<HTMLAudioElement>>('audioEl');
  protected readonly playing = signal(false);
  protected readonly currentTime = signal(0);
  protected readonly duration = signal(0);

  private wasSyncPaused = false;

  constructor() {
    effect(() => {
      // Nouvelle question : (re)charge la piste et la lance automatiquement.
      this.audioUrl();
      const audio = this.audioRef().nativeElement;
      audio.currentTime = 0;
      audio.play().catch(() => {
        // Autoplay bloqué par le navigateur (rare une fois qu'il y a déjà eu une interaction sur la
        // page) : le joueur peut toujours démarrer manuellement avec le bouton ▶.
      });
    });

    effect(() => {
      const paused = this.syncPaused();
      const audio = this.audioRef().nativeElement;

      if (paused && !this.wasSyncPaused) {
        audio.pause();
      } else if (!paused && this.wasSyncPaused) {
        const target = this.syncElapsedSeconds();
        if (target !== undefined) {
          audio.currentTime = target;
        }
        audio.play().catch(() => {});
      }

      this.wasSyncPaused = paused;
    });
  }

  protected onPlay(): void {
    this.playing.set(true);
  }

  protected onPause(): void {
    this.playing.set(false);
  }

  protected onTimeUpdate(): void {
    const audio = this.audioRef().nativeElement;
    this.currentTime.set(audio.currentTime);
    this.duration.set(audio.duration || 0);
  }

  protected play(): void {
    if (this.syncPaused()) {
      return;
    }
    this.audioRef().nativeElement.play().catch(() => {});
  }

  protected pause(): void {
    this.audioRef().nativeElement.pause();
  }

  protected stop(): void {
    const audio = this.audioRef().nativeElement;
    audio.pause();
    audio.currentTime = 0;
    this.currentTime.set(0);
  }

  protected seek(value: string): void {
    const audio = this.audioRef().nativeElement;
    audio.currentTime = Number(value);
  }
}
