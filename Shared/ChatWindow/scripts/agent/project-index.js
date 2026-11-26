    'use strict';

    const PROJECT_INDEX_PREFIX = 'runmind.project-index.v2.';
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
    function projectIndexLocalImportGraph(files) {
      const known = new Set(files.map(file => file.path.replace(/\\/g,'/')));
      const edges = [];
      for (const file of files) {
        if (!['javascript','typescript','python'].includes(file.language)) continue;
        for (const specifier of file.imports || []) {
          if (!specifier.startsWith('.') && !(file.language==='python' && specifier.startsWith('.'))) continue;
          const base = file.path.split('/').slice(0,-1).join('/');
          const candidate = (base ? `${base}/${specifier}` : specifier).replace(/\\/g,'/').replace(/\/\.\//g,'/');
          const candidates = [candidate, ...(['javascript','typescript'].includes(file.language) ? ['.js','.mjs','.cjs','.jsx','.ts','.tsx','.json'].map(ext=>candidate+ext) : ['.py'].map(ext=>candidate+ext)), ...(['index.js','index.ts','index.tsx','__init__.py'].map(name=>`${candidate.replace(/\/$/,'')}/${name}`))];
          const target = candidates.find(path => known.has(path));
          if (target) edges.push({from:file.path,to:target,specifier});
        }
      }
      const incoming = new Map();
      for (const edge of edges) incoming.set(edge.to,(incoming.get(edge.to)||0)+1);
      return { localImportEdges: edges.slice(0,5000), filesWithIncomingImports: [...incoming].map(([path,count])=>({path,count})).sort((a,b)=>b.count-a.count||a.path.localeCompare(b.path)).slice(0,100), note:'Local relative import links use AST-extracted JavaScript specifiers where Acorn parsing succeeds; path resolution remains best-effort and is not compiler-resolved.' };
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
      let ast;
      let parseError = null;
      try {
        ast = parser.parse(source, { ecmaVersion: 'latest', sourceType: 'module', allowHashBang: true });
      } catch (error) {
        parseError = error;
        try { ast = parser.parse(source, { ecmaVersion: 'latest', sourceType: 'script', allowHashBang: true, allowReturnOutsideFunction: true }); }
        catch (_) { return { supported: true, parseError: { message: String(parseError.message || parseError).slice(0, 300), line: parseError.loc?.line || null, column: parseError.loc?.column ?? null }, imports: [], symbols: [], symbolDetails: [], identifierOccurrences: [] }; }
      }
      const imports = new Set(), declarations = new Map(), occurrences = new Map(), declarationPositions = new Set();
      const addPattern = pattern => {
        if (!pattern) return;
        if (pattern.type === 'Identifier') {
          declarations.set(pattern.name, (declarations.get(pattern.name) || 0) + 1);
          declarationPositions.add(pattern.start);
        } else if (pattern.type === 'RestElement') addPattern(pattern.argument);
        else if (pattern.type === 'AssignmentPattern') addPattern(pattern.left);
        else if (pattern.type === 'ArrayPattern') for (const item of pattern.elements) addPattern(item);
        else if (pattern.type === 'ObjectPattern') for (const item of pattern.properties) addPattern(item.type === 'RestElement' ? item.argument : item.value);
      };
      const stack = [{ node: ast, parent: null, key: '' }];
      while (stack.length) {
        const { node, parent, key } = stack.pop();
        if (!node || typeof node.type !== 'string') continue;
        switch (node.type) {
          case 'ImportDeclaration':
          case 'ExportNamedDeclaration':
          case 'ExportAllDeclaration':
            if (node.source && typeof node.source.value === 'string') imports.add(node.source.value);
            break;
          case 'ImportExpression':
            if (node.source?.type === 'Literal' && typeof node.source.value === 'string') imports.add(node.source.value);
            break;
          case 'CallExpression':
            if (node.callee?.type === 'Identifier' && node.callee.name === 'require' && node.arguments?.[0]?.type === 'Literal' && typeof node.arguments[0].value === 'string') imports.add(node.arguments[0].value);
            break;
          case 'FunctionDeclaration':
          case 'FunctionExpression':
          case 'ArrowFunctionExpression':
            if (node.type === 'FunctionDeclaration' || node.type === 'FunctionExpression') addPattern(node.id);
            for (const parameter of node.params || []) addPattern(parameter);
            break;
          case 'ClassDeclaration':
          case 'ClassExpression':
            if (node.type === 'ClassDeclaration') addPattern(node.id);
            break;
          case 'VariableDeclarator':
            addPattern(node.id);
            break;
          case 'ImportSpecifier':
          case 'ImportDefaultSpecifier':
          case 'ImportNamespaceSpecifier':
            addPattern(node.local);
            break;
          case 'CatchClause':
            addPattern(node.param);
            break;
          case 'Identifier': {
            const isNonReferenceKey = (parent?.type === 'MemberExpression' && key === 'property' && !parent.computed)
              || ((parent?.type === 'Property' || parent?.type === 'MethodDefinition' || parent?.type === 'PropertyDefinition') && key === 'key' && !parent.computed && !parent.shorthand)
              || parent?.type === 'LabeledStatement' || parent?.type === 'BreakStatement' || parent?.type === 'ContinueStatement'
              || parent?.type === 'ImportSpecifier' || parent?.type === 'ImportDefaultSpecifier' || parent?.type === 'ImportNamespaceSpecifier'
              || (parent?.type === 'ExportSpecifier' && key === 'exported');
            if (!isNonReferenceKey && !declarationPositions.has(node.start)) occurrences.set(node.name, (occurrences.get(node.name) || 0) + 1);
            break;
          }
        }
        for (const childKey of Object.keys(node)) {
          if (childKey === 'start' || childKey === 'end' || childKey === 'loc' || childKey === 'range') continue;
          const value = node[childKey];
          if (Array.isArray(value)) {
            for (let i = value.length - 1; i >= 0; i--) if (value[i] && typeof value[i].type === 'string') stack.push({ node: value[i], parent: node, key: childKey });
          } else if (value && typeof value.type === 'string') stack.push({ node: value, parent: node, key: childKey });
        }
      }
      const symbolDetails = [...declarations].map(([name, declarationCount]) => ({ name, declarationCount, referenceOccurrences: occurrences.get(name) || 0 })).sort((a,b)=>a.name.localeCompare(b.name));
      return { supported: true, engine: 'acorn', imports: [...imports].slice(0, 120), symbols: symbolDetails.map(item=>item.name).slice(0, 120), symbolDetails: symbolDetails.slice(0, 500), identifierOccurrences: [...occurrences].map(([name,count])=>({name,count})).sort((a,b)=>b.count-a.count||a.name.localeCompare(b.name)).slice(0, 500), parseError: null };
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
      return value && value.version===2 && value.contextId===workspaceContext.id ? value : null;
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
            if(prior && prior.size===file.size && prior.lastModified===file.lastModified && typeof prior.text==='string' && (prior.language!=='javascript' || prior.analysisEngine==='acorn')) {
              files.push(prior); reused++;
              if(isManifest) manifests.push({path,text:prior.text.slice(0,16000)});
              if(prior.language && prior.language!=='text') technologies.add(prior.language);
              continue;
            }
            if(readBytes+file.size>PROJECT_INDEX_LIMITS.totalBytes){stop('aggregate text budget reached');return;}
            const source=await file.text(); readBytes+=file.size;
            const text=redactProjectIndexSecrets(source);
            const language=projectIndexLanguage(path), extracted=projectIndexExtract(text,language,path);
            const record={path,size:file.size,lastModified:file.lastModified,language,analysisEngine:extracted.engine||'regex-fallback',parseError:extracted.parseError||null,imports:extracted.imports,symbols:extracted.symbols,symbolDetails:extracted.symbolDetails||[],identifierOccurrences:extracted.identifierOccurrences||[],text:text.slice(0,Math.min(text.length,160000)).toLowerCase()};
            files.push(record);
            if(isManifest) manifests.push({path,text:text.slice(0,16000)});
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
      const importGraph=projectIndexLocalImportGraph(files);
      const index={version:2,contextId:workspaceContext.id,rootName:agentDirectoryHandle.name||workspaceContext.folderName||'workspace',createdAt:previous?.createdAt||new Date().toISOString(),updatedAt:new Date().toISOString(),files,manifests:manifests.map(({path,text})=>({path,text})),technologies:[...technologies].sort(),frameworks,dependencies,toolchains,importGraph,stats:{files:files.length,scanned,entries,readBytes,reused,removed,skipped,truncated,reason}};
      await workspaceDbAction('readwrite','put',index,projectIndexKey());
      projectIndexStatus?.(index);
      return {root:index.rootName,files:index.stats.files,scanned,entries,readBytes,reused,removed,skipped,truncated,reason,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies.slice(0,100),toolchains:index.toolchains,importGraphSummary:{localImportEdges:index.importGraph?.localImportEdges?.length||0,mostImportedFiles:index.importGraph?.filesWithIncomingImports||[],note:index.importGraph?.note},updatedAt:index.updatedAt};
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
        if(score) hits.push({path:file.path,language:file.language,score,matches:[...new Set(matches)].slice(0,12),symbols:(file.symbols||[]).filter(symbol=>terms.some(term=>symbol.toLowerCase().includes(term))).slice(0,20),symbolDetails:(file.symbolDetails||[]).filter(symbol=>terms.some(term=>symbol.name.toLowerCase().includes(term))).slice(0,20),analysisEngine:file.analysisEngine||'regex-fallback',parseError:file.parseError||null,imports:(file.imports||[]).slice(0,20)});
      }
      hits.sort((a,b)=>b.score-a.score||a.path.localeCompare(b.path));
      return {query,kind,indexUpdatedAt:index.updatedAt,totalMatches:hits.length,results:hits.slice(0,Math.max(1,Math.min(100,limit))),note:'The persistent index is refreshed automatically when a workspace is selected and periodically while the chat is open; manual refresh remains available.'};
    }
    async function getProjectIndexOverview() {
      const index=await loadProjectIndex();
      if(!index) return {indexed:false,message:'No project index has been built.'};
      return {indexed:true,root:index.rootName,updatedAt:index.updatedAt,stats:index.stats,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies,toolchains:index.toolchains||null,analysis:{acornFiles:index.files.filter(item=>item.analysisEngine==='acorn').length,parseErrors:index.files.filter(item=>item.parseError).length,regexFallbackFiles:index.files.filter(item=>item.analysisEngine==='regex-fallback').length},importGraph:index.importGraph?{localImportEdges:index.importGraph.localImportEdges.length,filesWithIncomingImports:index.importGraph.filesWithIncomingImports,note:index.importGraph.note}:null,manifests:index.manifests.map(item=>item.path),topLevel:index.files.filter(item=>!item.path.includes('/')).map(item=>item.path).slice(0,100)};
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
