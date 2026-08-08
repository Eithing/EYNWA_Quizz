import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';

/** En prod apiBaseUrl vaut "" (appels relatifs, même origine que la page) ; en dev c'est une origine
 * complète (http://localhost:5100). Comparer les origines résolues gère les deux cas plutôt qu'un
 * simple startsWith, qui serait un no-op silencieux dès que apiBaseUrl est vide. */
function isApiRequest(url: string): boolean {
  const resolved = new URL(url, window.location.origin);
  const apiOrigin = environment.apiBaseUrl ? new URL(environment.apiBaseUrl).origin : window.location.origin;
  return resolved.origin === apiOrigin;
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token();

  // Jamais envoyer le JWT du GM vers autre chose que notre propre API (évite qu'un futur appel
  // vers un service tiers ne l'emporte silencieusement avec lui).
  if (!token || !isApiRequest(req.url)) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    })
  );
};
