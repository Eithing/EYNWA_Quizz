import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { GameSessionState, JokerUsedEvent, Player } from '../../models/session.model';

@Injectable({ providedIn: 'root' })
export class GameSignalrService {
  private connection: signalR.HubConnection | null = null;

  async connect(inviteToken: string): Promise<void> {
    const hubUrl = `${environment.apiBaseUrl}/hubs/game`;

    this.connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).withAutomaticReconnect().build();
    this.connection.onreconnected(() => this.connection?.invoke('JoinSession', inviteToken));

    try {
      await this.connection.start();
      await this.connection.invoke('JoinSession', inviteToken);
    } catch (error) {
      // Ne jamais avaler silencieusement : sans ça, l'appli retombe sur les données
      // chargées au montage et personne ne se doute que le temps réel est cassé.
      console.error('Connexion SignalR au hub de jeu échouée, les mises à jour temps réel seront indisponibles.', error);
    }
  }

  onStateChanged(callback: (state: GameSessionState) => void): void {
    this.connection?.on('StateChanged', callback);
  }

  onPlayerJoined(callback: (player: Player) => void): void {
    this.connection?.on('PlayerJoined', callback);
  }

  onScoreUpdated(callback: (player: Player) => void): void {
    this.connection?.on('ScoreUpdated', callback);
  }

  onAnswerPendingValidation(callback: () => void): void {
    this.connection?.on('AnswerPendingValidation', callback);
  }

  onJokerUsed(callback: (event: JokerUsedEvent) => void): void {
    this.connection?.on('JokerUsed', callback);
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
  }
}
