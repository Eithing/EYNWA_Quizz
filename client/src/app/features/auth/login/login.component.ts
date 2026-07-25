import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { UiCardComponent } from '../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink, UiCardComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router
  ) {}

  protected submit(): void {
    if (!this.username().trim() || !this.password()) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.authService.login({ username: this.username(), password: this.password() }).subscribe({
      next: (response) => {
        this.authService.setSession(response);
        this.router.navigateByUrl('/');
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Identifiants invalides.');
      }
    });
  }
}
