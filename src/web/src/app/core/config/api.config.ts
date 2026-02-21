export const apiConfig = {
  baseUrl: (globalThis as { __apiBaseUrl?: string }).__apiBaseUrl ?? 'http://localhost:5202'
};
