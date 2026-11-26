    'use strict';

    const PROJECT_INDEX_PREFIX = 'runmind.project-index.v6.';
    const PROJECT_INDEX_LIMITS = Object.freeze({ files: 10000, entries: 50000, depth: 10, fileBytes: 256 * 1024, totalBytes: 24 * 1024 * 1024 });
    const PROJECT_INDEX_SKIP_DIRS = new Set(['.git','.hg','.svn','.runmind-backups','.cache','node_modules','vendor','bin','obj','dist','build','out','target','.next','.nuxt','.venv','venv','__pycache__','coverage','.idea','.vs','.gradle','.terraform','.npm','.nuget','.cargo','.docker','.pypoetry','.conda','secret','secrets','credential','credentials','.ssh','.aws','.azure','.gnupg']);
    const PROJECT_INDEX_SKIP_FILES = /^(?:\.env(?:\..*)?|(?:.*\.(?:pem|key|p12|pfx|keystore|jks|kdbx|sqlite|db|wasm|dll|exe|so|dylib|bin|lockb)|.*(?:secret|credential).*)|id_(?:rsa|ed25519)|credentials?(?:\..*)?|secrets?(?:\..*)?|\.npmrc|\.pypirc|\.netrc|\.git-credentials|nuget\.config|pip\.conf|\.dockercfg|known_hosts)$/i;
    const PROJECT_INDEX_TEXT_EXTENSIONS = new Set(['.md','.mdx','.txt','.rst','.adoc','.json','.jsonc','.json5','.yaml','.yml','.toml','.ini','.cfg','.conf','.xml','.html','.htm','.svg','.css','.scss','.less','.js','.mjs','.cjs','.jsx','.ts','.tsx','.mts','.cts','.vue','.svelte','.py','.pyi','.cs','.java','.kt','.kts','.go','.rs','.c','.h','.cc','.cpp','.hpp','.hh','.php','.rb','.swift','.sh','.bash','.zsh','.ps1','.bat','.cmd','.sql','.graphql','.gql','.proto','.dockerfile','.makefile','.gradle','.properties','.lock']);

    function projectIndexKey() { return `${PROJECT_INDEX_PREFIX}${workspaceContext.id}`; }
    let projectIndexCompilerLoadPromise = null;
    async function projectIndexEnsureTypeScriptCompiler() {
      if (globalThis.ts?.createProgram) return globalThis.ts;
      if (projectIndexCompilerLoadPromise) return projectIndexCompilerLoadPromise;
      projectIndexCompilerLoadPromise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'ChatWindow/vendor/typescript/typescript.js';
        script.async = true;
        script.onload = () => globalThis.ts?.createProgram ? resolve(globalThis.ts) : reject(new Error('The TypeScript compiler bundle loaded without exposing its API.'));
        script.onerror = () => { script.remove(); reject(new Error('The bundled TypeScript compiler could not be loaded from the application files.')); };
        document.head.appendChild(script);
      }).catch(error => { projectIndexCompilerLoadPromise = null; throw error; });
      return projectIndexCompilerLoadPromise;
    }
    function projectIndexLanguage(path) {
      const ext = String(path).toLowerCase().match(/\.[^.\/]+$/)?.[0] || '';
      const map = { '.js':'javascript','.mjs':'javascript','.cjs':'javascript','.jsx':'javascript','.ts':'typescript','.tsx':'typescript','.mts':'typescript','.cts':'typescript','.json':'json','.yaml':'yaml','.yml':'yaml','.toml':'toml','.md':'markdown','.html':'html','.htm':'html','.svg':'svg','.css':'css' };
      // Other source extensions remain available through generic file reading/search, but are not treated as supported project languages.
      return map[ext] || (['Dockerfile','Makefile'].includes(path.split('/').pop()) ? 'build' : 'text');
    }
    function projectIndexDependencies(manifests) {
      const dependencies = new Set();
      for (const manifest of manifests) {
        if (manifest.path.toLowerCase() !== 'package.json') continue;
        try {
          const data = JSON.parse(manifest.text);
          for (const field of ['dependencies','devDependencies','peerDependencies','optionalDependencies'])
            for (const name of Object.keys(data[field] || {})) dependencies.add(`npm:${name}`);
        } catch (_) { /* Invalid manifests are reported by Project Diagnostics. */ }
      }
      return [...dependencies].sort().slice(0, 1000);
    }
    function projectIndexFrameworks(dependencies) {
      const names = dependencies.map(value => value.toLowerCase()), frameworks = [];
      const rules = [['React',/^npm:react$/],['Next.js',/^npm:next$/],['Vite',/^npm:vite$/],['Express',/^npm:express$/],['Fastify',/^npm:fastify$/],['Astro',/^npm:astro$/],['Svelte',/^npm:svelte$/]];
      for (const [label, pattern] of rules) if (names.some(name => pattern.test(name))) frameworks.push(label);
      return frameworks;
    }
    function projectIndexToolchains(files, manifests, observedPaths = []) {
      const names = new Set([...files.map(file => file.path), ...observedPaths].map(path => path.split('/').pop().toLowerCase()));
      const packageManifest = manifests.find(item => item.path.split('/').pop().toLowerCase() === 'package.json');
      let declaredPackageManager = '';
      if (packageManifest) {
        try {
          const value = String(JSON.parse(packageManifest.text).packageManager || '').split('@')[0].toLowerCase();
          if (['npm','pnpm','yarn','bun'].includes(value)) declaredPackageManager = value;
        } catch (_) { /* Project Diagnostics reports malformed package.json. */ }
      }
      const lockfiles = [['npm','package-lock.json'],['pnpm','pnpm-lock.yaml'],['yarn','yarn.lock'],['bun','bun.lock'],['bun','bun.lockb']]
        .filter(([,name]) => names.has(name)).map(([manager,file]) => ({manager,file}));
      const lockManagers = [...new Set(lockfiles.map(item => item.manager))];
      const selected = declaredPackageManager || (lockManagers.length === 1 ? lockManagers[0] : '');
      const conflicts = !!declaredPackageManager && lockManagers.some(manager => manager !== declaredPackageManager);
      return {
        packageManager: selected || null,
        packageManagerSource: declaredPackageManager ? 'package.json packageManager field' : lockManagers.length === 1 ? 'unique lockfile' : lockManagers.length > 1 ? 'ambiguous lockfiles' : 'not detected',
        lockfiles, conflictingPackageManagerSignals: conflicts,
        ecosystems: packageManifest ? ['node'] : []
      };
    }
    function projectIndexLocalImportGraph(files, manifests = []) {
      const known = new Set(files.map(file => file.path.replace(/\\/g,'/')));
      const normalizePath = value => {
        const parts=[];
        for(const part of String(value||'').replace(/\\/g,'/').split('/')) {
          if(!part||part==='.')continue;
          if(part==='..'){if(parts.length)parts.pop();else parts.push('..');continue;}
          parts.push(part);
        }
        return parts.join('/');
      };
      const manifestRecords = [];
      for(const manifest of manifests) {
        if(manifest.path.split('/').pop().toLowerCase()!=='package.json')continue;
        try { const data=JSON.parse(manifest.text); if(data&&typeof data==='object')manifestRecords.push({path:manifest.path,dir:manifest.path.split('/').slice(0,-1).join('/'),data}); } catch(_) {}
      }
      const packages = new Map();
      for(const record of manifestRecords)if(typeof record.data.name==='string')packages.set(record.data.name,{...record, name:record.data.name});
      const resolveFile = baseValue => {
        const base=normalizePath(baseValue);
        const extensionSubstitutions=base.endsWith('.mjs')?[base.slice(0,-4)+'.mts']:base.endsWith('.cjs')?[base.slice(0,-4)+'.cts']:base.endsWith('.jsx')?[base.slice(0,-4)+'.tsx']:base.endsWith('.js')?[base.slice(0,-3)+'.ts',base.slice(0,-3)+'.tsx',base.slice(0,-3)+'.d.ts']:[];
        const candidates=[base,...extensionSubstitutions,...['.js','.mjs','.cjs','.jsx','.ts','.tsx','.mts','.cts','.d.ts','.json','.node'].map(ext=>base+ext),...['index.js','index.mjs','index.cjs','index.jsx','index.ts','index.tsx','index.mts','index.cts','index.d.ts','index.json'].map(name=>`${base.replace(/\/$/,'')}/${name}`)];
        return candidates.find(candidate=>known.has(candidate))||null;
      };
      const collectTargets = (value, wildcard='') => {
        if(typeof value==='string')return [value.replace(/\*/g,wildcard)];
        if(Array.isArray(value))return value.flatMap(item=>collectTargets(item,wildcard));
        if(value&&typeof value==='object') {
          const keys=Object.keys(value), preferred=['node-addons','node','import','require','types','browser','development','production','default'];
          const ordered=[...preferred.filter(key=>keys.includes(key)),...keys.filter(key=>!preferred.includes(key)&&!key.startsWith('.'))];
          return ordered.flatMap(key=>collectTargets(value[key],wildcard));
        }
        return [];
      };
      const mapSubpath = (mapping, subpath, wildcard='') => {
        if(typeof mapping==='string'||Array.isArray(mapping))return subpath==='.'?collectTargets(mapping,wildcard):[];
        if(!mapping||typeof mapping!=='object')return [];
        const keys=Object.keys(mapping), hasSubpathKeys=keys.some(key=>key.startsWith('.')||key.startsWith('#'));
        if(Object.prototype.hasOwnProperty.call(mapping,subpath))return collectTargets(mapping[subpath],wildcard);
        if(!hasSubpathKeys)return subpath==='.'?collectTargets(mapping,wildcard):[];
        const patterns=keys.filter(key=>key.startsWith('.')||key.startsWith('#')).filter(key=>key.includes('*')).sort((a,b)=>{const aa=a.split('*'),bb=b.split('*');return (bb[0].length+bb[1].length)-(aa[0].length+aa[1].length);});
        for(const key of patterns) {
          const star=key.indexOf('*'),prefix=key.slice(0,star),suffix=key.slice(star+1);
          if(subpath.startsWith(prefix)&&subpath.endsWith(suffix)&&subpath.length>=prefix.length+suffix.length) {
            const value=subpath.slice(prefix.length,subpath.length-suffix.length||undefined);
            return collectTargets(mapping[key],value);
          }
        }
        return [];
      };
      const packageTargets = (record, subpath) => {
        const data=record.data;
        if(data.exports!==undefined) return mapSubpath(data.exports,subpath);
        if(subpath!=='.')return [`./${subpath.slice(2)}`];
        return [data.main,data.module,data.types,data.typings,'./index.js'].filter(value=>typeof value==='string').map(value=>value.startsWith('./')?value:`./${value.replace(/^\.\//,'')}`);
      };
      const nearestPackage = filePath => manifestRecords.filter(item=>!item.dir||filePath.startsWith(item.dir+'/')||filePath===item.path).sort((a,b)=>b.dir.length-a.dir.length)[0]||null;
      const resolveSpecifier = (file, specifier) => {
        const source=String(specifier||'');
        if(source.startsWith('.')) {
          const base=file.path.split('/').slice(0,-1).join('/');
          return resolveFile(`${base?base+'/':''}${source}`);
        }
        if(source.startsWith('#')) {
          const owner=nearestPackage(file.path); if(!owner||!owner.data.imports)return null;
          const targets=mapSubpath(owner.data.imports,source);
          for(const target of targets)if(target.startsWith('./')){const found=resolveFile(`${owner.dir?owner.dir+'/':''}${target}`);if(found)return found;}
          return null;
        }
        const parts=source.split('/');
        const packageName=source.startsWith('@')?parts.slice(0,2).join('/'):parts[0];
        const record=packages.get(packageName); if(!record)return null;
        const subpath=parts.length>(source.startsWith('@')?2:1)?`./${parts.slice(source.startsWith('@')?2:1).join('/')}`:'.';
        for(const target of packageTargets(record,subpath)) {
          if(!target.startsWith('./'))continue;
          const found=resolveFile(`${record.dir?record.dir+'/':''}${target}`); if(found)return found;
        }
        return null;
      };
      const edges = [], externalImports=[];
      const nodeBuiltins = new Set(['assert','async_hooks','buffer','child_process','cluster','console','constants','crypto','dgram','diagnostics_channel','dns','domain','events','fs','http','http2','https','inspector','module','net','os','path','perf_hooks','process','punycode','querystring','readline','repl','stream','string_decoder','sys','timers','tls','trace_events','tty','url','util','v8','vm','wasi','worker_threads','zlib','test','sqlite']);
      for (const file of files) {
        if (!['javascript','typescript'].includes(file.language)) continue;
        for (const specifier of file.imports || []) {
          const target=resolveSpecifier(file,specifier);
          if(target)edges.push({from:file.path,to:target,specifier});
          else if(!specifier.startsWith('.')&&!specifier.startsWith('#')) {
            const parts=specifier.split('/'), packageName=specifier.startsWith('@')?parts.slice(0,2).join('/'):parts[0];
            if(!packages.has(packageName)&&!specifier.startsWith('node:')&&!nodeBuiltins.has(packageName))externalImports.push({from:file.path,specifier});
          }
        }
      }
      const incoming = new Map();
      for (const edge of edges) incoming.set(edge.to,(incoming.get(edge.to)||0)+1);
      const adjacency = new Map();
      for (const edge of edges) { const list=adjacency.get(edge.from)||[]; list.push(edge.to); adjacency.set(edge.from,list); }
      const circularDependencies = [], cycleKeys = new Set();
      const canonicalCycle = cycle => { const body=cycle.slice(0,-1); if(!body.length)return ''; const rotations=body.map((_,index)=>body.slice(index).concat(body.slice(0,index)).join(' -> ')); return rotations.sort()[0]; };
      let cycleVisits = 0;
      for (const start of adjacency.keys()) {
        if(cycleVisits>2500||circularDependencies.length>=100)break;
        const walk=(current,path,active)=>{
          if(path.length>24||cycleVisits++>2500||circularDependencies.length>=100)return;
          for(const next of adjacency.get(current)||[]) {
            if(next===start) { const cycle=[...path,start], key=canonicalCycle(cycle); if(key&&!cycleKeys.has(key)){cycleKeys.add(key);circularDependencies.push(cycle);} continue; }
            if(active.has(next))continue;
            active.add(next); walk(next,[...path,next],active); active.delete(next);
          }
        };
        walk(start,[start],new Set([start]));
      }
      const byPath = new Map(files.map(file=>[file.path,file]));
      const resolvedBindings = [], unresolvedBindings = [];
      for (const file of files) for (const binding of file.importBindings || []) {
        const edge=edges.find(item=>item.from===file.path&&item.specifier===binding.source), target=edge&&byPath.get(edge.to);
        if(!target) {
          if(binding.source.startsWith('.')||binding.source.startsWith('#')||packages.has(binding.source.split('/')[0])||binding.source.startsWith('@')&&packages.has(binding.source.split('/').slice(0,2).join('/')))
            unresolvedBindings.push({from:file.path,source:binding.source,local:binding.local,imported:binding.imported,reason:'workspace-module-not-resolved'});
          continue;
        }
        const exported=(target.exports||[]).find(item=>item.exported===binding.imported || (item.exported==='*'&&binding.imported==='*'));
        if(exported) resolvedBindings.push({from:file.path,to:target.path,source:binding.source,local:binding.local,imported:binding.imported,exported:exported.exported});
        else unresolvedBindings.push({from:file.path,to:target.path,source:binding.source,local:binding.local,imported:binding.imported,reason:'export-not-found-or-not-statically-known'});
      }
      return { localImportEdges: edges.slice(0,5000), resolvedBindings:resolvedBindings.slice(0,5000), unresolvedBindings:unresolvedBindings.slice(0,5000), externalImports:externalImports.slice(0,2000), circularDependencies, filesWithIncomingImports: [...incoming].map(([path,count])=>({path,count})).sort((a,b)=>b.count-a.count||a.path.localeCompare(b.path)).slice(0,100), note:'Static Node-compatible resolution covers indexed relative paths, workspace package exports/imports, conditional export targets, package main/module fallbacks, and Node built-ins. It cannot resolve unindexed node_modules contents, custom loaders, runtime-generated specifiers, or runtime-dependent conditions; use node_resolve_modules for actual installed-package resolution through Node ESM and CommonJS resolvers.' };
    }
    function redactProjectIndexSecrets(source) {
      let text=String(source||'');
      text=text.replace(/-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----/gi,'[REDACTED PRIVATE KEY]');
      const secretKey='(?:client[_-]?secret|secret[_-]?key|auth[_-]?token|refresh[_-]?token|access[_-]?token|id[_-]?token|oauth[_-]?token|bearer[_-]?token|api[_-]?key|access[_-]?key|private[_-]?key|signing[_-]?key|password|passwd|pwd|authorization|jwt|token|secret|auth)';
      const quotedValue=new RegExp(`((?:["']?)${secretKey}(?:["']?)\\s*[:=]\\s*)(["'])[^"']*\\2`,'gi');
      text=text.replace(quotedValue,(_,prefix,quote)=>`${prefix}${quote}[REDACTED]${quote}`);
      const plainValue=new RegExp(`((?:["']?)${secretKey}(?:["']?)\\s*[:=]\\s*)(?!["'])(?:Bearer\\s+)?[^\\s,;}]+`,'gi');
      text=text.replace(plainValue,'$1[REDACTED]');
      text=text.replace(/(authorization\s*[:=]\s*)(?:Bearer\s+)?[^\r\n]+/gi,'$1[REDACTED]');
      text=text.replace(/\bBearer\s+[A-Za-z0-9._~+\/-]+=*/gi,'Bearer [REDACTED]');
      text=text.replace(/(https?:\/\/)[^/@\s:]+:[^/@\s]+@/gi,'$1[REDACTED]@');
      text=text.replace(/\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|AKIA[A-Z0-9]{16})\b/g,'[REDACTED TOKEN]');
      return text;
    }
    function projectIndexExtractJavaScript(source, path = '') {
      const parser = globalThis.acorn;
      if (!parser || typeof parser.parse !== 'function') return { supported: false, error: 'Acorn parser is unavailable.' };
      let ast, parseError = null;
      try { ast = parser.parse(source, { ecmaVersion: 'latest', sourceType: 'module', allowHashBang: true }); }
      catch (error) {
        parseError = error;
        try { ast = parser.parse(source, { ecmaVersion: 'latest', sourceType: 'script', allowHashBang: true, allowReturnOutsideFunction: true }); }
        catch (_) { return { supported: true, engine: 'acorn', parseError: { message: String(parseError.message || parseError).slice(0,300), line: parseError.loc?.line || null, column: parseError.loc?.column ?? null }, imports: [], symbols: [], symbolDetails: [], identifierOccurrences: [], references: [], calls: [], exports: [], importBindings: [] }; }
      }
      const imports = new Set(), exports = [], importBindings = [], scopes = [], scopeByNode = new WeakMap(), declarationNodes = new WeakSet(), referenceNodes = [];
      const makeScope = (parent, type, node) => { const scope={parent,type,node,bindings:new Map()}; scopes.push(scope); if(node)scopeByNode.set(node,scope); return scope; };
      const rootScope = makeScope(null,'module',ast);
      const patternNames = (pattern, out=[]) => {
        if(!pattern)return out;
        if(pattern.type==='Identifier')out.push(pattern);
        else if(pattern.type==='RestElement')patternNames(pattern.argument,out);
        else if(pattern.type==='AssignmentPattern')patternNames(pattern.left,out);
        else if(pattern.type==='ArrayPattern')for(const item of pattern.elements)patternNames(item,out);
        else if(pattern.type==='ObjectPattern')for(const item of pattern.properties)patternNames(item.type==='RestElement'?item.argument:item.value,out);
        return out;
      };
      const declare = (scope,node,kind) => { for(const id of patternNames(node)) { declarationNodes.add(id); const list=scope.bindings.get(id.name)||[]; list.push({name:id.name,kind,start:id.start,end:id.end}); scope.bindings.set(id.name,list); } };
      const childEntries = node => Object.keys(node).filter(key=>!['start','end','loc','range'].includes(key)).flatMap(key=>{const value=node[key];return Array.isArray(value)?value.filter(item=>item&&typeof item.type==='string').map(item=>[item,key]):value&&typeof value.type==='string'?[[value,key]]:[];});
      const buildScopes = (node,scope,parent=null,key='') => {
        if(!node||typeof node.type!=='string')return;
        let current=scope;
        if(node!==ast && (node.type==='BlockStatement'||node.type==='CatchClause'||node.type==='ForStatement'||node.type==='ForInStatement'||node.type==='ForOfStatement'||node.type==='SwitchStatement')) current=makeScope(scope,node.type,node);
        else if(node.type==='FunctionDeclaration'||node.type==='FunctionExpression'||node.type==='ArrowFunctionExpression') current=makeScope(scope,'function',node);
        if(node.type==='FunctionDeclaration'&&node.id)declare(scope,node.id,'function');
        if(node.type==='FunctionExpression'&&node.id)declare(current,node.id,'function-name');
        if(node.type==='ClassDeclaration'&&node.id)declare(scope,node.id,'class');
        if(node.type==='ClassExpression'&&node.id)declare(current,node.id,'class-name');
        if(node.type==='VariableDeclarator') {
          let target=current; const declarationParent=parent;
          if(declarationParent?.kind==='var'){ while(target.parent&&target.type!=='function'&&target.type!=='module')target=target.parent; }
          declare(target,node.id,declarationParent?.kind||'variable');
        }
        if(node.type==='FunctionDeclaration'||node.type==='FunctionExpression'||node.type==='ArrowFunctionExpression')for(const param of node.params||[])declare(current,param,'parameter');
        if(node.type==='CatchClause'&&node.param)declare(current,node.param,'catch');
        if(node.type==='ImportDeclaration') {
          imports.add(node.source.value);
          for(const spec of node.specifiers||[]) { declare(scope,spec.local,'import'); importBindings.push({source:node.source.value,imported:spec.type==='ImportDefaultSpecifier'?'default':spec.type==='ImportNamespaceSpecifier'?'*':spec.imported?.name||spec.imported?.value,local:spec.local.name}); }
        }
        if(node.type==='ImportExpression'&&node.source?.type==='Literal'&&typeof node.source.value==='string')imports.add(node.source.value);
        if(node.type==='CallExpression'&&node.callee?.type==='Identifier'&&node.callee.name==='require'&&node.arguments?.[0]?.type==='Literal'&&typeof node.arguments[0].value==='string')imports.add(node.arguments[0].value);
        if(node.type==='ExportNamedDeclaration') {
          if(node.source)imports.add(node.source.value);
          if(node.declaration) { const d=node.declaration; if(d.id)exports.push({exported:d.id.name,local:d.id.name,kind:'declaration'}); else if(d.declarations)for(const item of d.declarations)for(const id of patternNames(item.id))exports.push({exported:id.name,local:id.name,kind:'declaration'}); }
          for(const spec of node.specifiers||[]) {
            const exported=spec.exported?.name||spec.exported?.value, local=spec.local?.name||spec.local?.value;
            exports.push({exported,local,source:node.source?.value||null,kind:'named'});
            if(node.source)importBindings.push({source:node.source.value,imported:local,local:exported,kind:'re-export'});
          }
        }
        if(node.type==='ExportDefaultDeclaration')exports.push({exported:'default',local:node.declaration?.id?.name||null,kind:'default'});
        if(node.type==='ExportAllDeclaration') { imports.add(node.source.value); exports.push({exported:'*',local:null,source:node.source.value,kind:'all'}); }
        for(const [child,childKey] of childEntries(node))buildScopes(child,current,node,childKey);
      };
      buildScopes(ast,rootScope);
      const resolve = (scope,name) => { for(let current=scope;current;current=current.parent)if(current.bindings.has(name))return current.bindings.get(name);return null; };
      const references=[], calls=[], visitedReferenceNodes=new Set();
      const classifyAccess = (node, ancestors) => {
        let target=node;
        for(let index=ancestors.length-1;index>=0;index--) {
          const {node:parent,key}=ancestors[index];
          if(parent.type==='AssignmentExpression'&&key==='left') return parent.operator==='='?{access:'write',read:false,write:true}:{access:'read-write',read:true,write:true};
          if(parent.type==='UpdateExpression'&&key==='argument') return {access:'read-write',read:true,write:true};
          if((parent.type==='ForInStatement'||parent.type==='ForOfStatement')&&key==='left') return {access:'write',read:false,write:true};
          if(parent.type==='Property'&&parent.shorthand) continue;
          if(parent.type==='ArrayPattern'||parent.type==='ObjectPattern'||parent.type==='RestElement'||parent.type==='AssignmentPattern') { target=parent; continue; }
          if(parent.type==='MemberExpression'&&key==='object') return {access:'read',read:true,write:false};
          if(target!==node) break;
          break;
        }
        return {access:'read',read:true,write:false};
      };
      const visitReferences = (node,scope,ancestors=[]) => {
        if(!node||typeof node.type!=='string')return;
        const current=scopeByNode.get(node)||scope;
        const parentEntry=ancestors.at(-1), parent=parentEntry?.node, key=parentEntry?.key;
        if(node.type==='CallExpression'||node.type==='NewExpression') {
          const callee=node.callee;
          let calleeName='', receiver=null, resolution='dynamic', declaration=null;
          if(callee?.type==='Identifier') { calleeName=callee.name; const binding=resolve(current,calleeName)?.[0]; resolution=binding?'local':'unresolved'; declaration=binding?.start??null; }
          else if(callee?.type==='MemberExpression') { receiver=callee.object?.type==='Identifier'?callee.object.name:null; calleeName=callee.computed?(callee.property?.type==='Literal'?String(callee.property.value):'[computed]'):(callee.property?.name||''); resolution='member'; }
          let caller=null;
          for(let index=ancestors.length-1;index>=0;index--) {
            const ancestor=ancestors[index].node;
            if(ancestor.type==='FunctionDeclaration'||ancestor.type==='FunctionExpression'||ancestor.type==='ArrowFunctionExpression') {
              caller=ancestor.id?.name||null;
              if(!caller) { const owner=ancestors[index-1]?.node; if(owner?.type==='VariableDeclarator'&&owner.id?.type==='Identifier')caller=owner.id.name; else if(owner?.type==='Property'&&owner.key?.type==='Identifier')caller=owner.key.name; }
              break;
            }
          }
          calls.push({kind:node.type==='NewExpression'?'construct':'call',callee:calleeName,receiver,caller,start:node.start,end:node.end,argumentsCount:(node.arguments||[]).length,resolution,declaration});
        }
        const referenceKey=node.type==='Identifier'?`${node.start}:${node.end}:${node.name}`:'';
        if(node.type==='Identifier'&&!declarationNodes.has(node)&&!visitedReferenceNodes.has(referenceKey)) {
          visitedReferenceNodes.add(referenceKey);
          const nonReference=(parent?.type==='MemberExpression'&&key==='property'&&!parent.computed)||((parent?.type==='Property'||parent?.type==='MethodDefinition'||parent?.type==='PropertyDefinition')&&key==='key'&&!parent.computed&&!parent.shorthand)||parent?.type==='LabeledStatement'||parent?.type==='BreakStatement'||parent?.type==='ContinueStatement'||parent?.type==='ImportSpecifier'||parent?.type==='ImportDefaultSpecifier'||parent?.type==='ImportNamespaceSpecifier'||(parent?.type==='ExportSpecifier'&&key==='exported');
          if(!nonReference) {
            const bindings=resolve(current,node.name), access=classifyAccess(node,ancestors);
            references.push({name:node.name,start:node.start,end:node.end,scope:current.type,resolution:bindings?'local':'unresolved',declaration:bindings?.[0]?.start??null,...access});
          }
        }
        const next=[...ancestors,{node,key:null}];
        for(const [child,childKey] of childEntries(node)) { next[next.length-1].key=childKey; visitReferences(child,current,next); }
      };
      visitReferences(ast,rootScope);
      const symbolDetails=[];
      for(const scope of scopes)for(const [name,bindings] of scope.bindings)for(const binding of bindings){const refs=references.filter(ref=>ref.name===name&&ref.resolution==='local'&&ref.declaration===binding.start);symbolDetails.push({name,kind:binding.kind,scope:scope.type,start:binding.start,referenceCount:refs.length,referenceOccurrences:refs.length,readCount:refs.filter(ref=>ref.read).length,writeCount:refs.filter(ref=>ref.write).length,referencePositions:refs.slice(0,100).map(ref=>ref.start)});}
      symbolDetails.sort((a,b)=>a.name.localeCompare(b.name)||a.start-b.start);
      const identifierOccurrences=new Map(); for(const ref of references)identifierOccurrences.set(ref.name,(identifierOccurrences.get(ref.name)||0)+1);
      return {supported:true,engine:'acorn-semantic-local',calls:calls.slice(0,5000),imports:[...imports].slice(0,120),symbols:[...new Set(symbolDetails.map(item=>item.name))].slice(0,120),symbolDetails:symbolDetails.slice(0,500),identifierOccurrences:[...identifierOccurrences].map(([name,count])=>({name,count})).sort((a,b)=>b.count-a.count||a.name.localeCompare(b.name)).slice(0,500),references:references.slice(0,5000),exports:exports.slice(0,500),importBindings:importBindings.slice(0,500),parseError:null};
    }
    function projectIndexExtractTypeScript(source, path = '') {
      const ts = globalThis.ts;
      if (!ts?.createSourceFile) return { supported: false, error: 'The bundled TypeScript compiler API is unavailable.' };
      const text = String(source ?? '');
      const extension = String(path).toLowerCase().split('.').pop();
      const scriptKind = extension === 'tsx' ? ts.ScriptKind.TSX : extension === 'jsx' ? ts.ScriptKind.JSX : ts.ScriptKind.TS;
      const sourceFile = ts.createSourceFile(path || 'workspace.ts', text, ts.ScriptTarget.Latest, true, scriptKind);
      const diagnostics = sourceFile.parseDiagnostics || [];
      const diagnostic = diagnostics[0];
      if (diagnostic) {
        const position = diagnostic.start ?? 0;
        const location = sourceFile.getLineAndCharacterOfPosition(position);
        return { supported: true, engine: 'typescript-compiler-api', parserVersion: ts.version, parseError: { message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n').slice(0, 400), line: location.line + 1, column: location.character }, imports: [], symbols: [], symbolDetails: [], identifierOccurrences: [], references: [], calls: [], exports: [], importBindings: [], diagnostics: diagnostics.length };
      }
      const scopes = [];
      const nodeScopes = new WeakMap();
      const declarationNodes = new WeakSet();
      const references = [], calls = [], imports = new Set(), exports = [], importBindings = [];
      const root = { parent: null, type: 'module', node: sourceFile, bindings: new Map() };
      scopes.push(root);
      const makeScope = (parent, type, node) => { const scope = { parent, type, node, bindings: new Map() }; scopes.push(scope); return scope; };
      const bindingIdentifiers = (node, output = []) => {
        if (!node) return output;
        if (ts.isIdentifier(node)) output.push(node);
        else if (ts.isObjectBindingPattern(node) || ts.isArrayBindingPattern(node)) for (const element of node.elements) if (element && !ts.isOmittedExpression(element)) bindingIdentifiers(ts.isBindingElement(element) ? element.name : element, output);
        return output;
      };
      const declare = (scope, node, kind) => {
        for (const identifier of bindingIdentifiers(node)) {
          declarationNodes.add(identifier);
          const name = identifier.text;
          const list = scope.bindings.get(name) || [];
          list.push({ name, kind, start: identifier.getStart(sourceFile), end: identifier.end });
          scope.bindings.set(name, list);
        }
      };
      const modifiers = node => ts.canHaveModifiers(node) ? (ts.getModifiers(node) || []) : [];
      const hasModifier = (node, kind) => modifiers(node).some(item => item.kind === kind);
      const nameText = node => node && (ts.isIdentifier(node) || ts.isStringLiteralLike(node) || ts.isNumericLiteral(node)) ? node.text : '';
      const addExport = (exported, local, kind, node) => { if (exported) exports.push({ exported, local: local || exported, kind, start: node?.getStart(sourceFile) ?? 0 }); };
      const functionAt = node => { let current = node; while (current && current !== sourceFile) { if (ts.isFunctionLike(current)) { if (current.name && ts.isIdentifier(current.name)) return current.name.text; const owner=current.parent; if (owner && ts.isVariableDeclaration(owner) && ts.isIdentifier(owner.name)) return owner.name.text; if (owner && ts.isPropertyAssignment(owner) && ts.isIdentifier(owner.name)) return owner.name.text; } current = current.parent; } return null; };
      const walk = (node, parentScope, parent = null) => {
        let scope = parentScope;
        if (node !== sourceFile && (ts.isBlock(node) || ts.isModuleBlock(node))) scope = makeScope(parentScope, 'Block', node);
        if (ts.isFunctionLike(node)) scope = makeScope(parentScope, 'function', node);
        if (ts.isClassLike(node)) scope = makeScope(parentScope, 'class', node);
        nodeScopes.set(node, scope);

        if (ts.isFunctionDeclaration(node) && node.name) declare(parentScope, node.name, 'function');
        else if (ts.isClassDeclaration(node) && node.name) declare(parentScope, node.name, 'class');
        else if (ts.isFunctionExpression(node) && node.name) declare(scope, node.name, 'function-name');
        else if (ts.isClassExpression(node) && node.name) declare(scope, node.name, 'class-name');
        else if (ts.isInterfaceDeclaration(node)) declare(parentScope, node.name, 'interface');
        else if (ts.isTypeAliasDeclaration(node)) declare(parentScope, node.name, 'type');
        else if (ts.isEnumDeclaration(node)) declare(parentScope, node.name, 'enum');
        else if (ts.isModuleDeclaration(node) && ts.isIdentifier(node.name)) declare(parentScope, node.name, 'namespace');
        else if (ts.isTypeParameterDeclaration(node)) declare(scope, node.name, 'type-parameter');
        else if (ts.isMethodDeclaration(node) || ts.isMethodSignature(node)) { if (ts.isIdentifier(node.name)) { declarationNodes.add(node.name); const list=parentScope.bindings.get(node.name.text)||[]; list.push({name:node.name.text,kind:'method',start:node.name.getStart(sourceFile),end:node.name.end}); parentScope.bindings.set(node.name.text,list); } }
        else if (ts.isPropertyDeclaration(node) || ts.isPropertySignature(node)) { if (ts.isIdentifier(node.name)) { declarationNodes.add(node.name); const list=scope.bindings.get(node.name.text)||[]; list.push({name:node.name.text,kind:'property',start:node.name.getStart(sourceFile),end:node.name.end}); scope.bindings.set(node.name.text,list); } }
        else if (ts.isVariableDeclaration(node)) {
          const list = parent && ts.isVariableDeclarationList(parent) ? parent : null;
          const isVar = !!list && (list.flags & ts.NodeFlags.BlockScoped) === 0;
          let target = scope;
          if (isVar) while (target.parent && target.type !== 'function' && target.type !== 'module') target = target.parent;
          declare(target, node.name, isVar ? 'var' : 'variable');
        }
        if (ts.isParameter(node)) declare(scope, node.name, 'parameter');
        if (ts.isCatchClause(node) && node.variableDeclaration) declare(scope, node.variableDeclaration.name, 'catch');

        if (ts.isImportDeclaration(node) && ts.isStringLiteralLike(node.moduleSpecifier)) {
          const specifier = node.moduleSpecifier.text; imports.add(specifier);
          const clause = node.importClause;
          if (clause?.name) { declare(root, clause.name, 'import'); importBindings.push({ source: specifier, imported: 'default', local: clause.name.text, kind: 'import' }); }
          if (clause?.namedBindings && ts.isNamespaceImport(clause.namedBindings)) {
            declare(root, clause.namedBindings.name, 'import'); importBindings.push({ source: specifier, imported: '*', local: clause.namedBindings.name.text, kind: 'import' });
          } else if (clause?.namedBindings && ts.isNamedImports(clause.namedBindings)) {
            for (const element of clause.namedBindings.elements) {
              if (element.propertyName) declarationNodes.add(element.propertyName);
              declare(root, element.name, 'import');
              importBindings.push({ source: specifier, imported: nameText(element.propertyName || element.name), local: element.name.text, kind: 'import' });
            }
          }
        }
        if (ts.isImportTypeNode(node) && node.argument && ts.isLiteralTypeNode(node.argument) && ts.isStringLiteralLike(node.argument.literal)) imports.add(node.argument.literal.text);
        if (ts.isImportEqualsDeclaration(node)) {
          declare(root, node.name, 'import');
          if (ts.isExternalModuleReference(node.moduleReference) && node.moduleReference.expression && ts.isStringLiteralLike(node.moduleReference.expression)) {
            const specifier = node.moduleReference.expression.text; imports.add(specifier); importBindings.push({ source: specifier, imported: 'export=', local: node.name.text, kind: 'import-equals' });
          }
        }
        if (ts.isExportDeclaration(node)) {
          const specifier = node.moduleSpecifier && ts.isStringLiteralLike(node.moduleSpecifier) ? node.moduleSpecifier.text : '';
          if (specifier) imports.add(specifier);
          if (node.exportClause && ts.isNamedExports(node.exportClause)) for (const element of node.exportClause.elements) {
            const imported = nameText(element.propertyName || element.name), exported = element.name.text;
            declarationNodes.add(element.name); if (element.propertyName) declarationNodes.add(element.propertyName);
            if (specifier) importBindings.push({ source: specifier, imported, local: exported, kind: 're-export' });
            else addExport(exported, imported, 'specifier', element);
          }
          if (node.exportClause && ts.isNamespaceExport(node.exportClause)) addExport(node.exportClause.name.text, '*', 'namespace', node.exportClause);
        }
        if (ts.isExportAssignment(node)) addExport(node.isExportEquals ? 'export=' : 'default', node.expression && ts.isIdentifier(node.expression) ? node.expression.text : (node.isExportEquals ? 'export=' : 'default'), node.isExportEquals ? 'export-equals' : 'default', node);
        if ((ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) && ts.isIdentifier(node.tagName)) calls.push({ kind:'jsx', callee:node.tagName.text, receiver:null, start:node.getStart(sourceFile), end:node.end, caller:functionAt(node), resolution:'unresolved', declaration:null });

        if (ts.isVariableStatement(node) && hasModifier(node, ts.SyntaxKind.ExportKeyword)) for (const declaration of node.declarationList.declarations) for (const identifier of bindingIdentifiers(declaration.name)) addExport(identifier.text, identifier.text, 'declaration', identifier);
        if ((ts.isFunctionDeclaration(node) || ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node) || ts.isEnumDeclaration(node)) && node.name && hasModifier(node, ts.SyntaxKind.ExportKeyword) && !hasModifier(node, ts.SyntaxKind.DefaultKeyword)) addExport(node.name.text, node.name.text, 'declaration', node.name);
        if (ts.isFunctionDeclaration(node) && hasModifier(node, ts.SyntaxKind.DefaultKeyword)) addExport('default', node.name?.text || 'default', 'default', node);

        if (ts.isCallExpression(node) || ts.isNewExpression(node)) {
          const expression = node.expression;
          if (ts.isCallExpression(node) && (expression.kind === ts.SyntaxKind.ImportKeyword || (ts.isIdentifier(expression) && expression.text === 'require')) && node.arguments?.[0] && ts.isStringLiteralLike(node.arguments[0])) imports.add(node.arguments[0].text);
          let callee = '', receiver = '';
          if (ts.isIdentifier(expression)) callee = expression.text;
          else if (ts.isPropertyAccessExpression(expression)) { callee = expression.name.text; receiver = expression.expression.getText(sourceFile); }
          else callee = expression.getText(sourceFile).slice(0, 180);
          calls.push({ kind: ts.isNewExpression(node) ? 'construct' : 'call', callee, receiver: receiver || null, start: node.getStart(sourceFile), end: node.end, caller: functionAt(node), resolution: 'unresolved', declaration: null });
        }
        ts.forEachChild(node, child => walk(child, scope, node));
      };
      walk(sourceFile, root, null);

      const isPropertyName = (node, parent) => (ts.isPropertyAccessExpression(parent) && parent.name === node) || ((ts.isPropertyAssignment(parent) || ts.isMethodDeclaration(parent) || ts.isPropertyDeclaration(parent) || ts.isMethodSignature(parent) || ts.isPropertySignature(parent)) && parent.name === node && !ts.isComputedPropertyName(parent.name));
      const accessFor = (node, parent) => {
        if (parent && (ts.isPrefixUnaryExpression(parent) || ts.isPostfixUnaryExpression(parent)) && parent.operand === node && [ts.SyntaxKind.PlusPlusToken, ts.SyntaxKind.MinusMinusToken].includes(parent.operator)) return 'read-write';
        if (parent && ts.isBinaryExpression(parent) && parent.left === node && ts.isAssignmentOperator(parent.operatorToken.kind)) return parent.operatorToken.kind === ts.SyntaxKind.EqualsToken ? 'write' : 'read-write';
        return 'read';
      };
      const resolve = (scope, name, position) => {
        for (let current = scope; current; current = current.parent) {
          const list = current.bindings.get(name);
          if (list) { const found = list.filter(item => item.start <= position || ['function','class','interface','type','enum','namespace','import','parameter','var'].includes(item.kind)).sort((a,b) => b.start-a.start)[0]; if (found) return found; }
        }
        return null;
      };
      const collectReferences = (node, parent = null) => {
        if (ts.isIdentifier(node) && !declarationNodes.has(node) && !isPropertyName(node, parent) && !(parent && ts.isShorthandPropertyAssignment(parent) && parent.name !== node)) {
          const start = node.getStart(sourceFile), access = accessFor(node, parent), binding = resolve(nodeScopes.get(node) || root, node.text, start);
          references.push({ name: node.text, start, end: node.end, access, read: access !== 'write', write: access !== 'read', resolution: binding ? 'local' : 'unresolved', declaration: binding?.start ?? null });
        }
        ts.forEachChild(node, child => collectReferences(child, node));
      };
      collectReferences(sourceFile);
      const symbols = [];
      for (const scope of scopes) for (const [name, bindings] of scope.bindings) for (const binding of bindings) {
        const refs = references.filter(item => item.declaration === binding.start);
        symbols.push({ ...binding, scope: scope.type, referenceCount: refs.length, readCount: refs.filter(item => item.read).length, writeCount: refs.filter(item => item.write).length });
      }
      const occurrenceMap = new Map(); for (const ref of references) occurrenceMap.set(ref.name, (occurrenceMap.get(ref.name) || 0) + 1);
      return { supported: true, engine: 'typescript-compiler-api', parserVersion: ts.version, parseError: null, imports: [...imports].slice(0, 200), symbols: [...new Set(symbols.map(item => item.name))].slice(0, 200), symbolDetails: symbols.slice(0, 1000), identifierOccurrences: [...occurrenceMap].map(([name,count]) => ({ name,count })).sort((a,b) => b.count-a.count || a.name.localeCompare(b.name)).slice(0,1000), references: references.slice(0,10000), calls: calls.slice(0,5000), exports: exports.slice(0,1000), importBindings: importBindings.slice(0,1000), diagnostics: 0 };
    }

    function projectIndexRefactorPublicApi(targetPath, targetSource, consumers, oldName, newName) {
      const ts = globalThis.ts;
      if (!ts?.createSourceFile) throw new Error('The TypeScript compiler API is unavailable.');
      if (!/^[A-Za-z_$][\w$]*$/.test(String(newName || '')) || !/^[A-Za-z_$][\w$]*$/.test(String(oldName || ''))) throw new Error('Provide valid old and new exported identifiers.');
      if (oldName === newName) return { changed: false, changes: [], message: 'The exported symbol already has that name.' };
      const parse = (path, text) => {
        const ext = String(path).toLowerCase().split('.').pop();
        const kind = ext === 'tsx' ? ts.ScriptKind.TSX : ext === 'jsx' ? ts.ScriptKind.JSX : ts.ScriptKind.TS;
        const file = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true, kind);
        if (file.parseDiagnostics?.length) throw new Error(`Cannot refactor '${path}': ${ts.flattenDiagnosticMessageText(file.parseDiagnostics[0].messageText, '\n')}`);
        return file;
      };
      const applyEdits = (text, edits, parsePath = 'preview.ts') => {
        const sorted = edits.sort((a,b) => b.start-a.start);
        for (let i=1;i<sorted.length;i++) if (sorted[i-1].start < sorted[i].end) throw new Error('Overlapping syntax edits were detected; public API rename was blocked.');
        let content=text;
        for (const edit of sorted) content=content.slice(0,edit.start)+edit.value+content.slice(edit.end);
        const outputFile = parse(parsePath, content);
        return { content, edits: edits.length, diagnostics: outputFile.parseDiagnostics?.length || 0 };
      };
      const targetText=String(targetSource ?? ''), targetFile=parse(targetPath,targetText), targetEdits=[];
      const modifiers=node=>ts.canHaveModifiers(node)?(ts.getModifiers(node)||[]):[];
      const exported=(node)=>modifiers(node).some(item=>item.kind===ts.SyntaxKind.ExportKeyword);
      let match=null;
      for(const statement of targetFile.statements) {
        if(ts.isVariableStatement(statement)&&exported(statement)) for(const declaration of statement.declarationList.declarations) if(ts.isIdentifier(declaration.name)&&declaration.name.text===oldName) match={kind:'direct',node:declaration.name,local:oldName};
        if((ts.isFunctionDeclaration(statement)||ts.isClassDeclaration(statement)||ts.isInterfaceDeclaration(statement)||ts.isTypeAliasDeclaration(statement)||ts.isEnumDeclaration(statement))&&exported(statement)&&!modifiers(statement).some(item=>item.kind===ts.SyntaxKind.DefaultKeyword)&&statement.name?.text===oldName) match={kind:'direct',node:statement.name,local:oldName};
        if(ts.isExportDeclaration(statement)&&!statement.moduleSpecifier&&statement.exportClause&&ts.isNamedExports(statement.exportClause)) for(const element of statement.exportClause.elements) if(element.name.text===oldName) match={kind:'specifier',node:element,local:(element.propertyName||element.name).text,aliased:!!element.propertyName};
      }
      if(!match) throw new Error(`No direct named export '${oldName}' was found in '${targetPath}'. Default exports and indirect export-star chains are not renamed by this operation.`);
      if(match.kind==='direct') {
        const bindingStart=match.node.getStart(targetFile);
        targetEdits.push({start:bindingStart,end:match.node.end,value:newName});
        const analysis=projectIndexExtractTypeScript(targetText,targetPath);
        for(const reference of analysis.references||[]) if(reference.declaration===bindingStart) targetEdits.push({start:reference.start,end:reference.end,value:newName});
      } else if(match.aliased) {
        targetEdits.push({start:match.node.name.getStart(targetFile),end:match.node.name.end,value:newName});
      } else {
        targetEdits.push({start:match.node.name.getStart(targetFile),end:match.node.name.end,value:`${oldName} as ${newName}`});
      }
      const targetResult=applyEdits(targetText,targetEdits,targetPath);
      const changes=[{path:targetPath,content:targetResult.content,edits:targetResult.edits}];
      const seen=new Set([targetPath]);
      for(const consumer of consumers||[]) {
        if(seen.has(consumer.path)) continue;
        const source=String(consumer.text??''), file=parse(consumer.path,source), edits=[];
        const matchingSources=new Set(consumer.sources||[]);
        const visit=node=>{
          if(ts.isImportDeclaration(node)&&node.importClause&&ts.isStringLiteralLike(node.moduleSpecifier)&&matchingSources.has(node.moduleSpecifier.text)&&node.importClause.namedBindings&&ts.isNamedImports(node.importClause.namedBindings)) {
            for(const element of node.importClause.namedBindings.elements) {
              const imported=(element.propertyName||element.name).text;
              if(imported!==oldName) continue;
              if(element.propertyName) edits.push({start:element.propertyName.getStart(file),end:element.propertyName.end,value:newName});
              else edits.push({start:element.name.getStart(file),end:element.name.end,value:`${newName} as ${element.name.text}`});
            }
          }
          if(ts.isExportDeclaration(node)&&node.moduleSpecifier&&ts.isStringLiteralLike(node.moduleSpecifier)&&matchingSources.has(node.moduleSpecifier.text)&&node.exportClause&&ts.isNamedExports(node.exportClause)) {
            for(const element of node.exportClause.elements) if((element.propertyName||element.name).text===oldName) {
              if(element.propertyName) edits.push({start:element.propertyName.getStart(file),end:element.propertyName.end,value:newName});
              else edits.push({start:element.name.getStart(file),end:element.name.end,value:`${newName} as ${element.name.text}`});
            }
          }
          ts.forEachChild(node,visit);
        };
        visit(file);
        if(edits.length) { const result=applyEdits(source,edits,consumer.path); changes.push({path:consumer.path,content:result.content,edits:result.edits}); seen.add(consumer.path); }
      }
      if (changes.length > 20) throw new Error(`The public API rename affects ${changes.length} files; the atomic multi-file editor supports at most 20 files. Narrow the refactor or split the API migration into reviewed steps.`);
      return { action:'rename-public-api', changed:true, symbol:{oldName,newName,exportedFrom:targetPath,localName:match.local}, changes, totalEdits:changes.reduce((sum,item)=>sum+item.edits,0), message:`Prepared a public API rename across ${changes.length} file(s). Importer local names are preserved with aliases where necessary. Review every file preview before applying.` };
    }

    function projectIndexRefactorJavaScript(source, { action = 'rename-symbol', oldName = '', newName = '', declarationStart = null, selectionEnd = null } = {}) {
      const parser = globalThis.acorn;
      if (!parser || typeof parser.parse !== 'function') throw new Error('Acorn parser is unavailable.');
      const text = String(source ?? '');
      let ast;
      const comments = [];
      try { ast = parser.parse(text, { ecmaVersion: 'latest', sourceType: 'module', allowHashBang: true, onComment: comments }); }
      catch (moduleError) {
        if (action === 'organize-imports') throw new Error(`Import organization requires a successfully parsed ES module: ${moduleError.message || moduleError}`);
        comments.length = 0;
        try { ast = parser.parse(text, { ecmaVersion: 'latest', sourceType: 'script', allowHashBang: true, allowReturnOutsideFunction: true, onComment: comments }); }
        catch (scriptError) { throw new Error(`JavaScript refactoring requires a successfully parsed source file: ${scriptError.message || scriptError}`); }
      }

      if (action === 'organize-imports') {
        const imports = (ast.body || []).filter(node => node.type === 'ImportDeclaration');
        if (imports.length < 2) return { action, changed: false, content: text, edits: 0, message: 'Fewer than two top-level imports; no changes needed.' };
        const edits = [];
        let run = [];
        let runHasLeadingComment = comments.some(comment => comment.start < (imports[0]?.start ?? 0));
        const flush = () => {
          if (run.length < 2 || runHasLeadingComment) { run = []; runHasLeadingComment = false; return; }
          if (run.some(node => !node.specifiers?.length)) { run = []; runHasLeadingComment = false; return; }
          const original = run.map(node => text.slice(node.start, node.end));
          const sorted = run.map((node, index) => ({ node, value: original[index], key: String(node.source?.value || '').toLowerCase(), index }))
            .sort((a, b) => a.key.localeCompare(b.key) || a.index - b.index).map(item => item.value);
          if (sorted.some((value, index) => value !== original[index])) edits.push({ start: run[0].start, end: run[run.length - 1].end, value: sorted.join(text.includes('\r\n') ? '\r\n' : '\n') });
          run = [];
          runHasLeadingComment = false;
        };
        for (const node of imports) {
          const previous = run.at(-1);
          if (previous) {
            const gapStart = previous.end, gapEnd = node.start;
            const gap = text.slice(gapStart, gapEnd);
            const hasComment = comments.some(comment => comment.start >= gapStart && comment.end <= gapEnd);
            const blankLine = (gap.match(/\r?\n/g) || []).length > 1;
            if (!/^\s*$/.test(gap) || hasComment || blankLine) { flush(); runHasLeadingComment = hasComment; }
          }
          run.push(node);
        }
        flush();
        let content = text;
        for (const edit of edits.sort((a,b) => b.start-a.start)) content = content.slice(0, edit.start) + edit.value + content.slice(edit.end);
        try { parser.parse(content, { ecmaVersion: 'latest', sourceType: 'module', allowHashBang: true }); }
        catch (error) { throw new Error(`Import organization produced invalid JavaScript; no change was returned: ${error.message || error}`); }
        return { action, changed: edits.length > 0, content, edits: edits.length, message: edits.length ? `Reordered ${edits.length} independent import group(s). Comments and separated import groups were preserved.` : 'Imports are already organized.' };
      }

      if (action === 'extract-function') {
        const functionName = String(newName || '');
        const selectionStart = Number(declarationStart);
        const selectionEndOffset = Number(selectionEnd);
        if (!/^[A-Za-z_$][\w$]*$/.test(functionName) || !Number.isInteger(selectionStart) || !Number.isInteger(selectionEndOffset) || selectionStart < 0 || selectionEndOffset <= selectionStart)
          throw new Error('Extract function requires a valid function_name, selection_start and selection_end source offsets.');
        try { parser.parse(`function ${functionName}() {}`, { ecmaVersion: 'latest', sourceType: 'module' }); }
        catch (_) { throw new Error(`'${functionName}' is not a valid JavaScript function name.`); }
        const analysis = projectIndexExtractJavaScript(text);
        if (analysis.parseError) throw new Error(`Cannot extract from a file with parse errors: ${analysis.parseError.message}`);
        if ((analysis.symbolDetails || []).some(item => item.name === functionName) || (analysis.references || []).some(item => item.name === functionName && item.resolution === 'unresolved')) throw new Error(`A '${functionName}' binding or unresolved reference already exists in this file; choose another function name.`);
        let selectedNodes = null;
        const findStatements = node => {
          if (!node || typeof node.type !== 'string' || selectedNodes) return;
          if ((node.type === 'Program' || node.type === 'BlockStatement') && Array.isArray(node.body)) {
            const startIndex = node.body.findIndex(item => item.start === selectionStart);
            if (startIndex >= 0) {
              for (let endIndex = startIndex; endIndex < node.body.length; endIndex++) {
                if (node.body[endIndex].end === selectionEndOffset) { selectedNodes = node.body.slice(startIndex, endIndex + 1); break; }
                if (node.body[endIndex].end > selectionEndOffset) break;
              }
            }
          }
          for (const key of Object.keys(node)) {
            if (['start','end','loc','range'].includes(key)) continue;
            const value = node[key];
            if (Array.isArray(value)) { for (const child of value) if (child && typeof child.type === 'string') findStatements(child); }
            else if (value && typeof value.type === 'string') findStatements(value);
          }
        };
        findStatements(ast);
        if (!selectedNodes?.length || selectedNodes.at(-1).end !== selectionEndOffset) throw new Error('The selected source offsets must exactly cover one or more consecutive statements in the same Program or block body.');
        const forbidden = new Set(['VariableDeclaration','FunctionDeclaration','ClassDeclaration','ReturnStatement','BreakStatement','ContinueStatement','AwaitExpression','YieldExpression','ThisExpression','Super','WithStatement','LabeledStatement']);
        let unsafeReason = '';
        const inspect = node => {
          if (!node || typeof node.type !== 'string' || unsafeReason) return;
          if (forbidden.has(node.type)) { unsafeReason = node.type; return; }
          if (node.type === 'Identifier' && node.name === 'arguments') { unsafeReason = 'arguments'; return; }
          if (node.type === 'MetaProperty' && node.meta?.name === 'new' && node.property?.name === 'target') { unsafeReason = 'new.target'; return; }
          if (node.type === 'CallExpression' && node.callee?.type === 'Identifier' && node.callee.name === 'eval') { unsafeReason = 'direct eval'; return; }
          for (const key of Object.keys(node)) {
            if (['start','end','loc','range'].includes(key)) continue;
            const value = node[key];
            if (Array.isArray(value)) { for (const child of value) if (child && typeof child.type === 'string') inspect(child); }
            else if (value && typeof value.type === 'string') inspect(value);
          }
        };
        for (const node of selectedNodes) inspect(node);
        if (unsafeReason) throw new Error(`Extraction was blocked because the selection contains '${unsafeReason}', whose control-flow or binding semantics would change when moved into a function.`);
        const lineStart = text.lastIndexOf('\n', selectionStart - 1) + 1;
        const indent = text.slice(lineStart, selectionStart);
        if (!/^[\t ]*$/.test(indent)) throw new Error('The first selected statement must begin after indentation only; inline statement extraction is not supported.');
        const selectedText = text.slice(selectionStart, selectionEndOffset);
        const lines = selectedText.split(/\r?\n/);
        const commonIndent = indent.length;
        const newline = text.includes('\r\n') ? '\r\n' : '\n';
        const body = lines.map(line => { if (!line.trim().length) return ''; const leading = line.match(/^[\t ]*/)[0].length; return indent + '  ' + line.slice(Math.min(commonIndent, leading)); }).join(newline);
        const replacement = `function ${functionName}() {${newline}${body}${newline}${indent}}${newline}${indent}${functionName}();`;
        const content = text.slice(0, selectionStart) + replacement + text.slice(selectionEndOffset);
        try { parser.parse(content, { ecmaVersion: 'latest', sourceType: 'module', allowHashBang: true }); }
        catch (_) { parser.parse(content, { ecmaVersion: 'latest', sourceType: 'script', allowHashBang: true, allowReturnOutsideFunction: true }); }
        return { action, changed: true, content, edits: 1, functionName, extractedStatements: selectedNodes.length, message: `Prepared extraction of ${selectedNodes.length} statement(s) into '${functionName}'. Variable declarations and control-flow-sensitive constructs are deliberately rejected; outer bindings are captured by the new function. Review before applying.` };
      }

      if (action !== 'rename-symbol') throw new Error(`Unsupported JavaScript refactoring action: ${action}`);
      if (!/^[A-Za-z_$][\w$]*$/.test(String(newName)) || !String(oldName)) throw new Error('Provide a valid oldName and a valid JavaScript identifier for newName.');
      try { parser.parse(`let ${newName};`, { ecmaVersion: 'latest', sourceType: 'module' }); }
      catch (_) { throw new Error(`'${newName}' is not a valid JavaScript binding identifier.`); }
      const analysis = projectIndexExtractJavaScript(text);
      if (analysis.parseError) throw new Error(`Cannot rename in a file with parse errors: ${analysis.parseError.message}`);
      const candidates = (analysis.symbolDetails || []).filter(item => item.name === oldName && (declarationStart == null || item.start === Number(declarationStart)));
      if (!candidates.length) throw new Error(`No declaration for '${oldName}' matched the requested scope.`);
      if (candidates.length > 1) throw new Error(`'${oldName}' has ${candidates.length} declarations in this file. Supply declarationStart from project_symbol_graph to select one safely.`);
      const binding = candidates[0];
      if (binding.name === newName) return { action, changed: false, content: text, edits: 0, message: 'The symbol already has that name.' };
      const collision = (analysis.symbolDetails || []).some(item => item.name === newName && item.scope === binding.scope && item.start !== binding.start);
      if (collision) throw new Error(`A '${newName}' declaration already exists in the same scope category; rename was blocked to avoid a collision.`);
      const positions = [binding.start, ...(analysis.references || []).filter(item => item.declaration === binding.start).map(item => item.start)];
      const uniquePositions = [...new Set(positions)];
      const replacements = new Map(uniquePositions.map(start => [start, newName]));
      const visitShorthand = node => {
        if (!node || typeof node.type !== 'string') return;
        if (node.type === 'Property' && node.shorthand && node.key?.type === 'Identifier' && node.key.name === oldName) {
          const valueIdentifier = node.value?.type === 'Identifier' ? node.value : node.value?.type === 'AssignmentPattern' ? node.value.left : null;
          if (valueIdentifier?.type === 'Identifier' && replacements.has(valueIdentifier.start)) replacements.set(valueIdentifier.start, `${oldName}: ${newName}`);
        }
        for (const key of Object.keys(node)) {
          if (['start','end','loc','range'].includes(key)) continue;
          const value = node[key];
          if (Array.isArray(value)) { for (const child of value) if (child && typeof child.type === 'string') visitShorthand(child); }
          else if (value && typeof value.type === 'string') visitShorthand(value);
        }
      };
      visitShorthand(ast);
      const edits = [...replacements.keys()].sort((a,b) => b-a);
      let content = text;
      for (const start of edits) {
        const current = content.slice(start, start + oldName.length);
        if (current !== oldName) throw new Error('The source no longer matches the parsed identifier positions; refresh the project index and retry.');
        content = content.slice(0, start) + replacements.get(start) + content.slice(start + oldName.length);
      }
      try { parser.parse(content, { ecmaVersion: 'latest', sourceType: 'module', allowHashBang: true }); }
      catch (_) { parser.parse(content, { ecmaVersion: 'latest', sourceType: 'script', allowHashBang: true, allowReturnOutsideFunction: true }); }
      return { action, changed: true, content, edits: edits.length, symbol: { oldName, newName, declarationStart: binding.start, scope: binding.scope }, message: `Prepared a scope-aware rename in this file (${edits.length} identifier edits). This operation does not rename imports in other files; review the preview before applying.` };
    }

    function projectIndexExtract(text, language, path = '') {
      const imports = [], symbols = [];
      const addMatches = (regex, target, max=120) => { let match; while ((match=regex.exec(text)) && target.length<max) { const value=match.slice(1).filter(Boolean).at(-1)?.trim() || ''; if(value && !target.includes(value)) target.push(value); } };
      if (['javascript','typescript'].includes(language)) {
        const ext = String(path).toLowerCase().split('.').pop();
        if (language === 'javascript' && ['js','mjs','cjs'].includes(ext)) {
          const parsed = projectIndexExtractJavaScript(text, path);
          if (parsed.supported) return { ...parsed, imports: parsed.imports || [], symbols: parsed.symbols || [] };
        }
        if ((language === 'javascript' && ext === 'jsx') || (language === 'typescript' && ['ts','tsx','mts','cts'].includes(ext))) {
          const parsed = projectIndexExtractTypeScript(text, path);
          if (parsed.supported) return { ...parsed, imports: parsed.imports || [], symbols: parsed.symbols || [] };
        }
        addMatches(/(?:^|\n)\s*(?:import\s+(?:[^'"\n]*?\s+from\s+)?|export\s+[^'"\n]*?\s+from\s+|import\s*\()(['"])([^'"\n]+)\1/g, imports);
        addMatches(/\brequire\s*\(\s*(['"])([^'"]+)\1\s*\)/g, imports);
        addMatches(/(?:^|\n)\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?(?:function|class|interface|type|enum|const|let|var)\s+([A-Za-z_$][\w$]*)/g, symbols);
      }
      return { imports: imports.slice(0,120), symbols: symbols.slice(0,120), symbolDetails: [], identifierOccurrences: [], parseError: null };
    }
    async function loadProjectIndex() {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace first.');
      const value = await workspaceDbAction('readonly','get',undefined,projectIndexKey());
      return value && value.version===6 && value.contextId===workspaceContext.id ? value : null;
    }
    function projectIndexAnalyzeTypeScriptProgram(files, manifests = []) {
      const ts = globalThis.ts;
      if (!ts?.createProgram) return { supported: false, reason: 'TypeScript compiler API unavailable.', filesAnalyzed: 0, resolvedBindings: [], crossFileReferences: [] };
      const eligible = (files || []).filter(file => ['javascript','typescript'].includes(file.language) && typeof file.analysisSource === 'string');
      const selected = eligible.slice().sort((a,b) => a.path.localeCompare(b.path)).slice(0, 500);
      const totalBytes = selected.reduce((sum,file) => sum + file.analysisSource.length, 0);
      if (totalBytes > 12 * 1024 * 1024) {
        let bytes = 0;
        const bounded = [];
        for (const file of selected) { if (bytes + file.analysisSource.length > 12 * 1024 * 1024) break; bounded.push(file); bytes += file.analysisSource.length; }
        selected.splice(0, selected.length, ...bounded);
      }
      const canonical = value => String(value || '').replace(/\\/g,'/').replace(/^\/+/, '').replace(/^runmind\//i,'').replace(/\/{2,}/g,'/').toLowerCase();
      const sources = new Map(selected.map(file => [canonical(file.path), file.analysisSource]));
      const paths = new Map(selected.map(file => [canonical(file.path), file.path]));
      const manifestText = new Map((manifests || []).map(item => [canonical(item.path), item.text]));
      const virtual = value => `/runmind/${canonical(value)}`;
      const options = {
        allowJs: true, checkJs: false, noEmit: true, noLib: true, skipLibCheck: true,
        target: ts.ScriptTarget.ESNext, module: ts.ModuleKind.NodeNext, moduleResolution: ts.ModuleResolutionKind.NodeNext,
        jsx: ts.JsxEmit.Preserve, resolveJsonModule: true, allowImportingTsExtensions: true,
        strict: false, types: [], preserveSymlinks: true
      };
      const tsconfig = (manifests || []).filter(item => /(?:^|\/)tsconfig(?:\.[^/]+)?\.json$/i.test(item.path)).sort((a,b)=>a.path.split('/').length-b.path.split('/').length)[0];
      if (tsconfig) {
        try {
          const parsed = ts.parseConfigFileTextToJson(tsconfig.path, tsconfig.text);
          if (parsed.config?.compilerOptions) {
            const baseDirectory = tsconfig.path.split('/').slice(0,-1).join('/');
            const converted = ts.convertCompilerOptionsFromJson(parsed.config.compilerOptions, `/runmind/${baseDirectory}`);
            Object.assign(options, converted.options || {});
          }
        } catch (_) {}
      }
      Object.assign(options, { allowJs: true, noEmit: true, noLib: true, skipLibCheck: true, types: [], preserveSymlinks: true });
      const host = {
        getCurrentDirectory: () => '/runmind',
        getCanonicalFileName: value => canonical(value),
        useCaseSensitiveFileNames: () => false,
        getNewLine: () => '\n',
        getDefaultLibFileName: () => '/runmind/__no_default_lib__.d.ts',
        fileExists: value => sources.has(canonical(value)) || manifestText.has(canonical(value)),
        readFile: value => sources.get(canonical(value)) ?? manifestText.get(canonical(value)),
        directoryExists: value => {
          const prefix = canonical(value).replace(/\/$/,'');
          return [...sources.keys(),...manifestText.keys()].some(key => key.startsWith(prefix ? `${prefix}/` : ''));
        },
        realpath: value => value,
        getSourceFile: (fileName, languageVersion, onError) => {
          const content = sources.get(canonical(fileName));
          if (content === undefined) return undefined;
          const extension = canonical(fileName).split('.').pop();
          const kind = extension === 'tsx' ? ts.ScriptKind.TSX : extension === 'jsx' ? ts.ScriptKind.JSX : ['js','mjs','cjs'].includes(extension) ? ts.ScriptKind.JS : ts.ScriptKind.TS;
          return ts.createSourceFile(fileName, content, languageVersion, true, kind);
        },
        writeFile: () => {},
        readDirectory: () => [],
        getDirectories: (value) => {
          const prefix = canonical(value).replace(/\/$/,''); const result = new Set();
          for (const key of [...sources.keys(),...manifestText.keys()]) { const relative=prefix?key.slice(prefix.length+1):key; const index=relative.indexOf('/'); if(index>0)result.add(relative.slice(0,index)); }
          return [...result];
        }
      };
      const rootNames = selected.map(file => virtual(file.path));
      let program;
      try { program = ts.createProgram({ rootNames, options, host }); }
      catch (error) { return { supported: true, reason: String(error?.message || error).slice(0,300), filesAnalyzed: 0, resolvedBindings: [], crossFileReferences: [] }; }
      const checker = program.getTypeChecker();
      const resolvedBindings = [], crossFileReferences = [];
      const getTarget = node => {
        let symbol;
        try { symbol = checker.getSymbolAtLocation(node); if (symbol && (symbol.flags & ts.SymbolFlags.Alias)) symbol = checker.getAliasedSymbol(symbol); } catch (_) { return null; }
        const declaration = symbol?.declarations?.find(item => item.getSourceFile && paths.has(canonical(item.getSourceFile().fileName)));
        if (!declaration) return null;
        const targetPath = paths.get(canonical(declaration.getSourceFile().fileName));
        const nameNode = declaration.name && ts.isIdentifier(declaration.name) ? declaration.name : null;
        return { targetPath, declarationStart: nameNode ? nameNode.getStart(declaration.getSourceFile()) : declaration.getStart(declaration.getSourceFile()), targetName: nameNode?.text || symbol.name || '' };
      };
      const isNamePosition = (node,parent) => parent?.name===node || (ts.isPropertyAccessExpression(parent) && parent.name===node) || (ts.isPropertyAssignment(parent) && parent.name===node && !ts.isComputedPropertyName(parent.name));
      for (const file of selected) {
        const sourceFile = program.getSourceFile(virtual(file.path));
        if (!sourceFile) continue;
        const currentPath = file.path;
        const visit = (node,parent=null) => {
          if (ts.isImportSpecifier(node) || ts.isExportSpecifier(node)) {
            const parentDeclaration = ts.isImportSpecifier(node) ? node.parent?.parent?.parent : node.parent?.parent;
            const moduleSpecifier = parentDeclaration?.moduleSpecifier && ts.isStringLiteralLike(parentDeclaration.moduleSpecifier) ? parentDeclaration.moduleSpecifier.text : '';
            const target = getTarget(node.name);
            if (moduleSpecifier && target && target.targetPath !== currentPath) {
              const imported = (node.propertyName || node.name).text;
              resolvedBindings.push({ from: currentPath, to: target.targetPath, source: moduleSpecifier, local: node.name.text, imported, exported: target.targetName || imported, compiler: true, kind: ts.isImportSpecifier(node) ? 'import' : 're-export' });
            }
          }
          if (ts.isIdentifier(node) && !isNamePosition(node,parent)) {
            const target = getTarget(node);
            if (target && target.targetPath !== currentPath) {
              const access = parent && ts.isBinaryExpression(parent) && parent.left===node && ts.isAssignmentOperator(parent.operatorToken.kind) ? (parent.operatorToken.kind===ts.SyntaxKind.EqualsToken?'write':'read-write') : 'read';
              crossFileReferences.push({ path: currentPath, name: node.text, start: node.getStart(sourceFile), end: node.end, targetPath: target.targetPath, targetName: target.targetName, declarationStart: target.declarationStart, access, engine: 'typescript-checker' });
            }
          }
          ts.forEachChild(node, child => visit(child,node));
        };
        visit(sourceFile);
      }
      const uniqueBindings = new Map();
      for (const binding of resolvedBindings) uniqueBindings.set([binding.from,binding.to,binding.source,binding.local,binding.imported,binding.kind].join('|'),binding);
      return { supported: true, compilerVersion: ts.version, filesAnalyzed: selected.length, truncated: selected.length < eligible.length, resolvedBindings: [...uniqueBindings.values()].slice(0,10000), crossFileReferences: crossFileReferences.slice(0,20000), note: 'TypeScript compiler Program and checker resolve symbols across indexed JavaScript/JSX/TypeScript/TSX files. Analysis is bounded to 500 source files and 12 MB; external node_modules packages are resolved only when their source is represented in the indexed virtual workspace.' };
    }

    async function buildProjectIndex({ force=false, signal=null, onProgress=null }={}) {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace first.');
      const previous = force ? null : await loadProjectIndex();
      const oldFiles = new Map((previous?.files || []).map(file => [file.path,file]));
      const files = [], manifests = [], technologies = new Set(), visitedPaths = new Set();
      let entries=0, scanned=0, readBytes=0, reused=0, skipped=0, truncated=false, reason='';
      const stop = value => { truncated=true; if(!reason) reason=value; };
      const manifestNames = new Set(['package.json','dockerfile','makefile','package-lock.json','pnpm-lock.yaml','yarn.lock','bun.lock','bun.lockb']);
      async function visit(dir,prefix,depth) {
        if(signal?.aborted){stop('cancelled');return;}
        if(depth>PROJECT_INDEX_LIMITS.depth){stop('depth limit reached');return;}
        for await (const [name,handle] of dir.entries()) {
          if(truncated) return;
          if(signal?.aborted){stop('cancelled');return;}
          if(entries>=PROJECT_INDEX_LIMITS.entries){stop('directory-entry limit reached');return;}
          if(scanned>=PROJECT_INDEX_LIMITS.files){stop('file limit reached');return;}
          entries++;
          const path=prefix?`${prefix}/${name}`:name;
          if(handle.kind==='directory') { if(!PROJECT_INDEX_SKIP_DIRS.has(name.toLowerCase())) await visit(handle,path,depth+1); continue; }
          scanned++;
          visitedPaths.add(path);
          if(PROJECT_INDEX_SKIP_FILES.test(name)) { skipped++; continue; }
          const lower=name.toLowerCase(), ext=lower.match(/\.[^.]+$/)?.[0]||'';
          const isManifest=manifestNames.has(lower)||(lower.startsWith('tsconfig.')&&lower.endsWith('.json'));
          if(!isManifest&&!PROJECT_INDEX_TEXT_EXTENSIONS.has(ext)) { skipped++; continue; }
          try {
            const file=await handle.getFile();
            if(file.size>PROJECT_INDEX_LIMITS.fileBytes){skipped++;continue;}
            const prior=oldFiles.get(path);
            if(prior && prior.size===file.size && prior.lastModified===file.lastModified && typeof prior.text==='string' && ( !['javascript','typescript'].includes(prior.language) || typeof prior.analysisSource==='string') && (prior.language!=='javascript' || /^(?:acorn|typescript-compiler-api)/.test(String(prior.analysisEngine||'')))) {
              files.push(prior); reused++;
              if(isManifest) manifests.push({path,text:prior.manifestText||prior.text.slice(0,64000)});
              if(prior.language && prior.language!=='text') technologies.add(prior.language);
              continue;
            }
            if(readBytes+file.size>PROJECT_INDEX_LIMITS.totalBytes){stop('aggregate text budget reached');return;}
            const source=await file.text(); readBytes+=file.size;
            const text=redactProjectIndexSecrets(source);
            const language=projectIndexLanguage(path);
            if ((language==='typescript' || ext==='.jsx') && !globalThis.ts?.createSourceFile) { try { await projectIndexEnsureTypeScriptCompiler(); } catch (_) { /* Heuristic fallback remains available if the offline compiler bundle cannot load. */ } }
            const extracted=projectIndexExtract(text,language,path);
            const record={path,size:file.size,lastModified:file.lastModified,language,analysisEngine:extracted.engine||'regex-fallback',parseError:extracted.parseError||null,imports:extracted.imports,symbols:extracted.symbols,symbolDetails:extracted.symbolDetails||[],identifierOccurrences:extracted.identifierOccurrences||[],references:extracted.references||[],calls:extracted.calls||[],exports:extracted.exports||[],importBindings:extracted.importBindings||[],manifestText:isManifest?text.slice(0,64000):null,analysisSource:['javascript','typescript'].includes(language)?text.slice(0,256*1024):null,text:text.slice(0,Math.min(text.length,160000)).toLowerCase()};
            files.push(record);
            if(isManifest) manifests.push({path,text:text.slice(0,64000)});
            if(language!=='text') technologies.add(language);
          } catch (_) { skipped++; }
          if(files.length%100===0) onProgress?.({files:files.length,scanned,readBytes});
        }
      }
      await visit(agentDirectoryHandle,'',0);
      if(signal?.aborted) throw new DOMException('Project indexing cancelled.','AbortError');
      if(files.some(file=>['javascript','typescript'].includes(file.language)) && !globalThis.ts?.createProgram) { try { await projectIndexEnsureTypeScriptCompiler(); } catch (_) { /* Acorn remains available; compiler-backed cross-file analysis is omitted. */ } }
      if(truncated && previous) for(const [path,record] of oldFiles) { if(files.length>=PROJECT_INDEX_LIMITS.files) break; if(!visitedPaths.has(path)) files.push(record); }
      const removed = truncated ? 0 : [...oldFiles.keys()].filter(path => !visitedPaths.has(path)).length;
      const dependencies=projectIndexDependencies(manifests);
      const frameworks=projectIndexFrameworks(dependencies);
      const toolchains=projectIndexToolchains(files,manifests,[...visitedPaths]);
      const importGraph=projectIndexLocalImportGraph(files,manifests);
      const hasCompilerSources=files.some(file=>['javascript','typescript'].includes(file.language));
      const canReuseCompilerAnalysis=!force&&previous?.importGraph?.compilerAnalysis&&reused===files.length&&removed===0&&(previous.importGraph.compilerAnalysis.compilerVersion||!hasCompilerSources);
      const compilerAnalysis=canReuseCompilerAnalysis
        ? { supported:true, compilerVersion:previous.importGraph.compilerAnalysis.compilerVersion, filesAnalyzed:previous.importGraph.compilerAnalysis.filesAnalyzed||0, truncated:!!previous.importGraph.compilerAnalysis.truncated, resolvedBindings:(previous.importGraph.resolvedBindings||[]).filter(item=>item.compiler===true), crossFileReferences:files.flatMap(file=>file.compilerReferences||[]), note:previous.importGraph.compilerAnalysis.note||'' }
        : projectIndexAnalyzeTypeScriptProgram(files,manifests);
      const compilerReferencesByPath=new Map();
      for(const reference of compilerAnalysis.crossFileReferences||[]){const list=compilerReferencesByPath.get(reference.path)||[];list.push(reference);compilerReferencesByPath.set(reference.path,list);}
      for(const file of files)file.compilerReferences=compilerReferencesByPath.get(file.path)||[];
      const resolvedKeys=new Set((importGraph.resolvedBindings||[]).map(item=>[item.from,item.to,item.source,item.local,item.imported].join('|')));
      for(const binding of compilerAnalysis.resolvedBindings||[]){const key=[binding.from,binding.to,binding.source,binding.local,binding.imported].join('|');if(!resolvedKeys.has(key)){importGraph.resolvedBindings.push(binding);resolvedKeys.add(key);}}
      importGraph.compilerAnalysis={supported:compilerAnalysis.supported!==false,compilerVersion:compilerAnalysis.compilerVersion||null,filesAnalyzed:compilerAnalysis.filesAnalyzed||0,truncated:!!compilerAnalysis.truncated,crossFileReferences:compilerAnalysis.crossFileReferences?.length||0,note:compilerAnalysis.note||compilerAnalysis.reason||''};
      const index={version:6,contextId:workspaceContext.id,rootName:agentDirectoryHandle.name||workspaceContext.folderName||'workspace',createdAt:previous?.createdAt||new Date().toISOString(),updatedAt:new Date().toISOString(),files,manifests:manifests.map(({path,text})=>({path,text})),technologies:[...technologies].sort(),frameworks,dependencies,toolchains,importGraph,stats:{files:files.length,scanned,entries,readBytes,reused,removed,skipped,truncated,reason}};
      await workspaceDbAction('readwrite','put',index,projectIndexKey());
      projectIndexStatus?.(index);
      return {root:index.rootName,files:index.stats.files,scanned,entries,readBytes,reused,removed,skipped,truncated,reason,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies.slice(0,100),toolchains:index.toolchains,importGraphSummary:{localImportEdges:index.importGraph?.localImportEdges?.length||0,resolvedBindings:index.importGraph?.resolvedBindings?.length||0,unresolvedBindings:index.importGraph?.unresolvedBindings?.length||0,circularDependencies:index.importGraph?.circularDependencies?.length||0,externalImports:index.importGraph?.externalImports?.length||0,mostImportedFiles:index.importGraph?.filesWithIncomingImports||[],note:index.importGraph?.note},updatedAt:index.updatedAt};
    }
    async function searchProjectIndex(query,{limit=30,kind='all'}={}) {
      const index=await loadProjectIndex();
      if(!index) throw new Error('No project index exists yet. Build it from Agent settings or call project_index first.');
      const terms=String(query||'').slice(0,400).toLowerCase().split(/[^a-z0-9_.$-]+/).filter(Boolean).slice(0,12);
      if(!terms.length) throw new Error('Provide at least one search term.');
      const hits=[];
      for(const file of index.files){
        let score=0; const matches=[];
        if(kind!=='symbols') for(const term of terms){if(file.path.toLowerCase().includes(term)){score+=5;matches.push(`path:${term}`);} if(file.text?.includes(term)){score+=2;matches.push(`text:${term}`);}}
        if(kind==='all'||kind==='symbols') for(const symbol of file.symbols||[]) for(const term of terms) if(symbol.toLowerCase().includes(term)){score+=8;matches.push(`symbol:${symbol}`);}
        if(kind==='all') for(const imported of file.imports||[]) for(const term of terms) if(imported.toLowerCase().includes(term)){score+=3;matches.push(`import:${imported}`);}
        if(score) hits.push({path:file.path,language:file.language,score,matches:[...new Set(matches)].slice(0,12),symbols:(file.symbols||[]).filter(symbol=>terms.some(term=>symbol.toLowerCase().includes(term))).slice(0,20),symbolDetails:(file.symbolDetails||[]).filter(symbol=>terms.some(term=>symbol.name.toLowerCase().includes(term))).slice(0,20),references:(file.references||[]).filter(ref=>terms.includes(ref.name.toLowerCase())).slice(0,30),exports:(file.exports||[]).slice(0,30),analysisEngine:file.analysisEngine||'regex-fallback',parseError:file.parseError||null,imports:(file.imports||[]).slice(0,20)});
      }
      hits.sort((a,b)=>b.score-a.score||a.path.localeCompare(b.path));
      return {query,kind,indexUpdatedAt:index.updatedAt,totalMatches:hits.length,results:hits.slice(0,Math.max(1,Math.min(100,limit))),note:'The persistent index is refreshed automatically when a workspace is selected and periodically while the chat is open; manual refresh remains available.'};
    }
    function projectIndexCrossFileReferences(files, graph, { path = '', name = '', limit = 200 } = {}) {
      const byPath = new Map((files || []).map(file => [file.path, file]));
      const results = [];
      for (const binding of graph?.resolvedBindings || []) {
        if (path && binding.from !== path && binding.to !== path) continue;
        if (name && binding.local !== name && binding.imported !== name && binding.exported !== name) continue;
        const importer = byPath.get(binding.from);
        const target = byPath.get(binding.to);
        if (!importer || !target) continue;
        const declaration = (importer.symbolDetails || []).find(item => item.name === binding.local && item.kind === 'import');
        if (!declaration) continue;
        const references = (importer.references || []).filter(item => item.declaration === declaration.start).map(item => ({ start: item.start, end: item.end, access: item.access, read: item.read, write: item.write }));
        results.push({ from: binding.from, to: binding.to, local: binding.local, imported: binding.imported, exported: binding.exported, importDeclarationStart: declaration.start, referenceCount: references.length, references });
      }
      const maximum = Math.max(1, Math.min(1000, Number(limit) || 200));
      return { results: results.slice(0, maximum), truncated: results.length > maximum, note: 'Cross-file reference edges are derived from statically resolved import bindings and lexical references in the importing module. Dynamic runtime references and unresolved imports are not included.' };
    }

    async function getProjectSymbolGraph({path='',name='',limit=200}={}) {
      const index=await loadProjectIndex();
      if(!index)throw new Error('Build the project index before requesting the symbol graph.');
      const files=index.files.filter(file=>!path||file.path===path);
      const declarations=files.flatMap(file=>(file.symbolDetails||[]).filter(item=>!name||item.name===name).map(item=>({path:file.path,...item})));
      const references=files.flatMap(file=>(file.references||[]).filter(item=>!name||item.name===name).map(item=>({path:file.path,...item})));
      const compilerReferences=files.flatMap(file=>(file.compilerReferences||[]).filter(item=>!name||item.name===name||item.targetName===name).map(item=>({path:file.path,...item})));
      const calls=files.flatMap(file=>(file.calls||[]).filter(item=>!name||item.callee===name||item.caller===name).map(item=>({path:file.path,...item})));
      const graph=index.importGraph||{};
      const edges=(graph.localImportEdges||[]).filter(item=>!path||item.from===path||item.to===path);
      const circularDependencies=(graph.circularDependencies||[]).filter(cycle=>!path||cycle.includes(path));
      const externalImports=(graph.externalImports||[]).filter(item=>!path||item.from===path);
      const resolved=(graph.resolvedBindings||[]).filter(item=>(!path||item.from===path||item.to===path)&&(!name||item.local===name||item.imported===name));
      const unresolved=(graph.unresolvedBindings||[]).filter(item=>(!path||item.from===path||item.to===path)&&(!name||item.local===name||item.imported===name));
      const crossFileReferences=projectIndexCrossFileReferences(index.files,graph,{path,name,limit});
      const maximum=Math.max(1,Math.min(1000,Number(limit)||200));
      return {indexUpdatedAt:index.updatedAt,filters:{path:path||null,name:name||null},compilerAnalysis:graph.compilerAnalysis||null,compilerReferences:compilerReferences.slice(0,maximum),declarations:declarations.slice(0,maximum),references:references.slice(0,maximum),calls:calls.slice(0,maximum),moduleEdges:edges.slice(0,maximum),circularDependencies:circularDependencies.slice(0,100),externalImports:externalImports.slice(0,maximum),resolvedImports:resolved.slice(0,maximum),unresolvedImports:unresolved.slice(0,maximum),crossFileReferences:crossFileReferences.results,crossFileReferencesTruncated:crossFileReferences.truncated,truncated:crossFileReferences.truncated||compilerReferences.length>maximum||declarations.length>maximum||references.length>maximum||calls.length>maximum||edges.length>maximum||circularDependencies.length>100||externalImports.length>maximum||resolved.length>maximum||unresolved.length>maximum,scope:'Acorn JavaScript lexical bindings plus TypeScript compiler-API syntax trees and a bounded project-wide TypeScript checker for JavaScript/JSX/TypeScript/TSX symbol references. Node module runtime resolution is separately available through node_resolve_modules.'};
    }
    async function getProjectIndexOverview() {
      const index=await loadProjectIndex();
      if(!index) return {indexed:false,message:'No project index has been built.'};
      return {indexed:true,root:index.rootName,updatedAt:index.updatedAt,stats:index.stats,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies,toolchains:index.toolchains||null,analysis:{acornFiles:index.files.filter(item=>String(item.analysisEngine||'').startsWith('acorn')).length,typescriptCompilerFiles:index.files.filter(item=>String(item.analysisEngine||'').startsWith('typescript-compiler-api')).length,typescriptProgramFiles:index.importGraph?.compilerAnalysis?.filesAnalyzed||0,typescriptCrossFileReferences:index.importGraph?.compilerAnalysis?.crossFileReferences||0,callSites:index.files.reduce((sum,item)=>sum+(item.calls?.length||0),0),parseErrors:index.files.filter(item=>item.parseError).length,regexFallbackFiles:index.files.filter(item=>item.analysisEngine==='regex-fallback').length,svgFiles:index.files.filter(item=>item.language==='svg').length},compilerAnalysis:index.importGraph?.compilerAnalysis||null,importGraph:index.importGraph?{localImportEdges:index.importGraph.localImportEdges.length,resolvedBindings:index.importGraph.resolvedBindings?.length||0,unresolvedBindings:index.importGraph.unresolvedBindings?.length||0,circularDependencies:index.importGraph.circularDependencies?.length||0,externalImports:index.importGraph.externalImports?.length||0,filesWithIncomingImports:index.importGraph.filesWithIncomingImports,note:index.importGraph.note}:null,manifests:index.manifests.map(item=>item.path),topLevel:index.files.filter(item=>!item.path.includes('/')).map(item=>item.path).slice(0,100)};
    }

    function projectIndexNormalizePath(value) {
      return String(value || '').replace(/\\/g, '/').replace(/^\.\//, '').replace(/\/{2,}/g, '/').replace(/\/$/, '');
    }
    function projectIndexPackageData(index) {
      const records = [];
      for (const item of index?.manifests || []) {
        if (item.path.split('/').pop().toLowerCase() !== 'package.json') continue;
        try { records.push({ path: item.path, data: JSON.parse(item.text) }); } catch (_) { /* Diagnostics reports malformed package manifests. */ }
      }
      return records;
    }
    function projectIndexResolveLocalAsset(ownerPath, reference) {
      const value = String(reference || '').trim().split(/[?#]/, 1)[0].replace(/\\/g, '/');
      if (!value || /^(?:https?:|data:|blob:|\/\/|#|javascript:)/i.test(value)) return null;
      const segments = value.startsWith('/') ? [] : String(ownerPath || '').split('/').slice(0, -1);
      for (const segment of value.replace(/^\/+/, '').split('/')) {
        if (!segment || segment === '.') continue;
        if (segment === '..') { if (!segments.length) return null; segments.pop(); continue; }
        segments.push(segment);
      }
      return segments.join('/');
    }

    function projectIndexBuildUnderstanding(index) {
      if (!index) return { available: false, message: 'Build the project index before requesting Project Understanding.' };
      const files = Array.isArray(index.files) ? index.files : [];
      const edges = index.importGraph?.localImportEdges || [];
      const incoming = new Map(), outgoing = new Map();
      for (const edge of edges) {
        incoming.set(edge.to, (incoming.get(edge.to) || 0) + 1);
        outgoing.set(edge.from, (outgoing.get(edge.from) || 0) + 1);
      }
      const topLevel = new Map();
      for (const file of files) {
        const segment = file.path.includes('/') ? file.path.split('/')[0] : '(root)';
        const row = topLevel.get(segment) || { directory: segment, files: 0, javascript: 0, typescript: 0, html: 0, css: 0, svg: 0 };
        row.files++;
        if (Object.hasOwn(row, file.language)) row[file.language]++;
        topLevel.set(segment, row);
      }
      const packageRecords = projectIndexPackageData(index);
      const entryPoints = [];
      const assetReferences = [];
      for (const { path, data } of packageRecords) {
        const dir = path.split('/').slice(0, -1).join('/');
        for (const field of ['main', 'module', 'browser', 'types', 'typings']) if (typeof data[field] === 'string') entryPoints.push({ path: [dir, data[field]].filter(Boolean).join('/'), source: `${path}#${field}`, confidence: 'declared' });
        if (typeof data.source === 'string') entryPoints.push({ path: [dir, data.source].filter(Boolean).join('/'), source: `${path}#source`, confidence: 'declared' });
        const collectExportTargets = value => {
          if (typeof value === 'string') return value.startsWith('./') ? [value] : [];
          if (Array.isArray(value)) return value.flatMap(collectExportTargets);
          if (value && typeof value === 'object') return Object.values(value).flatMap(collectExportTargets);
          return [];
        };
        for (const target of collectExportTargets(data.exports).slice(0, 50)) entryPoints.push({ path: [dir, target].filter(Boolean).join('/'), source: `${path}#exports`, confidence: 'conditional-export-candidate' });
        for (const [script, command] of Object.entries(data.scripts || {})) if (['start', 'dev', 'serve'].includes(script)) entryPoints.push({ path, source: `${path}#scripts.${script}`, command: String(command).slice(0, 180), confidence: 'script-declared' });
      }
      for (const file of files) {
        if (/^(?:index|main|app)\.(?:html?)$/i.test(file.path.split('/').pop())) entryPoints.push({ path: file.path, source: 'HTML entry candidate', confidence: 'candidate' });
        if (file.language === 'html' && typeof file.text === 'string') {
          const scriptPattern = /<script\b[^>]*\bsrc\s*=\s*[\"']([^\"']+)[\"'][^>]*>/gi;
          const stylePattern = /<link\b[^>]*\brel\s*=\s*[\"']stylesheet[\"'][^>]*\bhref\s*=\s*[\"']([^\"']+)[\"'][^>]*>/gi;
          for (const [pattern, kind] of [[scriptPattern, 'HTML script reference'], [stylePattern, 'HTML stylesheet reference']]) {
            let match; while ((match = pattern.exec(file.text)) && entryPoints.length < 200) if (!/^(?:https?:|data:|\/\/|#)/i.test(match[1])) entryPoints.push({ path: match[1], source: `${file.path} · ${kind}`, confidence: 'heuristic-candidate' });
          }
        }
      }
      for (const file of files) {
        if (typeof file.text !== 'string') continue;
        if (file.language === 'html') {
          const markupAssetPattern = /(?:\bsrc|\bhref|\bdata)\s*=\s*([\"'])([^\"']+\.svg(?:[?#][^\"']*)?)\1/gi;
          let assetMatch;
          while ((assetMatch = markupAssetPattern.exec(file.text)) && assetReferences.length < 500) {
            const target = projectIndexResolveLocalAsset(file.path, assetMatch[2]);
            if (target) assetReferences.push({ from: file.path, to: target, kind: 'HTML SVG asset reference', targetIndexed: files.some(item => item.path === target) });
          }
        } else if (file.language === 'css') {
          const cssAssetPattern = /url\(\s*([\"']?)([^\"')]+\.svg(?:[?#][^\"')]*)?)\1\s*\)/gi;
          let assetMatch;
          while ((assetMatch = cssAssetPattern.exec(file.text)) && assetReferences.length < 500) {
            const target = projectIndexResolveLocalAsset(file.path, assetMatch[2]);
            if (target) assetReferences.push({ from: file.path, to: target, kind: 'CSS SVG asset reference', targetIndexed: files.some(item => item.path === target) });
          }
        }
      }
      for (const file of files) {
        if (file.language !== 'svg' || typeof file.text !== 'string') continue;
        const referencePattern = /(?:\bxlink:href|\bhref)\s*=\s*([\"'])(.*?)\1/gi;
        let match;
        while ((match = referencePattern.exec(file.text)) && assetReferences.length < 500) {
          const reference = String(match[2] || '').trim();
          const target = projectIndexResolveLocalAsset(file.path, reference);
          if (!target) continue;
          assetReferences.push({ from: file.path, to: target, kind: 'SVG local href reference', targetIndexed: files.some(item => item.path === target) });
        }
      }
      const uniqueEntries = [...new Map(entryPoints.map(item => [`${item.path}|${item.source}`, item])).values()].slice(0, 100);
      const modules = [...topLevel.values()].sort((a, b) => b.files - a.files || a.directory.localeCompare(b.directory)).slice(0, 100);
      const hotspots = files.map(file => ({ path: file.path, incomingImports: incoming.get(file.path) || 0, outgoingImports: outgoing.get(file.path) || 0, symbols: (file.symbols || []).length, calls: (file.calls || []).length }))
        .filter(item => item.incomingImports >= 2 || item.outgoingImports >= 8).sort((a, b) => (b.incomingImports + b.outgoingImports) - (a.incomingImports + a.outgoingImports)).slice(0, 40);
      return {
        available: true, root: index.rootName, indexedAt: index.updatedAt,
        scope: 'Language-aware code analysis is limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS; SVG is indexed as a first-class vector asset and its local href/xlink:href references are inspected. Other source files are generic text only. HTML reference candidates come from normalized search text and must be checked in the original HTML before use.',
        modules, entryPoints: uniqueEntries, assetReferences: assetReferences.slice(0, 200),
        dependencyDirection: { localImportEdges: edges.length, resolvedBindings: index.importGraph?.resolvedBindings?.length || 0, unresolvedBindings: index.importGraph?.unresolvedBindings?.length || 0, circularDependencies: index.importGraph?.circularDependencies || [], note: index.importGraph?.note || '' },
        highCouplingModules: hotspots,
        boundaries: modules.map(item => item.directory),
        analysisCoverage: { indexedFiles: files.length, javascriptFiles: files.filter(item => item.language === 'javascript').length, typescriptFiles: files.filter(item => item.language === 'typescript').length, htmlFiles: files.filter(item => item.language === 'html').length, cssFiles: files.filter(item => item.language === 'css').length, svgFiles: files.filter(item => item.language === 'svg').length, parseErrors: files.filter(item => item.parseError).map(item => ({ path: item.path, ...item.parseError })).slice(0, 100), indexTruncated: !!index.stats?.truncated }
      };
    }
    function projectIndexBuildDiagnostics(index) {
      if (!index) return { available: false, message: 'Build the project index before running Project Diagnostics.' };
      const checks = [], scripts = {}, testFiles = (index.files || []).filter(file => ['javascript', 'typescript'].includes(file.language) && /(?:^|\/)(?:test|tests|spec|specs)(?:\/|$)|\.(?:test|spec)\.[^/]+$/i.test(file.path)).map(file => file.path).slice(0, 200);
      const add = (id, severity, message, evidence = []) => checks.push({ id, severity, message, evidence: evidence.slice(0, 20) });
      const packageRecords = [];
      for (const item of index.manifests || []) {
        if (item.path.toLowerCase().endsWith('package.json')) {
          try {
            const data = JSON.parse(item.text);
            if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('package.json must contain a JSON object.');
            packageRecords.push({ path: item.path, data });
            if (data.scripts !== undefined && (!data.scripts || typeof data.scripts !== 'object' || Array.isArray(data.scripts))) add('invalid-package-scripts', 'error', 'package.json scripts must be an object of string commands.', [item.path]);
            if (data.scripts && typeof data.scripts === 'object' && !Array.isArray(data.scripts)) for (const [key, value] of Object.entries(data.scripts).slice(0, 60)) {
              if (typeof value !== 'string') add('invalid-package-script', 'error', `package.json script '${key}' must be a string.`, [item.path]);
              if (item.path === 'package.json') scripts[key] = String(value).slice(0, 300);
            }
            for (const field of ['dependencies', 'devDependencies', 'peerDependencies', 'optionalDependencies']) if (data[field] !== undefined && (!data[field] || typeof data[field] !== 'object' || Array.isArray(data[field]))) add('invalid-package-dependencies', 'error', `package.json ${field} must be an object.`, [item.path]);
            if (data.engines?.node && typeof data.engines.node === 'string') add('node-engine-declared', 'info', `Node.js requirement declared: ${data.engines.node}`, [item.path]);
            if (data.packageManager && !/^(?:npm|pnpm|yarn|bun)@/i.test(data.packageManager)) add('unsupported-package-manager-declaration', 'warning', `Unrecognized packageManager declaration '${String(data.packageManager).slice(0, 100)}'.`, [item.path]);
          } catch (error) { add('invalid-package-json', 'error', `Invalid package.json: ${String(error.message || error).slice(0, 240)}`, [item.path]); }
        } else if (item.path.toLowerCase().endsWith('.json')) {
          try { JSON.parse(item.text); } catch (error) { add('invalid-json-manifest', 'error', `Invalid JSON manifest: ${String(error.message || error).slice(0, 240)}`, [item.path]); }
        }
      }
      const manager = index.toolchains?.packageManager || null;
      const lockfiles = index.toolchains?.lockfiles || [];
      const rootPackage = packageRecords.find(item => item.path === 'package.json')?.data || null;
      const packageLock = (index.manifests || []).find(item => item.path.toLowerCase() === 'package-lock.json');
      if (packageLock && rootPackage) {
        try {
          const lock = JSON.parse(packageLock.text);
          const lockRoot = lock.packages?.[''] || lock;
          if (lockRoot.name && rootPackage.name && lockRoot.name !== rootPackage.name) add('package-lock-name-mismatch', 'error', 'package-lock.json root package name does not match package.json.', ['package.json', 'package-lock.json']);
          if (lockRoot.version && rootPackage.version && lockRoot.version !== rootPackage.version) add('package-lock-version-mismatch', 'error', 'package-lock.json root package version does not match package.json.', ['package.json', 'package-lock.json']);
        } catch (_) { /* The invalid JSON manifest check above reports parse failures. */ }
      }
      const lockManagers = [...new Set(lockfiles.map(item => item.manager))];
      if (lockManagers.length > 1) add('multiple-lockfile-managers', 'warning', 'Multiple package-manager lockfiles exist; deterministic installs may depend on which manager is used.', lockfiles.map(item => item.file));
      if (index.toolchains?.conflictingPackageManagerSignals) add('package-manager-conflict', 'error', 'package.json packageManager declaration conflicts with one or more detected lockfiles.', lockfiles.map(item => item.file));
      if (packageRecords.length && !manager) add('package-manager-ambiguous', lockManagers.length > 1 ? 'warning' : 'warning', 'A package.json exists, but no single package manager can be selected confidently.', packageRecords.map(item => item.path));
      if (!lockfiles.length && packageRecords.length) add('lockfile-missing', 'warning', 'No npm, pnpm, Yarn or Bun lockfile was found; reproducible dependency installation is not established by the index.');
      if (!Object.keys(scripts).length && packageRecords.length) add('package-scripts-missing', 'warning', 'The root package manifest declares no scripts.');
      for (const required of ['build', 'test']) if (Object.hasOwn(scripts, required)) add(`script-${required}`, 'info', `A '${required}' script is declared.`, ['package.json']);
      if (!testFiles.length) add('tests-not-discovered', 'warning', 'No conventional JavaScript/TypeScript test files were discovered. Tests may use a non-standard layout or be absent.');
      if (testFiles.length) add('tests-discovered', 'info', `${testFiles.length} test files were discovered.`, testFiles.slice(0, 20));
      const parseErrors = (index.files || []).filter(file => file.parseError).map(file => file.path);
      if (parseErrors.length) add('source-parse-errors', 'warning', `${parseErrors.length} supported-language source files could not be parsed.`, parseErrors);
      const unresolved = index.importGraph?.unresolvedBindings || [];
      if (unresolved.length) add('unresolved-imports', 'warning', `${unresolved.length} local/package import bindings remain unresolved by static analysis.`, unresolved.slice(0, 20).map(item => `${item.from}: ${item.source}`));
      const cycles = index.importGraph?.circularDependencies || [];
      if (cycles.length) add('circular-dependencies', 'warning', `${cycles.length} circular dependency groups were detected.`, cycles.slice(0, 20).map(item => item.join(' → ')));
      if (index.stats?.truncated) add('index-truncated', 'warning', `Project index is partial: ${index.stats.reason || 'scan limit reached'}.`);
      const configs = (index.files || []).map(file => file.path).filter(path => /(?:^|\/)(?:tsconfig(?:\.[^/]+)?\.json|eslint\.config\.[^/]+|\.eslintrc(?:\.[^/]+)?|biome\.jsonc?|stylelint\.config\.[^/]+|playwright\.config\.[^/]+|vite\.config\.[^/]+|webpack\.config\.[^/]+)$/i.test(path));
      if (configs.length) add('tooling-configurations', 'info', `${configs.length} JavaScript/TypeScript tooling configuration files were found.`, configs);
      const hasTypeScript = (index.files || []).some(file => file.language === 'typescript');
      if (hasTypeScript && !configs.some(path => /(?:^|\/)tsconfig(?:\.[^/]+)?\.json$/i.test(path))) add('typescript-config-missing', 'warning', 'TypeScript source files exist, but no tsconfig.json was discovered.');
      const errorCount = checks.filter(item => item.severity === 'error').length;
      const warningCount = checks.filter(item => item.severity === 'warning').length;
      return { available: true, root: index.rootName, indexedAt: index.updatedAt, status: errorCount ? 'issues-found' : warningCount ? 'attention' : 'healthy', summary: { errors: errorCount, warnings: warningCount, information: checks.length - errorCount - warningCount }, checks, packageManager: manager, lockfiles, packageScripts: scripts, testFilesFound: testFiles.length, testFiles, toolingConfigurations: configs, execution: { performed: false, environmentProbes: false, build: false, tests: false, dependencyAudit: false }, limitations: ['Static checks use indexed manifests and source metadata only.', 'Installed Node.js/package-manager versions and actual build/test/audit results require a separate approved native execution.', 'Only JavaScript, TypeScript, JSX/TSX, HTML and CSS receive language-aware project analysis.'] };
    }
    function projectIndexAnalyzeTestImpact(index, changedPaths = []) {
      if (!index) return { available: false, message: 'Build the project index before analyzing test impact.' };
      const changed = [...new Set((Array.isArray(changedPaths) ? changedPaths : [changedPaths]).map(projectIndexNormalizePath).filter(Boolean))].slice(0, 100);
      if (!changed.length) throw new Error('Provide one or more workspace-relative changed_paths. This analysis does not infer a Git diff.');
      const filesByPath = new Map((index.files || []).map(file => [file.path, file]));
      const existing = changed.filter(path => filesByPath.has(path));
      const unknown = changed.filter(path => !filesByPath.has(path));
      const analyzed = existing.filter(path => ['javascript', 'typescript'].includes(filesByPath.get(path)?.language));
      const unanalysed = existing.filter(path => !['javascript', 'typescript'].includes(filesByPath.get(path)?.language));
      const edges = index.importGraph?.localImportEdges || [];
      const reverse = new Map();
      for (const edge of edges) { const list = reverse.get(edge.to) || []; list.push(edge.from); reverse.set(edge.to, list); }
      const impacted = new Set(analyzed), queue = [...analyzed];
      while (queue.length && impacted.size < 2000) {
        const target = queue.shift();
        for (const importer of reverse.get(target) || []) if (!impacted.has(importer)) { impacted.add(importer); queue.push(importer); }
      }
      const testPattern = /(?:^|\/)(?:test|tests|spec|specs)(?:\/|$)|\.(?:test|spec)\.[^/]+$/i;
      const testFiles = (index.files || []).filter(file => ['javascript', 'typescript'].includes(file.language) && testPattern.test(file.path));
      const directTests = [], transitiveTests = [], nameCandidates = [];
      for (const test of testFiles) {
        if (existing.includes(test.path)) { directTests.push({ path: test.path, reason: 'The changed file is itself a discovered test.', confidence: 'direct' }); continue; }
        const directEdges = edges.filter(edge => edge.from === test.path && analyzed.includes(edge.to));
        if (directEdges.length) { directTests.push({ path: test.path, reason: `Directly imports changed module(s): ${directEdges.map(edge => edge.to).join(', ')}`, confidence: 'high' }); continue; }
        if (impacted.has(test.path)) { transitiveTests.push({ path: test.path, reason: 'Imports a module that transitively depends on a changed file.', confidence: 'medium' }); continue; }
        const testBase = test.path.split('/').pop().replace(/\.(?:test|spec)\.[^.]+$/i, '').toLowerCase();
        const changedBases = analyzed.map(path => path.split('/').pop().replace(/\.[^.]+$/, '').toLowerCase());
        if (changedBases.some(base => base && (testBase === base || testBase.includes(base) || base.includes(testBase)))) nameCandidates.push({ path: test.path, reason: 'Filename similarity only; inspect before running.', confidence: 'low' });
      }
      const selected = [...directTests, ...transitiveTests, ...nameCandidates].slice(0, 200);
      return { available: true, changedPaths: changed, matchedChangedPaths: existing, analyzedChangedPaths: analyzed, unanalysedChangedPaths: unanalysed, unknownChangedPaths: unknown, impactedModules: [...impacted].slice(0, 500), impactedModuleCount: impacted.size, tests: selected, summary: { analyzedChanges: analyzed.length, unanalysedChanges: unanalysed.length, unknownChanges: unknown.length, highConfidence: directTests.length, mediumConfidence: transitiveTests.length, lowConfidence: nameCandidates.length, recommendedTests: selected.length }, executionPerformed: false, note: 'Impact is derived only for JavaScript/TypeScript changed files from the indexed static import graph. Other indexed file types are reported as unanalysed. Dynamic imports, runtime-generated dependencies, test discovery conventions outside the indexed patterns, and external test harness behavior may be missed. No tests were executed.' };
    }
    function projectIndexAnalyzeChangeImpact(index, changedPaths = [], changedSymbols = []) {
      if (!index) return { available: false, message: 'Build the project index before exploring change impact.' };
      const normalize = value => projectIndexNormalizePath(String(value || '').trim());
      const changed = [...new Set((Array.isArray(changedPaths) ? changedPaths : [changedPaths]).map(normalize).filter(Boolean))].slice(0, 100);
      if (!changed.length) throw new Error('Provide one or more workspace-relative changed_paths. Git changes are not inferred.');
      const files = Array.isArray(index.files) ? index.files : [];
      const filesByPath = new Map(files.map(file => [file.path, file]));
      const matched = changed.filter(path => filesByPath.has(path));
      const unknown = changed.filter(path => !filesByPath.has(path));
      const supported = matched.filter(path => ['javascript','typescript'].includes(filesByPath.get(path)?.language));
      const unanalysed = matched.filter(path => !['javascript','typescript','html','css','svg'].includes(filesByPath.get(path)?.language));
      const graph = index.importGraph || {};
      const edges = Array.isArray(graph.localImportEdges) ? graph.localImportEdges : [];
      const reverse = new Map();
      for (const edge of edges) { const rows = reverse.get(edge.to) || []; rows.push(edge.from); reverse.set(edge.to, rows); }
      const direct = new Set(), transitive = new Set(), distance = new Map();
      const queue = matched.filter(path => ['javascript','typescript'].includes(filesByPath.get(path)?.language)).map(path => ({ path, depth: 0 }));
      for (const row of queue) distance.set(row.path, 0);
      while (queue.length && distance.size < 3000) {
        const current = queue.shift();
        for (const importer of reverse.get(current.path) || []) {
          if (distance.has(importer)) continue;
          distance.set(importer, current.depth + 1);
          if (current.depth === 0) direct.add(importer); else transitive.add(importer);
          queue.push({ path: importer, depth: current.depth + 1 });
        }
      }
      const understanding = projectIndexBuildUnderstanding(index);
      const changedSet = new Set(matched);
      const assetReferences = Array.isArray(understanding.assetReferences) ? understanding.assetReferences : [];
      const assetConsumers = assetReferences.filter(item => changedSet.has(item.to)).map(item => ({ path: item.from, changedAsset: item.to, kind: item.kind, indexed: !!item.targetIndexed }));
      const requestedSymbols = (Array.isArray(changedSymbols) ? changedSymbols : []).slice(0, 100).map(item => ({ path: normalize(item?.path), name: String(item?.name || '').trim().slice(0, 160) })).filter(item => item.path && item.name && matched.includes(item.path));
      const resolvedBindings = Array.isArray(graph.resolvedBindings) ? graph.resolvedBindings : [];
      const symbolConsumers = [];
      for (const item of requestedSymbols) {
        const consumers = resolvedBindings.filter(binding => binding.to === item.path && (binding.imported === item.name || binding.exported === item.name));
        for (const binding of consumers.slice(0, 200)) symbolConsumers.push({ changedPath: item.path, symbol: item.name, importer: binding.from, localName: binding.local, importedName: binding.imported, resolution: binding.compiler ? 'typescript-compiler' : 'static-export-binding' });
      }
      const impacted = [...new Set([...direct, ...transitive])];
      const supportedChanges = supported.length ? projectIndexAnalyzeTestImpact(index, supported) : null;
      const moduleRecords = impacted.slice(0, 500).map(path => ({ path, relationship: direct.has(path) ? 'direct-importer' : 'transitive-importer', distance: distance.get(path) ?? null, language: filesByPath.get(path)?.language || 'unknown' }));
      const confidence = graph.compilerAnalysis?.supported && !graph.compilerAnalysis?.truncated ? 'compiler-assisted-static-graph' : 'bounded-static-graph';
      return {
        available: true, root: index.rootName, indexedAt: index.updatedAt, analysisMethod: confidence,
        changedPaths: changed, matchedChangedPaths: matched, analyzedCodeChanges: supported,
        unknownChangedPaths: unknown, unanalysedChangedPaths: unanalysed,
        directDependents: moduleRecords.filter(item => item.relationship === 'direct-importer'),
        transitiveDependents: moduleRecords.filter(item => item.relationship === 'transitive-importer'),
        impactedModules: moduleRecords, impactedModuleCount: impacted.length,
        symbolConsumers: symbolConsumers.slice(0, 1000), assetConsumers: assetConsumers.slice(0, 500),
        summary: { suppliedChanges: changed.length, matchedChanges: matched.length, analyzedCodeChanges: supported.length, unknownChanges: unknown.length, unanalysedChanges: unanalysed.length, directDependents: direct.size, transitiveDependents: transitive.size, symbolConsumers: symbolConsumers.length, assetConsumers: assetConsumers.length, recommendedTests: supportedChanges?.summary?.recommendedTests || 0 },
        limitations: [graph.note || 'Static import graph only.', 'Dynamic imports, runtime-generated references, conditional runtime behavior and external package internals may be missed.', 'Symbol-consumer results are included only for explicitly supplied changed_symbols.', 'HTML/CSS/SVG asset links are path-based references, not runtime rendering validation.', 'No files were changed and no tests were executed.']
      };
    }

    function projectIndexBuildReleaseReadiness(index, diagnostics = null) {
      if (!index) return { available: false, message: 'Build the project index before assessing release readiness.' };
      const diagnostic = diagnostics || projectIndexBuildDiagnostics(index);
      const files = (index.files || []).map(file => file.path);
      const packageRecords = projectIndexPackageData(index);
      const rootPackage = packageRecords.find(item => item.path === 'package.json');
      const version = typeof rootPackage?.data?.version === 'string' ? rootPackage.data.version : null;
      const changelog = files.find(path => /^changelog(?:\.[^/]+)?$/i.test(path.split('/').pop())) || null;
      const readme = files.find(path => /^readme(?:\.[^/]+)?$/i.test(path.split('/').pop())) || null;
      const ciFiles = files.filter(path => /(?:^|\/)(?:\.github\/workflows\/|\.gitlab-ci\.yml$|azure-pipelines\.yml$|\.circleci\/config\.yml$)/i.test(path));
      const testFiles = diagnostic.testFiles || [];
      const checks = [];
      const add = (id, status, message, evidence = []) => checks.push({ id, status, message, evidence });
      const validVersion = !!version && /^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(version);
      add('version', !version ? 'warning' : validVersion ? 'pass' : 'fail', !version ? 'No root package version was found in package.json.' : validVersion ? `Root package version is ${version}.` : `Root package version '${version}' is not valid SemVer.`, version ? ['package.json'] : []);
      const changelogRecord = changelog ? (index.files || []).find(file => file.path === changelog) : null;
      add('changelog', changelog ? (version && changelogRecord?.text?.includes(version.toLowerCase()) ? 'pass' : 'warning') : 'warning', changelog ? (version && changelogRecord?.text?.includes(version.toLowerCase()) ? 'Changelog references the current package version.' : 'A changelog exists, but the current package version was not found in its indexed text.') : 'No conventional changelog file was found.', changelog ? [changelog] : []);
      add('readme', readme ? 'pass' : 'warning', readme ? 'A README file is present.' : 'No README file was found.', readme ? [readme] : []);
      add('tests', testFiles.length ? 'pass' : 'warning', testFiles.length ? `${testFiles.length} test files were discovered.` : 'No conventional test files were discovered.', testFiles.slice(0, 20));
      add('ci', ciFiles.length ? 'pass' : 'warning', ciFiles.length ? `${ciFiles.length} CI configuration files were found.` : 'No recognized CI configuration was found.', ciFiles.slice(0, 20));
      add('lockfiles', diagnostic.packageManager && diagnostic.lockfiles.length ? 'pass' : 'warning', diagnostic.packageManager ? `Package manager selected: ${diagnostic.packageManager}.` : 'Package manager/lockfile is not unambiguous.', diagnostic.lockfiles.map(item => item.file));
      add('static-diagnostics', diagnostic.summary.errors ? 'fail' : diagnostic.summary.warnings ? 'warning' : 'pass', `${diagnostic.summary.errors} errors and ${diagnostic.summary.warnings} warnings from Project Diagnostics.`);
      add('build-script', diagnostic.packageScripts?.build ? 'pass' : 'warning', diagnostic.packageScripts?.build ? 'A build script is declared.' : 'No root build script was found.');
      add('test-script', diagnostic.packageScripts?.test ? 'pass' : 'warning', diagnostic.packageScripts?.test ? 'A test script is declared.' : 'No root test script was found.');
      const blockers = checks.filter(item => item.status === 'fail').length;
      const warnings = checks.filter(item => item.status === 'warning').length;
      const manager = diagnostic.packageManager;
      const suggestedCommands = {};
      for (const script of ['build', 'test']) if (diagnostic.packageScripts?.[script] && manager) suggestedCommands[script] = manager === 'npm' ? `npm run ${script}` : manager === 'yarn' ? `yarn ${script}` : `${manager} run ${script}`;
      return { available: true, root: index.rootName, version, status: blockers ? 'blocked' : warnings ? 'not-yet-ready' : 'static-checks-passed', summary: { blockers, warnings, passed: checks.filter(item => item.status === 'pass').length }, checks, packageManager: manager || null, scriptDefinitions: { build: diagnostic.packageScripts?.build || null, test: diagnostic.packageScripts?.test || null }, suggestedCommands, executionPerformed: false, notVerified: ['Git working-tree cleanliness and uncommitted changes.', 'Actual .NET/Node/package-manager builds and test execution.', 'Dependency vulnerability audit against a live registry.', 'Artifact signing, provenance, SBOM, and publish/deploy permissions.', 'Target operating-system packaging and runtime acceptance.'], note: 'This is a static release-readiness assessment, not release approval. It does not create tags, commits, packages, signatures, or publications.' };
    }
    async function getProjectMissionControl({ changedPaths = [], refreshIfStale = true } = {}) {
      if (typeof projectIndexAutoRefreshPromise !== 'undefined' && projectIndexAutoRefreshPromise) await projectIndexAutoRefreshPromise;
      let index = await loadProjectIndex();
      if (!index) return { available: false, message: 'Build the project index before opening Project Mission Control.' };

      const staleAfterMs = 5 * 60 * 1000;
      const getIndexAge = value => {
        const timestamp = Date.parse(value || '');
        return Number.isFinite(timestamp) ? Math.max(0, Date.now() - timestamp) : null;
      };
      let ageMs = getIndexAge(index.updatedAt);
      const refresh = { attempted: false, succeeded: false, reason: null, error: null };
      if (refreshIfStale && (ageMs === null || ageMs > staleAfterMs)) {
        refresh.attempted = true;
        refresh.reason = ageMs === null ? 'index timestamp unavailable' : 'index older than five minutes';
        try {
          await buildProjectIndex({ force: false });
          const refreshed = await loadProjectIndex();
          if (refreshed) { index = refreshed; refresh.succeeded = true; }
        } catch (error) {
          refresh.error = String(error?.message || error).slice(0, 400);
        }
        ageMs = getIndexAge(index.updatedAt);
      }

      const diagnostics = projectIndexBuildDiagnostics(index);
      const readiness = projectIndexBuildReleaseReadiness(index, diagnostics);
      const understanding = projectIndexBuildUnderstanding(index);
      const normalizedChangedPaths = [...new Set((Array.isArray(changedPaths) ? changedPaths : [])
        .map(value => String(value || '').trim()).filter(Boolean))].slice(0, 100);
      const impact = normalizedChangedPaths.length ? projectIndexAnalyzeTestImpact(index, normalizedChangedPaths) : null;
      const stats = index.stats || {};
      const coverage = understanding.analysisCoverage || {};
      const ageMinutes = ageMs === null ? null : Math.round(ageMs / 60000);
      const freshness = ageMs === null ? 'unknown' : ageMs > staleAfterMs ? 'stale' : 'fresh';
      const architecture = {
        entryPoints: understanding.entryPoints.length,
        entryPointExamples: understanding.entryPoints.slice(0, 30),
        modules: understanding.modules.length,
        moduleBreakdown: understanding.modules.slice(0, 20),
        highCouplingModules: understanding.highCouplingModules.length,
        highCouplingExamples: understanding.highCouplingModules.slice(0, 10),
        assetReferences: (understanding.assetReferences || []).slice(0, 200),
        circularDependencyGroups: understanding.dependencyDirection.circularDependencies.length,
        circularDependencies: understanding.dependencyDirection.circularDependencies.slice(0, 10),
        unresolvedImports: understanding.dependencyDirection.unresolvedBindings,
        localImportEdges: understanding.dependencyDirection.localImportEdges,
        resolvedBindings: understanding.dependencyDirection.resolvedBindings,
        analysisCoverage: {
          indexedFiles: coverage.indexedFiles || 0,
          javascriptFiles: coverage.javascriptFiles || 0,
          typescriptFiles: coverage.typescriptFiles || 0,
          htmlFiles: coverage.htmlFiles || 0,
          cssFiles: coverage.cssFiles || 0,
          svgFiles: coverage.svgFiles || 0,
          parseErrors: (coverage.parseErrors || []).slice(0, 20),
          parseErrorCount: (coverage.parseErrors || []).length,
          indexTruncated: !!coverage.indexTruncated
        }
      };
      const indexCompleteness = {
        files: stats.files || (index.files || []).length,
        scanned: stats.scanned || 0,
        readBytes: stats.readBytes || 0,
        reused: stats.reused || 0,
        removed: stats.removed || 0,
        skipped: stats.skipped || 0,
        truncated: !!stats.truncated,
        truncationReason: stats.reason || null,
        freshness,
        ageMinutes,
        staleAfterMinutes: 5,
        refresh
      };

      const recommendations = [];
      const addRecommendation = (id, priority, action, evidence = [], source = 'Project Diagnostics') => {
        const key = String(action || '').trim().toLowerCase();
        if (!key || recommendations.some(item => item.key === key)) return;
        recommendations.push({ id, key, priority, action, evidence: (evidence || []).slice(0, 8), source });
      };
      for (const item of diagnostics.checks) {
        if (item.severity === 'error' || item.severity === 'warning')
          addRecommendation(item.id, item.severity === 'error' ? 'critical' : 'high', item.message, item.evidence, 'Project Diagnostics');
      }
      for (const item of readiness.checks) {
        if (item.status === 'fail' || item.status === 'warning')
          addRecommendation(item.id, item.status === 'fail' ? 'critical' : 'medium', item.message, item.evidence, 'Release Readiness');
      }
      if (freshness !== 'fresh') addRecommendation('index-freshness', freshness === 'stale' ? 'high' : 'medium', freshness === 'stale' ? 'Refresh the project index before relying on its findings.' : 'The project index timestamp is unavailable; refresh it to establish evidence freshness.', [], 'Project Understanding');
      if (stats.truncated) addRecommendation('index-truncated', 'high', `The project index is partial${stats.reason ? `: ${stats.reason}` : ''}. Review scan limits before treating absence of findings as proof.`, [], 'Project Understanding');
      if (architecture.circularDependencyGroups) addRecommendation('architecture-cycles', 'high', `${architecture.circularDependencyGroups} circular dependency group(s) were detected. Review their import direction and initialization behavior.`, architecture.circularDependencies.flat(), 'Project Understanding');
      if (architecture.highCouplingModules) addRecommendation('architecture-coupling', 'medium', `${architecture.highCouplingModules} high-coupling module(s) were identified. Review the largest dependency hubs before broad refactoring.`, architecture.highCouplingExamples.map(item => item.path), 'Project Understanding');
      const priorityRank = { critical: 0, high: 1, medium: 2, low: 3 };
      recommendations.sort((a, b) => priorityRank[a.priority] - priorityRank[b.priority] || a.action.localeCompare(b.action));
      const nextActions = recommendations.slice(0, 15).map(({ key, ...item }) => item);

      const diagnosticErrors = diagnostics.summary.errors || 0;
      const releaseBlockers = readiness.summary.blockers || 0;
      let status = 'static-checks-passed';
      if (diagnosticErrors || releaseBlockers) status = 'blocked';
      else if (freshness !== 'fresh' || stats.truncated) status = 'incomplete';
      else if ((diagnostics.summary.warnings || 0) || (readiness.summary.warnings || 0) || architecture.circularDependencyGroups) status = 'attention';

      return {
        available: true,
        schemaVersion: 2,
        generatedAt: new Date().toISOString(),
        root: index.rootName,
        indexedAt: index.updatedAt,
        status,
        statusExplanation: status === 'blocked' ? 'Static blockers or diagnostic errors require review.' : status === 'incomplete' ? 'Evidence is stale or the project scan is partial; refresh/review coverage before relying on this assessment.' : status === 'attention' ? 'No hard static blocker was found, but warnings or architecture risks need review.' : 'No issues were found by the available static checks; this is not release approval.',
        index: indexCompleteness,
        projectUnderstanding: architecture,
        diagnostics: { status: diagnostics.status, summary: diagnostics.summary, checks: diagnostics.checks.slice(0, 50), packageManager: diagnostics.packageManager, lockfiles: diagnostics.lockfiles, testFilesFound: diagnostics.testFilesFound, toolingConfigurations: diagnostics.toolingConfigurations, execution: diagnostics.execution },
        releaseReadiness: { status: readiness.status, summary: readiness.summary, checks: readiness.checks, packageManager: readiness.packageManager, suggestedCommands: readiness.suggestedCommands, executionPerformed: false, notVerified: readiness.notVerified, note: readiness.note },
        testImpact: impact ? { available: true, summary: impact.summary, changedPaths: impact.changedPaths, matchedChangedPaths: impact.matchedChangedPaths, analyzedChangedPaths: impact.analyzedChangedPaths, unanalysedChangedPaths: impact.unanalysedChangedPaths, unknownChangedPaths: impact.unknownChangedPaths, impactedModuleCount: impact.impactedModuleCount, tests: impact.tests.slice(0, 100), executionPerformed: false, note: impact.note } : { available: false, message: 'Supply workspace-relative changed_paths to calculate impacted tests. Git changes are not inferred.' },
        nextActions,
        scope: 'Language-aware project analysis is limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. Other source files remain generic text and are reported as unanalysed for test impact.',
        verification: { staticAnalysis: true, build: false, tests: false, liveDependencyAudit: false, gitStatus: false, signing: false, packaging: false, publication: false, commandsExecuted: false },
        limitations: [
          'Static import analysis cannot prove runtime behavior and may miss dynamic imports, generated dependencies, conditional runtime loading and external test harness behavior.',
          'Release readiness does not verify Git cleanliness, actual builds/tests, live vulnerability results, signing, SBOM, packaging or publication.',
          'High coupling and entry-point signals are evidence-based heuristics, not proof of architectural defects or runtime entry points.'
        ],
        executionPerformed: false
      };
    }

    let projectIndexAutoRefreshTimer = null;
    let projectIndexAutoRefreshController = null;
    let projectIndexAutoRefreshPromise = null;
    let projectIndexAutoRefreshContextId = '';
    async function refreshProjectIndexAutomatically(reason = 'workspace selected') {
      if (!agentDirectoryHandle) return null;
      const contextId = workspaceContext.id;
      if (projectIndexAutoRefreshPromise && projectIndexAutoRefreshContextId === contextId) return projectIndexAutoRefreshPromise;
      projectIndexAutoRefreshController?.abort();
      const controller = new AbortController();
      projectIndexAutoRefreshController = controller;
      projectIndexAutoRefreshContextId = contextId;
      projectIndexAutoRefreshPromise = (async () => {
        try {
          if (typeof projectIndexStatusElement !== 'undefined' && projectIndexStatusElement)
            projectIndexStatusElement.textContent = `Checking project knowledge · ${reason}…`;
          const result = await buildProjectIndex({ force: false, signal: controller.signal, onProgress: progress => {
            if (typeof projectIndexStatusElement !== 'undefined' && projectIndexStatusElement && workspaceContext.id === contextId)
              projectIndexStatusElement.textContent = `Updating project knowledge · ${progress.files} indexed · ${progress.scanned} scanned…`;
          } });
          if (workspaceContext.id !== contextId) return null;
          if (typeof projectIndexStatusElement !== 'undefined' && projectIndexStatusElement) {
            const changed = result.readBytes > 0 || result.removed > 0 || result.reused < result.files;
            projectIndexStatusElement.textContent = `${result.files} indexed files · ${result.reused} reused · ${result.removed} removed · ${result.readBytes.toLocaleString()} bytes read · ${changed ? 'knowledge updated' : 'already current'}${result.truncated ? ` · Partial: ${result.reason}` : ''} · automatic refresh enabled`;
          }
          return result;
        } catch (error) {
          if (error?.name !== 'AbortError' && workspaceContext.id === contextId && typeof projectIndexStatusElement !== 'undefined' && projectIndexStatusElement)
            projectIndexStatusElement.textContent = `Automatic project index refresh failed: ${error?.message || error}`;
          return null;
        } finally {
          if (projectIndexAutoRefreshController === controller) projectIndexAutoRefreshController = null;
          if (projectIndexAutoRefreshContextId === contextId) projectIndexAutoRefreshPromise = null;
        }
      })();
      return projectIndexAutoRefreshPromise;
    }
    function scheduleProjectIndexAutoRefresh(reason = 'workspace selected') {
      if (!agentDirectoryHandle) {
        projectIndexAutoRefreshController?.abort();
        projectIndexAutoRefreshController = null;
        if (projectIndexAutoRefreshTimer) clearInterval(projectIndexAutoRefreshTimer);
        projectIndexAutoRefreshTimer = null;
        projectIndexAutoRefreshContextId = '';
        return;
      }
      if (projectIndexAutoRefreshContextId !== workspaceContext.id) {
        projectIndexAutoRefreshController?.abort();
        if (projectIndexAutoRefreshTimer) clearInterval(projectIndexAutoRefreshTimer);
        projectIndexAutoRefreshTimer = null;
      }
      void refreshProjectIndexAutomatically(reason);
      if (!projectIndexAutoRefreshTimer) {
        projectIndexAutoRefreshTimer = setInterval(() => {
          if (document.visibilityState === 'hidden' || !agentDirectoryHandle) return;
          void refreshProjectIndexAutomatically('periodic workspace change check');
        }, 60_000);
      }
    }
