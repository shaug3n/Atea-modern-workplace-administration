import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const files = ['atea-tokens.css', 'theme.css', 'foundations.css'];

function block(css, header) {
  const start = css.indexOf(header);
  if (start < 0) return '';
  const end = css.indexOf('\n}', start);
  return css.slice(start + header.length, end);
}

function properties(body) {
  const result = {};
  for (const match of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) result[match[1]] = match[2].trim();
  return result;
}

async function tokens(theme) {
  const merged = {};
  for (const file of files) {
    const css = await readFile(`../../src/Web/src/styles/${file}`, 'utf8');
    Object.assign(merged, properties(block(css, ':root {')));
    if (theme === 'dark') Object.assign(merged, properties(block(css, ":root[data-theme='dark'] {")));
  }
  return merged;
}

function resolve(map, name, depth = 0) {
  let value = map[name];
  if (value === undefined || depth > 10) return undefined;
  const ref = value.match(/^var\((--[\w-]+)\)$/);
  return ref ? resolve(map, ref[1], depth + 1) : value;
}

function luminance(hex) {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function ratio(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

for (const theme of ['light', 'dark']) {
  test(`${theme} theme status, muted and primary colours meet contrast thresholds`, async () => {
    const map = await tokens(theme);
    const get = (name) => {
      const value = resolve(map, name);
      assert.match(value ?? '', /^#[0-9a-f]{6}$/i, `${name} should resolve to a hex colour`);
      return value;
    };
    const surface = get('--uw-surface');
    for (const tone of ['success', 'warning', 'danger', 'info']) {
      const text = ratio(get(`--uw-status-${tone}-fg`), get(`--uw-status-${tone}-bg`));
      const border = ratio(get(`--uw-status-${tone}-border`), surface);
      console.log(`${theme} ${tone}: fg/bg ${text.toFixed(2)}, border/surface ${border.toFixed(2)}`);
      assert.ok(text >= 4.5, `${theme} ${tone} text ${text}`);
      assert.ok(border >= 3, `${theme} ${tone} border ${border}`);
    }
    const muted = ratio(get('--uw-muted'), surface);
    const primary = ratio(get('--uw-primary'), surface);
    console.log(`${theme} muted/surface ${muted.toFixed(2)}, primary/surface ${primary.toFixed(2)}`);
    assert.ok(muted >= 4.5, `${theme} muted ${muted}`);
    assert.ok(primary >= 3, `${theme} primary ${primary}`);
  });
}
