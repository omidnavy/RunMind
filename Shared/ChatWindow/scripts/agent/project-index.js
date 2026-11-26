    'use strict';

    const PROJECT_INDEX_PREFIX = 'runmind.project-index.v4.';
    const PROJECT_INDEX_LIMITS = Object.freeze({ files: 10000, entries: 50000, depth: 10, fileBytes: 256 * 1024, totalBytes: 24 * 1024 * 1024 });
    const PROJECT_INDEX_SKIP_DIRS = new Set(['.git','.hg','.svn','.runmind-backups','.cache','node_modules','vendor','bin','obj','dist','build','out','target','.next','.nuxt','.venv','venv','__pycache__','coverage','.idea','.vs','.gradle','.terraform','.npm','.nuget','.cargo','.docker','.pypoetry','.conda','secret','secrets','credential','credentials','.ssh','.aws','.azure','.gnupg']);
    const PROJECT_INDEX_SKIP_FILES = /^(?:\.env(?:\..*)?|(?:.*\.(?:pem|key|p12|pfx|keystore|jks|kdbx|sqlite|db|wasm|dll|exe|so|dylib|bin|lockb)|.*(?:secret|credential).*)|id_(?:rsa|ed25519)|credentials?(?:\..*)?|secrets?(?:\..*)?|\.npmrc|\.pypirc|\.netrc|\.git-credentials|nuget\.config|pip\.conf|\.dockercfg|known_hosts)$/i;
    const PROJECT_INDEX_TEXT_EXTENSIONS = new Set(['.md','.mdx','.txt','.rst','.adoc','.json','.jsonc','.json5','.yaml','.yml','.toml','.ini','.cfg','.conf','.xml','.html','.htm','.css','.scss','.less','.js','.mjs','.cjs','.jsx','.ts','.tsx','.vue','.svelte','.py','.pyi','.cs','.java','.kt','.kts','.go','.rs','.c','.h','.cc','.cpp','.hpp','.hh','.php','.rb','.swift','.sh','.bash','.zsh','.ps1','.bat','.cmd','.sql','.graphql','.gql','.proto','.dockerfile','.makefile','.gradle','.properties','.lock']);

    function projectIndexKey() { return `${PROJECT_INDEX_PREFIX}${workspaceContext.id}`; }
    function projectIndexLanguage(path) {
      const ext = String(path).toLowerCase().match(/\.[^.\/]+$/)?.[0] || '';
      const map = { '.js':'javascript','.mjs':'javascript','.cjs':'javascript','.jsx':'javascript','.ts':'typescript','.tsx':'typescript','.py':'python','.cs':'csharp','.java':'java','.kt':'kotlin','.go':'go','.rs':'rust','.c':'c','.h':'c','.cc':'cpp','.cpp':'cpp','.hpp':'cpp','.php':'php','.rb':'ruby','.swift':'swift','.sh':'shell','.ps1':'powershell','.sql':'sql','.json':'json','.yaml':'yaml','.yml':'yaml','.toml':'toml','.md':'markdown','.html':'html','.css':'css' };
      return map[ext] || (['Dockerfile','Makefile'].includes(path.split('/').pop()) ? 'build' : 'text');
    }
    function projectIndexDependencies(manifests) {
      const dependencies = new Set();
      const add = (manager, name) => { const clean=String(name||'').trim().replace(/^['"\s]+|['"\s]+$/g,''); if(clean && clean.length<=180) dependencies.add(`${manager}:${clean}`); };
      for (const manifest of manifests) {
        const path=manifest.path.toLowerCase(), text=manifest.text;
        if (path==='package.json' || path==='composer.json') {
          try { const data=JSON.parse(text); for(const field of ['dependencies','devDependencies','peerDependencies','require','require-dev']) for(const name of Object.keys(data[field]||{})) add(path==='package.json'?'npm':'composer',name); } catch(_) {}
        } else if (path.startsWith('requirements') && path.endsWith('.txt')) {
          for(const line of text.split(/\r?\n/)){const value=line.trim();if(value&&!value.startsWith('#')&&!value.startsWith('-'))add('pip',value.split(/[<>=!~;[]/)[0]);}
        } else if (path==='go.mod') {
          const block=text.match(/require\s*\(([\s\S]*?)\)/m)?.[1]||'';
          for(const line of (block||text).split(/\r?\n/)){const match=line.trim().match(/^([\w.\-/]+)\s+v[^\s]+/);if(match)add('go',match[1]);}
        } else if (path==='cargo.toml') {
          let section=''; for(const line of text.split(/\r?\n/)){const heading=line.match(/^\s*\[([^\]]+)\]/);if(heading){section=heading[1];continue;}if(/^(?:dependencies|dev-dependencies|build-dependencies)(?:\.|$)/.test(section)){const match=line.match(/^\s*([A-Za-z0-9_-]+)\s*=/);if(match)add('cargo',match[1]);}}
        } else if (path.endsWith('.csproj')) {
          for(const match of text.matchAll(/<PackageReference\b[^>]*\bInclude=["']([^"']+)["']/gi))add('nuget',match[1]);
        } else if (path==='pom.xml') {
          const blocks=text.match(/<dependency>[\s\S]*?<\/dependency>/gi)||[];
          for(const block of blocks){const group=block.match(/<groupId>([^<]+)<\/groupId>/i)?.[1],artifact=block.match(/<artifactId>([^<]+)<\/artifactId>/i)?.[1];if(group&&artifact)add('maven',`${group}:${artifact}`);}
        } else if (path==='pyproject.toml') {
          const section=text.match(/(?:^|\n)dependencies\s*=\s*\[([\s\S]*?)\]/m)?.[1]||'';
          for(const match of section.matchAll(/["']([^"']+)["']/g))add('pip',match[1].split(/[<>=!~;[]/)[0]);
        } else if (path==='gemfile') {
          for(const match of text.matchAll(/^\s*gem\s+["']([^"']+)["']/gm))add('bundler',match[1]);
        } else if (path==='build.gradle' || path==='build.gradle.kts') {
          for(const match of text.matchAll(/(?:implementation|api|compileOnly|runtimeOnly|testImplementation)\s*\(?\s*["']([^"']+:[^"']+)["']/g))add('gradle',match[1]);
        }
      }
      return [...dependencies].sort().slice(0,1000);
    }
    function projectIndexFrameworks(dependencies) {
      const names=dependencies.map(value=>value.toLowerCase()), frameworks=[];
      const rules=[['React',/^(?:npm:)?react$/],['Next.js',/^(?:npm:)?next$/],['Vue',/^(?:npm:)?vue$/],['Angular',/^(?:npm:)?@angular\//],['Express',/^(?:npm:)?express$/],['FastAPI',/^(?:pip:)?fastapi$/],['Django',/^(?:pip:)?django$/],['Flask',/^(?:pip:)?flask$/],['ASP.NET',/^(?:nuget:)?microsoft\.aspnetcore\./],['Entity Framework',/^(?:nuget:)?microsoft\.entityframeworkcore\./],['Spring',/^(?:maven:)?org\.springframework:/],['Tokio',/^(?:cargo:)?tokio$/]];
      for(const [label,pattern] of rules)if(names.some(name=>pattern.test(name)))frameworks.push(label);
      return frameworks;
    }
    function projectIndexToolchains(files, manifests, observedPaths = []) {
      const names = new Set([...files.map(file => file.path), ...observedPaths].map(path => path.split('/').pop().toLowerCase()));
      const packageManifest = manifests.find(item => item.path.split('/').pop().toLowerCase() === 'package.json');
      let declaredPackageManager = '';
      if (packageManifest) {
        try { declaredPackageManager = String(JSON.parse(packageManifest.text).packageManager || '').split('@')[0].toLowerCase(); } catch (_) {}
      }
      const lockfiles = [
        ['npm','package-lock.json'],['pnpm','pnpm-lock.yaml'],['yarn','yarn.lock'],['bun','bun.lock'],['bun','bun.lockb'],
        ['uv','uv.lock'],['poetry','poetry.lock'],['pipenv','pipfile.lock'],['cargo','cargo.lock'],['go','go.sum'],
        ['composer','composer.lock'],['bundler','gemfile.lock']
      ].filter(([,name]) => names.has(name)).map(([manager,name]) => ({manager,file:name}));
      const lockManagers = [...new Set(lockfiles.map(item => item.manager))];
      const selected = declaredPackageManager || (lockManagers.length === 1 ? lockManagers[0] : '');
      const conflicts = declaredPackageManager && lockManagers.some(manager => manager !== declaredPackageManager && ['npm','pnpm','yarn','bun'].includes(manager));
      return {
        packageManager: selected || (lockManagers.length === 1 ? lockManagers[0] : null),
        packageManagerSource: declaredPackageManager ? 'package.json packageManager field' : lockManagers.length === 1 ? 'unique lockfile' : lockManagers.length > 1 ? 'ambiguous lockfiles' : 'not detected',
        lockfiles, conflictingPackageManagerSignals: !!conflicts,
        ecosystems: [...new Set(manifests.map(item => {
          const name=item.path.split('/').pop().toLowerCase();
          if(name==='package.json') return 'node'; if(name==='pyproject.toml'||name.startsWith('requirements')||name==='setup.py') return 'python';
          if(name==='cargo.toml') return 'rust'; if(name==='go.mod') return 'go'; if(name.endsWith('.csproj')||name.endsWith('.sln')) return 'dotnet';
          if(name==='pom.xml'||name.startsWith('build.gradle')) return 'jvm'; if(name==='composer.json') return 'php'; if(name==='gemfile') return 'ruby'; return null;
        }).filter(Boolean))]
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
        const candidates=[base,...['.js','.mjs','.cjs','.jsx','.ts','.tsx','.json','.py'].map(ext=>base+ext),...['index.js','index.mjs','index.cjs','index.jsx','index.ts','index.tsx','index.json','__init__.py'].map(name=>`${base.replace(/\/$/,'')}/${name}`)];
        return candidates.find(candidate=>known.has(candidate))||null;
      };
      const collectTargets = (value, wildcard='') => {
        if(typeof value==='string')return [value.replace(/\*/g,wildcard)];
        if(Array.isArray(value))return value.flatMap(item=>collectTargets(item,wildcard));
        if(value&&typeof value==='object') {
          const keys=Object.keys(value), preferred=['import','require','node','browser','default'];
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
        for(const key of keys) {
          if(!key.startsWith('.')&&!key.startsWith('#'))continue;
          const star=key.indexOf('*'); if(star<0)continue;
          const prefix=key.slice(0,star), suffix=key.slice(star+1);
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
        return [data.module,data.main,data.browser,'./index.js'].filter(value=>typeof value==='string').map(value=>value.startsWith('./')?value:`./${value.replace(/^\.\//,'')}`);
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
        if (!['javascript','typescript','python'].includes(file.language)) continue;
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
        if(exported) resolvedBindings.push({from:file.path,to:target.path,local:binding.local,imported:binding.imported,exported:exported.exported});
        else unresolvedBindings.push({from:file.path,to:target.path,source:binding.source,local:binding.local,imported:binding.imported,reason:'export-not-found-or-not-statically-known'});
      }
      return { localImportEdges: edges.slice(0,5000), resolvedBindings:resolvedBindings.slice(0,5000), unresolvedBindings:unresolvedBindings.slice(0,5000), externalImports:externalImports.slice(0,2000), circularDependencies, filesWithIncomingImports: [...incoming].map(([path,count])=>({path,count})).sort((a,b)=>b.count-a.count||a.path.localeCompare(b.path)).slice(0,100), note:'Relative imports, workspace package imports via package.json exports, package imports (# aliases), and common Node built-ins are recognized. Conditional target selection is conservative; runtime resolution, installed dependency layout, custom loaders, and TypeScript path mappings are not evaluated.' };
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
        addMatches(/(?:^|\n)\s*(?:import\s+(?:[^'"\n]*?\s+from\s+)?|export\s+[^'"\n]*?\s+from\s+|import\s*\()(['"])([^'"\n]+)\1/g, imports);
        addMatches(/\brequire\s*\(\s*(['"])([^'"]+)\1\s*\)/g, imports);
        addMatches(/(?:^|\n)\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?(?:function|class|interface|type|enum|const|let|var)\s+([A-Za-z_$][\w$]*)/g, symbols);
      } else if (language==='python') {
        addMatches(/(?:^|\n)\s*(?:from\s+([\w.]+)\s+import|import\s+([\w.]+))/g, imports);
        addMatches(/(?:^|\n)\s*(?:async\s+)?(?:def|class)\s+([A-Za-z_][\w]*)/g, symbols);
      } else if (language==='csharp') {
        addMatches(/(?:^|\n)\s*using\s+([A-Za-z_][\w.]*)\s*;/g, imports);
        addMatches(/(?:^|\n)\s*(?:public|internal|private|protected)?\s*(?:static\s+|abstract\s+|sealed\s+|partial\s+)*(?:class|interface|record|enum|struct)\s+([A-Za-z_][\w]*)/g, symbols);
      } else if (language==='go') {
        addMatches(/(?:^|\n)\s*import\s+(?:\w+\s+)?"([^"]+)"/g, imports);
        addMatches(/(?:^|\n)\s*func\s+(?:\([^)]*\)\s*)?([A-Za-z_][\w]*)/g, symbols);
      } else if (language==='rust') {
        addMatches(/(?:^|\n)\s*(?:use|extern\s+crate)\s+([^;]+);/g, imports);
        addMatches(/(?:^|\n)\s*(?:pub\s+)?(?:async\s+)?(?:fn|struct|enum|trait|type|mod)\s+([A-Za-z_][\w]*)/g, symbols);
      } else if (language==='java' || language==='kotlin') {
        addMatches(/(?:^|\n)\s*import\s+([\w.*]+)/g, imports);
        addMatches(/(?:^|\n)\s*(?:public\s+|private\s+|protected\s+|internal\s+|abstract\s+|final\s+)*(?:class|interface|enum|object|fun)\s+([A-Za-z_][\w]*)/g, symbols);
      }
      return { imports: imports.slice(0,120), symbols: symbols.slice(0,120), symbolDetails: [], identifierOccurrences: [], parseError: null };
    }
    async function loadProjectIndex() {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace first.');
      const value = await workspaceDbAction('readonly','get',undefined,projectIndexKey());
      return value && value.version===4 && value.contextId===workspaceContext.id ? value : null;
    }
    async function buildProjectIndex({ force=false, signal=null, onProgress=null }={}) {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace first.');
      const previous = force ? null : await loadProjectIndex();
      const oldFiles = new Map((previous?.files || []).map(file => [file.path,file]));
      const files = [], manifests = [], technologies = new Set(), visitedPaths = new Set();
      let entries=0, scanned=0, readBytes=0, reused=0, skipped=0, truncated=false, reason='';
      const stop = value => { truncated=true; if(!reason) reason=value; };
      const manifestNames = new Set(['package.json','pyproject.toml','requirements.txt','setup.py','cargo.toml','go.mod','pom.xml','build.gradle','build.gradle.kts','*.csproj','*.sln','composer.json','gemfile','dockerfile','makefile','package-lock.json','pnpm-lock.yaml','yarn.lock','bun.lock','bun.lockb','uv.lock','poetry.lock','pipfile.lock','cargo.lock','go.sum','composer.lock','gemfile.lock']);
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
          const isManifest=manifestNames.has(lower)||lower.startsWith('requirements')&&lower.endsWith('.txt')||lower.endsWith('.csproj')||lower.endsWith('.sln');
          if(!isManifest&&!PROJECT_INDEX_TEXT_EXTENSIONS.has(ext)) { skipped++; continue; }
          try {
            const file=await handle.getFile();
            if(file.size>PROJECT_INDEX_LIMITS.fileBytes){skipped++;continue;}
            const prior=oldFiles.get(path);
            if(prior && prior.size===file.size && prior.lastModified===file.lastModified && typeof prior.text==='string' && (prior.language!=='javascript' || String(prior.analysisEngine||'').startsWith('acorn'))) {
              files.push(prior); reused++;
              if(isManifest) manifests.push({path,text:prior.manifestText||prior.text.slice(0,64000)});
              if(prior.language && prior.language!=='text') technologies.add(prior.language);
              continue;
            }
            if(readBytes+file.size>PROJECT_INDEX_LIMITS.totalBytes){stop('aggregate text budget reached');return;}
            const source=await file.text(); readBytes+=file.size;
            const text=redactProjectIndexSecrets(source);
            const language=projectIndexLanguage(path), extracted=projectIndexExtract(text,language,path);
            const record={path,size:file.size,lastModified:file.lastModified,language,analysisEngine:extracted.engine||'regex-fallback',parseError:extracted.parseError||null,imports:extracted.imports,symbols:extracted.symbols,symbolDetails:extracted.symbolDetails||[],identifierOccurrences:extracted.identifierOccurrences||[],references:extracted.references||[],calls:extracted.calls||[],exports:extracted.exports||[],importBindings:extracted.importBindings||[],manifestText:isManifest?text.slice(0,64000):null,text:text.slice(0,Math.min(text.length,160000)).toLowerCase()};
            files.push(record);
            if(isManifest) manifests.push({path,text:text.slice(0,64000)});
            if(language!=='text') technologies.add(language);
          } catch (_) { skipped++; }
          if(files.length%100===0) onProgress?.({files:files.length,scanned,readBytes});
        }
      }
      await visit(agentDirectoryHandle,'',0);
      if(signal?.aborted) throw new DOMException('Project indexing cancelled.','AbortError');
      if(truncated && previous) for(const [path,record] of oldFiles) { if(files.length>=PROJECT_INDEX_LIMITS.files) break; if(!visitedPaths.has(path)) files.push(record); }
      const removed = truncated ? 0 : [...oldFiles.keys()].filter(path => !visitedPaths.has(path)).length;
      const dependencies=projectIndexDependencies(manifests);
      const frameworks=projectIndexFrameworks(dependencies);
      const toolchains=projectIndexToolchains(files,manifests,[...visitedPaths]);
      const importGraph=projectIndexLocalImportGraph(files,manifests);
      const index={version:4,contextId:workspaceContext.id,rootName:agentDirectoryHandle.name||workspaceContext.folderName||'workspace',createdAt:previous?.createdAt||new Date().toISOString(),updatedAt:new Date().toISOString(),files,manifests:manifests.map(({path,text})=>({path,text})),technologies:[...technologies].sort(),frameworks,dependencies,toolchains,importGraph,stats:{files:files.length,scanned,entries,readBytes,reused,removed,skipped,truncated,reason}};
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
      const calls=files.flatMap(file=>(file.calls||[]).filter(item=>!name||item.callee===name||item.caller===name).map(item=>({path:file.path,...item})));
      const graph=index.importGraph||{};
      const edges=(graph.localImportEdges||[]).filter(item=>!path||item.from===path||item.to===path);
      const circularDependencies=(graph.circularDependencies||[]).filter(cycle=>!path||cycle.includes(path));
      const externalImports=(graph.externalImports||[]).filter(item=>!path||item.from===path);
      const resolved=(graph.resolvedBindings||[]).filter(item=>(!path||item.from===path||item.to===path)&&(!name||item.local===name||item.imported===name));
      const unresolved=(graph.unresolvedBindings||[]).filter(item=>(!path||item.from===path||item.to===path)&&(!name||item.local===name||item.imported===name));
      const crossFileReferences=projectIndexCrossFileReferences(index.files,graph,{path,name,limit});
      const maximum=Math.max(1,Math.min(1000,Number(limit)||200));
      return {indexUpdatedAt:index.updatedAt,filters:{path:path||null,name:name||null},declarations:declarations.slice(0,maximum),references:references.slice(0,maximum),calls:calls.slice(0,maximum),moduleEdges:edges.slice(0,maximum),circularDependencies:circularDependencies.slice(0,100),externalImports:externalImports.slice(0,maximum),resolvedImports:resolved.slice(0,maximum),unresolvedImports:unresolved.slice(0,maximum),crossFileReferences:crossFileReferences.results,crossFileReferencesTruncated:crossFileReferences.truncated,truncated:crossFileReferences.truncated||declarations.length>maximum||references.length>maximum||calls.length>maximum||edges.length>maximum||circularDependencies.length>100||externalImports.length>maximum||resolved.length>maximum||unresolved.length>maximum,scope:'JavaScript lexical bindings, syntactic call sites, statically-resolved module exports, and cross-file references through resolved static imports; dynamic/runtime references, unresolved imports, and full compiler semantics are not included.'};
    }
    async function getProjectIndexOverview() {
      const index=await loadProjectIndex();
      if(!index) return {indexed:false,message:'No project index has been built.'};
      return {indexed:true,root:index.rootName,updatedAt:index.updatedAt,stats:index.stats,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies,toolchains:index.toolchains||null,analysis:{acornFiles:index.files.filter(item=>String(item.analysisEngine||'').startsWith('acorn')).length,callSites:index.files.reduce((sum,item)=>sum+(item.calls?.length||0),0),parseErrors:index.files.filter(item=>item.parseError).length,regexFallbackFiles:index.files.filter(item=>item.analysisEngine==='regex-fallback').length},importGraph:index.importGraph?{localImportEdges:index.importGraph.localImportEdges.length,resolvedBindings:index.importGraph.resolvedBindings?.length||0,unresolvedBindings:index.importGraph.unresolvedBindings?.length||0,circularDependencies:index.importGraph.circularDependencies?.length||0,externalImports:index.importGraph.externalImports?.length||0,filesWithIncomingImports:index.importGraph.filesWithIncomingImports,note:index.importGraph.note}:null,manifests:index.manifests.map(item=>item.path),topLevel:index.files.filter(item=>!item.path.includes('/')).map(item=>item.path).slice(0,100)};
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
