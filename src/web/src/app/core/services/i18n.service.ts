import { Injectable, signal } from '@angular/core';

export type AppLanguage = 'en' | 'ar';

const STORAGE_KEY = 'hrpayroll.language';

@Injectable({ providedIn: 'root' })
export class I18nService {
  readonly language = signal<AppLanguage>(this.getInitialLanguage());

  constructor() {
    this.applyDocumentLanguage(this.language());
  }

  setLanguage(language: AppLanguage) {
    this.language.set(language);
    localStorage.setItem(STORAGE_KEY, language);
    this.applyDocumentLanguage(language);
  }

  toggleLanguage() {
    this.setLanguage(this.language() === 'en' ? 'ar' : 'en');
  }

  isArabic(): boolean {
    return this.language() === 'ar';
  }

  text(english: string, arabic: string): string {
    return this.language() === 'ar' ? arabic : english;
  }

  private getInitialLanguage(): AppLanguage {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'en' || stored === 'ar') {
      return stored;
    }

    return 'en';
  }

  private applyDocumentLanguage(language: AppLanguage) {
    const dir = language === 'ar' ? 'rtl' : 'ltr';
    document.documentElement.lang = language;
    document.documentElement.dir = dir;
    document.body.dir = dir;
    document.title = language === 'ar' ? 'نظام إدارة الموارد البشرية والرواتب' : 'KSA HR Payroll';
  }
}
