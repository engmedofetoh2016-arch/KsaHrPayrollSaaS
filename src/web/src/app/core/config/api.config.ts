const runtimeBaseUrl = (globalThis as { __apiBaseUrl?: string }).__apiBaseUrl;

export const apiConfig = {
  baseUrl:
    runtimeBaseUrl && runtimeBaseUrl.trim().length > 0
      ? runtimeBaseUrl.trim().replace(/\/+$/, '')
      : 'http://localhost:5202'
};
