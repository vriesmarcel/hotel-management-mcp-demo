import { build } from 'esbuild';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { Script } from 'node:vm';
await mkdir('dist', { recursive: true });
for (const name of ['reservations', 'preview']) {
  const result = await build({ entryPoints: [`${name}.js`], bundle: true, write: false,
    minify: true, format: 'iife', target: 'es2022', legalComments: 'inline' });
  const template = await readFile(`${name}.html`, 'utf8');
  const css = name === 'reservations' ? await readFile('reservations.css', 'utf8') : '';
  // Function replacements preserve literal $&, $` and $' inside bundled dependencies.
  const html = template.replace('/* APP_CSS */', () => css)
    .replace('/* APP_JS */', () => result.outputFiles[0].text.replaceAll('</script', '<\\/script'));
  new Script(html.match(/<script>([\s\S]*)<\/script>/)[1]);
  await writeFile(`dist/${name}.html`, html);
}
