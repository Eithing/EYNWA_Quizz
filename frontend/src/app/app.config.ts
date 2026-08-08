import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { loadingInterceptor } from './core/interceptors/loading.interceptor';
import { sessionExpiredInterceptor } from './core/interceptors/session-expired.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // sessionExpiredInterceptor doit voir la requête déjà enrichie par authInterceptor (pour lire
    // l'en-tête Authorization), donc rester juste après lui dans la chaîne.
    provideHttpClient(withInterceptors([authInterceptor, sessionExpiredInterceptor, loadingInterceptor]))
  ]
};
