import { Component, OnInit, input, output, signal } from '@angular/core';
import { Player, Team } from '../../../../models/session.model';

interface TeamDraft {
  name: string;
  playerIds: Set<number>;
}

@Component({
  selector: 'app-team-builder',
  imports: [],
  templateUrl: './team-builder.component.html',
  styleUrl: './team-builder.component.scss'
})
export class TeamBuilderComponent implements OnInit {
  readonly players = input.required<Player[]>();
  readonly existingTeams = input.required<Team[]>();
  readonly save = output<{ name: string; playerIds: number[] }[]>();
  readonly cancel = output<void>();

  protected readonly teamDrafts = signal<TeamDraft[]>([]);

  ngOnInit(): void {
    const existing = this.existingTeams();
    this.teamDrafts.set(
      existing.length > 0
        ? existing.map((t) => ({ name: t.name, playerIds: new Set(t.playerIds) }))
        : [{ name: '', playerIds: new Set() }]
    );
  }

  protected addTeamDraft(): void {
    this.teamDrafts.update((drafts) => [...drafts, { name: '', playerIds: new Set() }]);
  }

  protected removeTeamDraft(index: number): void {
    this.teamDrafts.update((drafts) => drafts.filter((_, i) => i !== index));
  }

  protected renameTeamDraft(index: number, name: string): void {
    this.teamDrafts.update((drafts) => drafts.map((d, i) => (i === index ? { ...d, name } : d)));
  }

  protected toggleDraftPlayer(index: number, playerId: number): void {
    this.teamDrafts.update((drafts) =>
      drafts.map((d, i) => {
        if (i !== index) {
          return d;
        }
        const playerIds = new Set(d.playerIds);
        playerIds.has(playerId) ? playerIds.delete(playerId) : playerIds.add(playerId);
        return { ...d, playerIds };
      })
    );
  }

  protected isPlayerTakenByOtherDraft(index: number, playerId: number): boolean {
    return this.teamDrafts().some((d, i) => i !== index && d.playerIds.has(playerId));
  }

  protected submit(): void {
    const teams = this.teamDrafts()
      .filter((d) => d.name.trim().length > 0)
      .map((d) => ({ name: d.name.trim(), playerIds: [...d.playerIds] }));
    this.save.emit(teams);
  }
}
