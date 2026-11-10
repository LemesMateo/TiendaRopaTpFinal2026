// Captura pantallas del sitio Web para evidencias/presentación.
// Uso: node scripts/web-screenshots.mjs
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const BASE = 'http://localhost:5101';
const OUT = 'test-evidence';
const VIEWS = [
    { name: 'web-home', path: '/' },
    { name: 'web-catalogo', path: '/catalogo' },
    { name: 'web-producto', path: '/producto/9' },
    { name: 'web-login', path: '/login' }
];

mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch({ headless: true });
try {
    for (const view of VIEWS) {
        const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
        await page.goto(BASE + view.path, { waitUntil: 'networkidle' });
        await page.waitForTimeout(800);
        const file = `${OUT}/${view.name}.png`;
        await page.screenshot({ path: file, fullPage: false });
        console.log(`OK ${file} <- ${view.path}`);
        await page.close();
    }
} finally {
    await browser.close();
}
