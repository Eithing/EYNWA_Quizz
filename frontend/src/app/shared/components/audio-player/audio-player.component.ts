import { Component, ElementRef, input, signal, viewChild } from '@angular/core';

@Component({
  selector: 'app-audio-player',
  imports: [],
  templateUrl: './audio-player.component.html',
  styleUrl: './audio-player.component.scss'
})
export class AudioPlayerComponent {
  readonly audioUrl = input.required<string>();

  protected readonly audioRef = viewChild.required<ElementRef<HTMLAudioElement>>('audioEl');
  protected readonly playing = signal(false);
  protected readonly hasPlayedOnce = signal(false);

  protected play(): void {
    const audio = this.audioRef().nativeElement;
    audio.currentTime = 0;
    audio.play();
  }

  protected onPlay(): void {
    this.playing.set(true);
    this.hasPlayedOnce.set(true);
  }

  protected onEnded(): void {
    this.playing.set(false);
  }
}
