import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './tests', workers: 1, timeout: 45000,
  use: { browserName: 'chromium', channel: 'msedge', headless: true, viewport: { width: 1200, height: 950 }, trace: 'retain-on-failure' },
  reporter: 'list', outputDir: 'test-results' });
