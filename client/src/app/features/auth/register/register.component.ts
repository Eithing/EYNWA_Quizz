import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { UiCardComponent } from '../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink, UiCardComponent],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router
  ) {}

  protected submit(): void {
    if (!this.username().trim() || this.password().length < 6) {
      this.error.set('Pseudo requis, mot de passe de 6 caractères minimum.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.authService.register({ username: this.username(), password: this.password() }).subscribe({
      next: (response) => {
        this.authService.setSession(response);
        this.router.navigateByUrl('/');
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.status === 409 ? 'Ce pseudo est déjà pris.' : "Échec de l'inscription.");
      }
    });
  }
}
