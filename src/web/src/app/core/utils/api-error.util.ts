import { HttpErrorResponse } from '@angular/common/http';

export function getApiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    const payload = error.error as { error?: string; message?: string; details?: unknown } | string | null;

    if (typeof payload === 'string' && payload.trim().length > 0) {
      return payload;
    }

    if (payload && typeof payload === 'object') {
      if (typeof payload.error === 'string' && payload.error.trim().length > 0) {
        return payload.error;
      }

      if (typeof payload.message === 'string' && payload.message.trim().length > 0) {
        return payload.message;
      }

      if (Array.isArray(payload.details)) {
        const firstDetail = payload.details.find((x) => typeof x === 'string' && x.trim().length > 0);
        if (typeof firstDetail === 'string' && firstDetail.trim().length > 0) {
          return firstDetail;
        }
      }
    }

    if (error.status === 0) {
      return 'Cannot reach API server. Check backend URL and CORS.';
    }

    if (error.status === 401) {
      return 'Session expired or unauthorized. Please login again.';
    }

    if (error.status === 403) {
      return 'You do not have permission for this action.';
    }
  }

  return fallback;
}
