import { Component, computed, input, output, signal } from '@angular/core';
import { Player, Team } from '../../../models/session.model';

export interface ParticipantSelection {
  playerIds: number[];
  teamIds: number[];
}

/**
 * Sélecteur générique de participants : joueurs OU équipes (jamais les deux), avec un raccourci "tout le
 * monde". Réutilisé pour désigner les participants d'une manche restreinte comme pour choisir qui joue un
 * thème dans une manche à thèmes — la logique de sélection est identique dans les deux cas.
 */
@Component({
  selector: 'app-participant-selector',
  imports: [],
  templateUrl: './participant-selector.component.html',
  styleUrl: './participant-selector.component.scss'
})
export class ParticipantSelectorComponent {
  readonly players = input.required<Player[]>();
  readonly teams = input<Team[]>([]);
  readonly confirmLabel = input('Valider');

  readonly confirm = output<ParticipantSelection>();

  protected readonly mode = signal<'players' | 'teams'>('players');
  protected readonly selectedPlayerIds = signal<Set<number>>(new Set());
  protected readonly selectedTeamIds = signal<Set<number>>(new Set());

  protected readonly hasTeams = computed(() => this.teams().length > 0);
  protected readonly hasSelection = computed(() =>
    this.mode() === 'players' ? this.selectedPlayerIds().size > 0 : this.selectedTeamIds().size > 0
  );

  protected setMode(mode: 'players' | 'teams'): void {
    this.mode.set(mode);
  }

  protected togglePlayer(playerId: number): void {
    this.selectedPlayerIds.update((ids) => {
      const next = new Set(ids);
      next.has(playerId) ? next.delete(playerId) : next.add(playerId);
      return next;
    });
  }

  protected toggleTeam(teamId: number): void {
    this.selectedTeamIds.update((ids) => {
      const next = new Set(ids);
      next.has(teamId) ? next.delete(teamId) : next.add(teamId);
      return next;
    });
  }

  protected selectEveryone(): void {
    if (this.mode() === 'players') {
      this.selectedPlayerIds.set(new Set(this.players().map((p) => p.id)));
    } else {
      this.selectedTeamIds.set(new Set(this.teams().map((t) => t.id)));
    }
  }

  protected submit(): void {
    if (!this.hasSelection()) {
      return;
    }

    this.confirm.emit(
      this.mode() === 'players'
        ? { playerIds: [...this.selectedPlayerIds()], teamIds: [] }
        : { playerIds: [], teamIds: [...this.selectedTeamIds()] }
    );
  }
}
