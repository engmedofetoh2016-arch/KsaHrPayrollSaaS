import { HttpErrorResponse } from '@angular/common/http';

export function getApiErrorMessage(error: unknown, fallback: string): string {
  const isArabic = isArabicUi();

  if (error instanceof HttpErrorResponse) {
    const payload = error.error as { error?: string; message?: string; details?: unknown } | string | null;

    if (typeof payload === 'string' && payload.trim().length > 0) {
      return localizeKnownApiMessage(payload, isArabic);
    }

    if (payload && typeof payload === 'object') {
      if (Array.isArray(payload.details)) {
        const firstDetail = payload.details.find((x) => typeof x === 'string' && x.trim().length > 0);
        if (typeof firstDetail === 'string' && firstDetail.trim().length > 0) {
          return localizeKnownApiMessage(firstDetail, isArabic);
        }
      }

      if (typeof payload.error === 'string' && payload.error.trim().length > 0) {
        return localizeKnownApiMessage(payload.error, isArabic);
      }

      if (typeof payload.message === 'string' && payload.message.trim().length > 0) {
        return localizeKnownApiMessage(payload.message, isArabic);
      }
    }

    if (error.status === 0) {
      return isArabic
        ? 'تعذر الوصول إلى الخادم. تحقق من عنوان الخادم وإعدادات CORS.'
        : 'Cannot reach API server. Check backend URL and CORS.';
    }

    if (error.status === 401) {
      return isArabic
        ? 'انتهت الجلسة أو أنك غير مصرح. يرجى تسجيل الدخول مرة أخرى.'
        : 'Session expired or unauthorized. Please login again.';
    }

    if (error.status === 423) {
      return isArabic
        ? 'تم قفل الحساب مؤقتًا بعد عدة محاولات دخول فاشلة. حاول مرة أخرى بعد 15 دقيقة.'
        : 'Account is temporarily locked after multiple failed logins. Try again in 15 minutes.';
    }

    if (error.status === 429) {
      return isArabic
        ? 'عدد محاولات تسجيل الدخول كبير جدًا. يرجى الانتظار دقيقة ثم إعادة المحاولة.'
        : 'Too many login attempts. Please wait a minute and retry.';
    }

    if (error.status === 403) {
      return isArabic ? 'ليس لديك صلاحية لتنفيذ هذا الإجراء.' : 'You do not have permission for this action.';
    }
  }

  return localizeKnownApiMessage(fallback, isArabic);
}

function isArabicUi(): boolean {
  if (typeof document === 'undefined') {
    return false;
  }

  return document.documentElement.lang?.toLowerCase().startsWith('ar') ?? false;
}

function localizeKnownApiMessage(message: string, isArabic: boolean): string {
  const normalized = message.trim();
  if (!isArabic || normalized.length === 0) {
    return normalized || message;
  }

  const knownMap: Record<string, string> = {
    'An unexpected error occurred.': 'حدث خطأ غير متوقع.',
    'Unexpected error': 'حدث خطأ غير متوقع.',
    'Validation failed.': 'فشل التحقق من صحة البيانات.',
    'Validation error.': 'خطأ في التحقق من صحة البيانات.'
  };

  return knownMap[normalized] ?? normalized;
}
