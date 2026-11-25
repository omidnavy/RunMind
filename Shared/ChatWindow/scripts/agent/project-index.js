    'use strict';

    const PROJECT_INDEX_PREFIX = 'runmind.project-index.v1.';
    const PROJECT_INDEX_LIMITS = Object.freeze({ files: 10000, entries: 50000, depth: 10, fileBytes: 256 * 1024, totalBytes: 24 * 1024 * 1024 });
    const PROJECT_INDEX_SKIP_DIRS = new Set(['.git','.hg','.svn','.runmind-backups','.cache','node_modules','vendor','bin','obj','dist','build','out','target','.next','.nuxt','.venv','venv','__pycache__','coverage','.idea','.vs','.gradle','.terraform','.npm','.nuget','.cargo','.docker','.pypoetry','.conda','secret','secrets','credential','credentials','.ssh','.aws','.azure','.gnupg']);
    const PROJECT_INDEX_SKIP_FILES = /^(?:\.env(?:\..*)?|(?:.*\.(?:pem|key|p12|pfx|keystore|jks|kdbx|sqlite|db|wasm|dll|exe|so|dylib|bin|lockb)|.*(?:secret|credential).*)|id_(?:rsa|ed25519)|credentials?(?:\..*)?|secrets?(?:\..*)?|\.npmrc|\.pypirc|\.netrc|\.git-credentials|nuget\.config|pip\.conf|\.dockercfg|known_hosts)$/i;
    const PROJECT_INDEX_TEXT_EXTENSIONS = new Set(['.md','.mdx','.txt','.rst','.adoc','.json','.jsonc','.json5','.yaml','.yml','.toml','.ini','.cfg','.conf','.xml','.html','.htm','.css','.scss','.less','.js','.mjs','.cjs','.jsx','.ts','.tsx','.vue','.svelte','.py','.pyi','.cs','.java','.kt','.kts','.go','.rs','.c','.h','.cc','.cpp','.hpp','.hh','.php','.rb','.swift','.sh','.bash','.zsh','.ps1','.bat','.cmd','.sql','.graphql','.gql','.proto','.dockerfile','.makefile','.gradle','.properties']);

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
    function projectIndexExtract(text, language) {
      const imports = [], symbols = [];
      const addMatches = (regex, target, max=120) => { let match; while ((match=regex.exec(text)) && target.length<max) { const value=match.slice(1).filter(Boolean).at(-1)?.trim() || ''; if(value && !target.includes(value)) target.push(value); } };
      if (['javascript','typescript'].includes(language)) {
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
      return { imports: imports.slice(0,120), symbols: symbols.slice(0,120) };
    }
    async function loadProjectIndex() {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace first.');
      const value = await workspaceDbAction('readonly','get',undefined,projectIndexKey());
      return value && value.version===1 && value.contextId===workspaceContext.id ? value : null;
    }
    async function buildProjectIndex({ force=false, signal=null, onProgress=null }={}) {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace first.');
      const previous = force ? null : await loadProjectIndex();
      const oldFiles = new Map((previous?.files || []).map(file => [file.path,file]));
      const files = [], manifests = [], technologies = new Set(), visitedPaths = new Set();
      let entries=0, scanned=0, readBytes=0, reused=0, skipped=0, truncated=false, reason='';
      const stop = value => { truncated=true; if(!reason) reason=value; };
      const manifestNames = new Set(['package.json','pyproject.toml','requirements.txt','setup.py','cargo.toml','go.mod','pom.xml','build.gradle','build.gradle.kts','*.csproj','*.sln','composer.json','gemfile','dockerfile','makefile']);
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
            if(prior && prior.size===file.size && prior.lastModified===file.lastModified && typeof prior.text==='string') {
              files.push(prior); reused++;
              if(isManifest) manifests.push({path,text:prior.text.slice(0,16000)});
              if(prior.language && prior.language!=='text') technologies.add(prior.language);
              continue;
            }
            if(readBytes+file.size>PROJECT_INDEX_LIMITS.totalBytes){stop('aggregate text budget reached');return;}
            const source=await file.text(); readBytes+=file.size;
            const text=redactProjectIndexSecrets(source);
            const language=projectIndexLanguage(path), extracted=projectIndexExtract(text,language);
            const record={path,size:file.size,lastModified:file.lastModified,language,imports:extracted.imports,symbols:extracted.symbols,text:text.slice(0,Math.min(text.length,160000)).toLowerCase()};
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
      const index={version:1,contextId:workspaceContext.id,rootName:agentDirectoryHandle.name||workspaceContext.folderName||'workspace',createdAt:previous?.createdAt||new Date().toISOString(),updatedAt:new Date().toISOString(),files,manifests:manifests.map(({path,text})=>({path,text})),technologies:[...technologies].sort(),frameworks,dependencies,stats:{files:files.length,scanned,entries,readBytes,reused,removed,skipped,truncated,reason}};
      await workspaceDbAction('readwrite','put',index,projectIndexKey());
      projectIndexStatus?.(index);
      return {root:index.rootName,files:index.stats.files,scanned,entries,readBytes,reused,removed,skipped,truncated,reason,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies.slice(0,100),updatedAt:index.updatedAt};
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
        if(score) hits.push({path:file.path,language:file.language,score,matches:[...new Set(matches)].slice(0,12),symbols:(file.symbols||[]).filter(symbol=>terms.some(term=>symbol.toLowerCase().includes(term))).slice(0,20),imports:(file.imports||[]).slice(0,20)});
      }
      hits.sort((a,b)=>b.score-a.score||a.path.localeCompare(b.path));
      return {query,kind,indexUpdatedAt:index.updatedAt,totalMatches:hits.length,results:hits.slice(0,Math.max(1,Math.min(100,limit))),note:'The persistent index is refreshed automatically when a workspace is selected and periodically while the chat is open; manual refresh remains available.'};
    }
    async function getProjectIndexOverview() {
      const index=await loadProjectIndex();
      if(!index) return {indexed:false,message:'No project index has been built.'};
      return {indexed:true,root:index.rootName,updatedAt:index.updatedAt,stats:index.stats,technologies:index.technologies,frameworks:index.frameworks,dependencies:index.dependencies,manifests:index.manifests.map(item=>item.path),topLevel:index.files.filter(item=>!item.path.includes('/')).map(item=>item.path).slice(0,100)};
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
