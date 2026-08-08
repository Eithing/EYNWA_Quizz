import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/** Un 401 sur une requête qui portait le JWT du GM (posé par authInterceptor) signifie que le token a
 * expiré ou a été invalidé côté serveur — sans ça, l'action échouait silencieusement (bouton qui ne
 * répond plus) jusqu'à ce que le GM rafraîchisse la page par hasard. Se limite volontairement aux
 * requêtes qui avaient bien l'en-tête Authorization : un 401 sur un endpoint joueur (connectionToken
 * invalide) ne doit jamais déconnecter le GM ni rediriger vers /login. */
export const sessionExpiredInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && req.headers.has('Authorization')) {
        authService.logout();
      }
      return throwError(() => error);
    })
  );
};
