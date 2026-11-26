const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

test('Node resolver script reports ESM and CommonJS conditional export targets without importing modules', t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-node-resolve-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.mkdirSync(path.join(root, 'src'), { recursive: true });
  fs.mkdirSync(path.join(root, 'node_modules', 'conditional-demo'), { recursive: true });
  fs.writeFileSync(path.join(root, 'src', 'main.mjs'), 'export {};');
  fs.writeFileSync(path.join(root, 'node_modules', 'conditional-demo', 'package.json'), JSON.stringify({
    name: 'conditional-demo',
    exports: { '.': { import: './esm.mjs', require: './common.cjs' } }
  }));
  fs.writeFileSync(path.join(root, 'node_modules', 'conditional-demo', 'esm.mjs'), 'throw new Error("must not execute");');
  fs.writeFileSync(path.join(root, 'node_modules', 'conditional-demo', 'common.cjs'), 'throw new Error("must not execute");');
  const script = `import { createRequire } from 'node:module'; import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs'; const importer=path.resolve(process.cwd(),process.argv[1]); const root=process.cwd(); if(!importer.startsWith(root+path.sep)||!fs.statSync(importer).isFile()) throw new Error('Importer must be an existing file inside the selected workspace.'); const specs=JSON.parse(process.argv[2]); const requireFrom=createRequire(importer); const results=specs.map(specifier=>{let requireResult=null,requireError=null,importResult=null,importError=null;try{requireResult=requireFrom.resolve(specifier)}catch(error){requireError=String(error.message||error).slice(0,240)}try{importResult=import.meta.resolve(specifier,pathToFileURL(importer).href)}catch(error){importError=String(error.message||error).slice(0,240)}return {specifier,require:{resolved:requireResult,error:requireError},import:{resolved:importResult,error:importError}}}); console.log(JSON.stringify({node:process.version,importer,results}));`;
  const run = spawnSync(process.execPath, ['--input-type=module', '-e', script, 'src/main.mjs', JSON.stringify(['conditional-demo', 'node:fs'])], { cwd: root, encoding: 'utf8' });
  assert.equal(run.status, 0, run.stderr);
  const result = JSON.parse(run.stdout);
  const packageResult = result.results.find(item => item.specifier === 'conditional-demo');
  assert.equal(path.basename(packageResult.require.resolved), 'common.cjs');
  assert.equal(new URL(packageResult.import.resolved).pathname.split('/').pop(), 'esm.mjs');
  assert.equal(result.results.find(item => item.specifier === 'node:fs').import.resolved, 'node:fs');
});
