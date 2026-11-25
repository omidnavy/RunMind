    'use strict';

    function xmlElementsByName(documentNode, localName) { return [...documentNode.getElementsByTagName('*')].filter(node => String(node.localName || node.nodeName).toLowerCase() === String(localName).toLowerCase()); }
    function xmlChildText(node, localNames) {
      const wanted = new Set(localNames.map(value => value.toLowerCase()));
      const found = [...node.getElementsByTagName('*')].find(child => wanted.has(String(child.localName || child.nodeName).toLowerCase()));
      return found?.textContent?.trim() || '';
    }
    function readerMarkdownToDocument(page) {
      const lines = String(page.content || '').split(/\r?\n/);
      const output = [];
      let inList = false;
      let inTable = false;
      const closeList = () => { if (inList) { output.push('</ul>'); inList = false; } };
      const closeTable = () => { if (inTable) { output.push('</tbody></table>'); inTable = false; } };
      for (const rawLine of lines) {
        const line = rawLine.trim();
        if (!line) { closeList(); closeTable(); continue; }
        if (/^\|.*\|$/.test(line)) {
          closeList();
          const cells = line.replace(/^\||\|$/g, '').split('|').map(cell => cell.trim());
          if (cells.every(cell => /^:?-{2,}:?$/.test(cell))) continue;
          if (!inTable) { output.push('<table><tbody>'); inTable = true; }
          output.push(`<tr>${cells.map(cell => `<td>${escapeHtml(cell).replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2">$1</a>')}</td>`).join('')}</tr>`);
          continue;
        }
        closeTable();
        const heading = line.match(/^(#{1,6})\s+(.+)$/);
        if (heading) { closeList(); const level = heading[1].length; output.push(`<h${level}>${escapeHtml(heading[2])}</h${level}>`); continue; }
        if (/^[-*+]\s+/.test(line)) {
          if (!inList) { output.push('<ul>'); inList = true; }
          output.push(`<li>${escapeHtml(line.replace(/^[-*+]\s+/, ''))}</li>`); continue;
        }
        closeList();
        const linked = escapeHtml(line).replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2">$1</a>');
        output.push(`<p>${linked}</p>`);
      }
      closeList(); closeTable();
      const document = new DOMParser().parseFromString(`<html lang=""><head><title>${escapeHtml(page.title || '')}</title><meta name="description" content="${escapeHtml(page.description || '')}"></head><body>${output.join('')}</body></html>`, 'text/html');
      return document;
    }
    function isReadableWebContentType(contentType) {
      const type = String(contentType || '').split(';', 1)[0].trim().toLowerCase();
      if (!type) return true;
      return type.startsWith('text/') || /^(?:application\/(?:json|xml|xhtml\+xml|javascript|x-javascript|yaml|x-yaml|csv|markdown|ld\+json)|application\/[a-z0-9.+-]+\+(?:json|xml))$/.test(type);
    }
    async function fetchWebDocument(urlValue, signal, accept = 'text/html,application/xhtml+xml,application/json,text/plain,*/*') {
      let url;
      try { url = new URL(String(urlValue || '').trim()); } catch (_) { throw new Error('Provide a valid absolute HTTP or HTTPS URL.'); }
      if (!['http:', 'https:'].includes(url.protocol)) throw new Error('Only HTTP and HTTPS URLs are supported.');
      if (url.username || url.password) throw new Error('URLs containing embedded credentials are not supported.');
      const originalUrl = new URL(url.href);
      const secureUrl = new URL(url.href);
      if (secureUrl.protocol === 'http:') secureUrl.protocol = 'https:';
      const candidates = secureUrl.href === originalUrl.href ? [secureUrl] : [secureUrl, originalUrl];
      let directError = null;

      for (const candidate of candidates) {
        try {
          const response = await fetch(candidate.href, { signal, headers: { Accept: accept } });
          if (!response.ok) {
            try { await response.body?.cancel(); } catch (_) {}
            throw new Error(`HTTP ${response.status} while fetching ${candidate.href}`);
          }
          const finalUrl = response.url || candidate.href;
          const type = response.headers.get('content-type') || '';
          if (!isReadableWebContentType(type))
            throw new Error(`Browser fetch returned a non-text resource (${type || 'unknown content type'}); use download_file for original binary resources.`);
          if (!isResearchDomainAllowed(finalUrl)) {
            const blocked = new Error(`The page redirected to a domain blocked by the current Deep Research policy: ${new URL(finalUrl).hostname}`);
            blocked.code = 'RESEARCH_DOMAIN_BLOCKED';
            throw blocked;
          }
          const text = await readBoundedResponseText(response, 2 * 1024 * 1024);
          const document = new DOMParser().parseFromString(text, /xml/i.test(type) || /^\s*<\?xml/i.test(text) ? 'application/xml' : 'text/html');
          if (document.querySelector('parsererror')) throw new Error('The response could not be parsed as a document.');
          const structured = /html|xhtml/i.test(type) ? extractStructuredPageMetadata(document) : {};
          const meta = (...names) => {
            for (const name of names) {
              const item = document.querySelector(`meta[property="${name}" i]`) || document.querySelector(`meta[name="${name}" i]`);
              if (item?.content) return item.content.trim();
            }
            return '';
          };
          return {
            url: finalUrl, type, text, document, title: document.title || '', retrieval: 'Direct browser fetch',
            description: meta('description', 'og:description'),
            author: meta('author', 'article:author') || structured.author || '',
            publishedAt: meta('article:published_time', 'datePublished', 'date', 'pubdate') || structured.publishedAt || '',
            modifiedAt: meta('article:modified_time', 'dateModified', 'last-modified') || structured.modifiedAt || ''
          };
        } catch (error) {
          if (signal?.aborted || error?.name === 'AbortError') throw new DOMException('Document fetch cancelled.', 'AbortError');
          if (error?.code === 'RESEARCH_DOMAIN_BLOCKED') throw error;
          directError = error;
        }
      }

      if (/application\/xml|text\/xml|rss\+xml|atom\+xml/i.test(accept)) {
        throw new Error(`Direct XML browser fetch failed (${directError?.message || directError}). Reader fallback returns readable Markdown, not raw XML; this sitemap/feed cannot be parsed reliably.`);
      }
      const page = await fetchReadableWebPage(originalUrl.href, signal, { skipDirect: true, directError });
      const document = readerMarkdownToDocument(page);
      return {
        url: page.url || url.href, type: 'text/markdown', text: page.content, document, title: page.title || '',
        description: page.description || '', author: page.author || '', publishedAt: page.publishedAt || '',
        modifiedAt: page.modifiedAt || '', retrieval: page.retrieval, directFetchFailure: page.directFetchFailure || String(directError?.message || directError).slice(0, 700)
      };
    }

    function isPrivateOrLocalHost(hostname) {
      const host = String(hostname || '').toLowerCase().replace(/^\[|\]$/g, '').replace(/\.+$/, '');
      if (!host || !host.includes('.') || host.endsWith('.onion') || host === 'localhost' || host.endsWith('.localhost') || host.endsWith('.local') || host.endsWith('.internal') || host.endsWith('.lan') || host.endsWith('.home') || host.endsWith('.home.arpa') || host.endsWith('.corp') || host.endsWith('.test') || host.endsWith('.example') || host.endsWith('.invalid')) return true;
      // Do not forward private/loopback/link-local IPv4 destinations to a third-party reader.
      const parts = host.split('.');
      if (parts.length === 4 && parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255)) {
        const [a,b,c] = parts.map(Number);
        return a === 0 || a === 10 || a === 127 || (a === 169 && b === 254) || (a === 172 && b >= 16 && b <= 31)
          || (a === 192 && (b === 168 || (b === 0 && (c === 0 || c === 2)) || (b === 88 && c === 99)))
          || (a === 198 && (b === 18 || b === 19 || (b === 51 && c === 100)))
          || (a === 203 && b === 0 && c === 113) || (a === 100 && b >= 64 && b <= 127) || a >= 224;
      }
      // IPv6 literals are conservatively excluded from remote fallback.
      if (host.includes(':')) return true;
      return false;
    }
    async function requestReaderEndpoint(baseUrl, target, apiKey, signal, providerLabel) {
      const base = normalizeReaderEndpoint(baseUrl, false);
      if (!base) throw new Error(`${providerLabel} endpoint URL is invalid.`);
      // Jina Reader's documented GET form is https://r.jina.ai/{target-url}.
      // Reader-compatible custom gateways use the same path contract.
      const requestUrl = `${base}/${target.href}`;
      const headers = { Accept: 'application/json' };
      if (apiKey) headers.Authorization = `Bearer ${apiKey}`;
      let response;
      try {
        response = await fetch(requestUrl, { method: 'GET', signal, headers, redirect: 'error' });
      } catch (error) {
        if (signal?.aborted) throw new DOMException('Page fetch cancelled.', 'AbortError');
        throw new Error(`${providerLabel} connection failed: ${error?.message || String(error)}`);
      }
      const raw = await readBoundedResponseText(response, 2 * 1024 * 1024);
      if (!response.ok) {
        let detail = raw.slice(0, 350);
        try { const parsed = JSON.parse(raw); detail = parsed?.detail || parsed?.error?.message || parsed?.message || detail; } catch (_) {}
        throw new Error(`${providerLabel} returned HTTP ${response.status}${detail ? `: ${detail}` : ''}`);
      }
      if (raw.length > 2000000) throw new Error(`${providerLabel} returned more than the 2 MB response limit.`);
      let payload = null;
      const contentType = response.headers.get('content-type') || '';
      if (/json/i.test(contentType) || /^[\s]*[\[{]/.test(raw)) {
        try { payload = JSON.parse(raw); } catch (_) { payload = null; }
      }
      const data = payload?.data;
      const content = String(
        typeof data === 'string' ? data :
        data?.content || data?.text || payload?.content || payload?.text ||
        (typeof payload === 'string' ? payload : '') ||
        (!payload ? raw : '')
      ).trim();
      if (!content) throw new Error(`${providerLabel} returned no readable page content.`);
      const metadata = data && typeof data === 'object' ? data : payload?.metadata || {};
      const nestedMetadata = metadata?.metadata || payload?.metadata || {};
      return {
        url: metadata?.url || target.href,
        title: metadata?.title || nestedMetadata?.title || target.hostname,
        description: metadata?.description || nestedMetadata?.description || '',
        author: metadata?.author || nestedMetadata?.author || '',
        publishedAt: metadata?.publishedAt || metadata?.published_at || metadata?.published || metadata?.date || nestedMetadata?.publishedAt || nestedMetadata?.published_at || nestedMetadata?.published || nestedMetadata?.date || '',
        modifiedAt: metadata?.modifiedAt || metadata?.modified_at || nestedMetadata?.modifiedAt || nestedMetadata?.modified_at || '',
        contentType: 'text/markdown',
        content: content.slice(0, 90000),
        retrieval: providerLabel,
      };
    }
    async function fetchWithReaderFallback(target, signal, directError) {
      if (loadAgentSettings().readerFallback === false) {
        throw new Error(`Local page retrieval failed (${directError?.message || String(directError)}). Remote Reader fallback is disabled in Agent settings.`);
      }
      const sensitiveQueryKey = [...target.searchParams.keys()].some(key => /(?:token|api[_-]?key|secret|password|passwd|auth|session|signature|access[_-]?key|access[_-]?token|client[_-]?secret|code)/i.test(key));
      if (isPrivateOrLocalHost(target.hostname) || target.username || target.password || sensitiveQueryKey) {
        throw new Error(`Local page retrieval failed (${directError?.message || String(directError)}). Remote Reader fallback was not attempted because this is a private/local URL or contains credentials/sensitive query parameters.`);
      }
      const settings = loadAgentSettings();
      const credentials = loadReaderCredentials();
      const attempts = [];
      console.info('[RunMind Reader] Local retrieval failed; using the configured remote fallback.', {
        host: target.hostname,
        localRetrievalError: String(directError?.message || directError || 'No local result').slice(0, 500),
        customReaderConfigured: !!normalizeReaderEndpoint(settings.customReaderUrl || '', true),
        jinaEndpoint: normalizeReaderEndpoint(settings.readerBaseUrl || DEFAULT_JINA_READER_URL, false)
      });
      const customBase = normalizeReaderEndpoint(settings.customReaderUrl || '', true);
      if (customBase) {
        const customLabel = `Custom Reader gateway (${new URL(customBase).host})`;
        const customStartedAt = Date.now();
        console.info('[RunMind Reader] Trying custom Reader fallback.', {
          host: target.hostname,
          reader: new URL(customBase).host,
          reason: String(directError?.message || directError || 'Direct browser fetch returned no readable content').slice(0, 350)
        });
        try {
          const page = await requestReaderEndpoint(customBase, target, credentials.customGatewayToken || '', signal, customLabel);
          console.info('[RunMind Reader] Custom Reader fallback succeeded.', {
            host: target.hostname,
            reader: new URL(customBase).host,
            elapsedMs: Math.max(0, Date.now() - customStartedAt),
            contentCharacters: String(page.content || '').length
          });
          return page;
        } catch (error) {
          if (signal?.aborted || error?.name === 'AbortError') throw error;
          attempts.push(error?.message || String(error));
          console.warn('[RunMind Reader] Custom Reader fallback failed; continuing to Jina.', {
            host: target.hostname,
            reader: new URL(customBase).host,
            elapsedMs: Math.max(0, Date.now() - customStartedAt),
            reason: String(error?.message || error).slice(0, 500)
          });
        }
      }
      const readerBase = normalizeReaderEndpoint(settings.readerBaseUrl || DEFAULT_JINA_READER_URL, false);
      const jinaLabel = `Jina Reader (${new URL(readerBase).host})`;
      const jinaStartedAt = Date.now();
      console.info('[RunMind Reader] Trying Jina Reader fallback.', {
        host: target.hostname,
        reader: new URL(readerBase).host,
        customReaderConfigured: !!customBase,
        reason: String(directError?.message || directError || 'Local Reader returned no readable content').slice(0, 350)
      });
      try {
        const page = await requestReaderEndpoint(readerBase, target, credentials.jinaApiKey || '', signal, jinaLabel);
        console.info('[RunMind Reader] Jina Reader fallback succeeded.', {
          host: target.hostname,
          reader: new URL(readerBase).host,
          elapsedMs: Math.max(0, Date.now() - jinaStartedAt),
          contentCharacters: String(page.content || '').length
        });
        return page;
      } catch (error) {
        if (signal?.aborted || error?.name === 'AbortError') throw error;
        attempts.push(error?.message || String(error));
        console.warn('[RunMind Reader] Jina Reader fallback failed.', {
          host: target.hostname,
          reader: new URL(readerBase).host,
          elapsedMs: Math.max(0, Date.now() - jinaStartedAt),
          reason: String(error?.message || error).slice(0, 500)
        });
      }
      throw new Error(`Local page retrieval failed (${directError?.message || String(directError)}). Reader attempts failed: ${attempts.join(' | ')}`);
    }
    async function testReaderEndpoint(baseUrl, apiKey, providerLabel) {
      const target = new URL('https://example.com/');
      return await requestReaderEndpoint(baseUrl, target, apiKey || '', undefined, providerLabel);
    }
    function extractStructuredPageMetadata(doc) {
      const result = { author: '', publishedAt: '', modifiedAt: '' };
      const candidates = [];
      for (const script of doc.querySelectorAll('script[type="application/ld+json"]')) {
        try {
          const value = JSON.parse(script.textContent || 'null');
          const visit = item => {
            if (!item || typeof item !== 'object') return;
            if (Array.isArray(item)) { item.forEach(visit); return; }
            candidates.push(item);
            visit(item['@graph']);
            visit(item.mainEntity);
            visit(item.mainEntityOfPage);
          };
          visit(value);
        } catch (_) { /* Ignore malformed structured metadata. */ }
      }
      const pick = (...keys) => {
        for (const item of candidates) for (const key of keys) {
          const value = item[key];
          if (typeof value === 'string' && value.trim()) return value.trim();
          if (value && typeof value === 'object') {
            const name = Array.isArray(value) ? value.map(part => part?.name).filter(Boolean).join(', ') : value.name;
            if (typeof name === 'string' && name.trim()) return name.trim();
          }
        }
        return '';
      };
      result.author = pick('author', 'creator');
      result.publishedAt = pick('datePublished', 'dateCreated', 'uploadDate');
      result.modifiedAt = pick('dateModified');
      return result;
    }
    function htmlDocumentToMarkdown(doc, baseUrl) {
      const root = doc.querySelector('main, article, [role="main"]') || doc.body || doc.documentElement;
      const ignored = new Set(['SCRIPT','STYLE','NOSCRIPT','SVG','NAV','FOOTER','FORM','TEMPLATE','IFRAME','BUTTON']);
      const inline = node => {
        if (!node) return '';
        if (node.nodeType === Node.TEXT_NODE) return String(node.nodeValue || '').replace(/\s+/g, ' ');
        if (node.nodeType !== Node.ELEMENT_NODE) return '';
        const tag = node.tagName;
        if (ignored.has(tag) || node.hidden || node.getAttribute('aria-hidden') === 'true') return '';
        if (tag === 'BR') return '\n';
        if (tag === 'IMG') {
          const alt = String(node.getAttribute('alt') || '').trim();
          const raw = node.getAttribute('src');
          if (!raw) return alt;
          try { const url = new URL(raw, baseUrl); return /^https?:$/.test(url.protocol) ? `![${alt}](${url.href})` : alt; } catch (_) { return alt; }
        }
        const children = [...node.childNodes].map(inline).join('');
        if (tag === 'A') {
          const raw = node.getAttribute('href');
          if (!raw) return children;
          try { const url = new URL(raw, baseUrl); return /^https?:$/.test(url.protocol) ? `[${children.trim() || url.hostname}](${url.href})` : children; } catch (_) { return children; }
        }
        if (['STRONG','B'].includes(tag)) return children.trim() ? `**${children.trim()}**` : '';
        if (['EM','I'].includes(tag)) return children.trim() ? `*${children.trim()}*` : '';
        if (tag === 'CODE' && node.parentElement?.tagName !== 'PRE') return `\`${node.textContent || ''}\``;
        return children;
      };
      const blocks = [];
      const block = node => {
        if (!node || node.nodeType !== Node.ELEMENT_NODE || ignored.has(node.tagName) || node.hidden || node.getAttribute('aria-hidden') === 'true') return;
        const tag = node.tagName;
        if (/^H[1-6]$/.test(tag)) { const level = Number(tag[1]); const text = inline(node).trim(); if (text) blocks.push(`${'#'.repeat(level)} ${text}`); return; }
        if (tag === 'P') { const text = inline(node).trim(); if (text) blocks.push(text); return; }
        if (tag === 'PRE') { const text = String(node.textContent || '').replace(/\s+$/,''); if (text) blocks.push(`\`\`\`\n${text}\n\`\`\``); return; }
        if (tag === 'BLOCKQUOTE') { const text = inline(node).trim(); if (text) blocks.push(text.split('\n').map(line => `> ${line}`).join('\n')); return; }
        if (tag === 'UL' || tag === 'OL') {
          const items = [...node.children].filter(child => child.tagName === 'LI').map((item,index) => `${tag === 'OL' ? `${index+1}.` : '-'} ${inline(item).trim()}`).filter(item => item.length > 2);
          if (items.length) blocks.push(items.join('\n')); return;
        }
        if (tag === 'TABLE') {
          const rows = [...node.querySelectorAll('tr')].slice(0, 80).map(row => [...row.querySelectorAll(':scope > th, :scope > td')].slice(0, 16).map(cell => inline(cell).trim().replace(/\|/g,'\\|'))).filter(row => row.length);
          if (rows.length) {
            const width = Math.max(...rows.map(row => row.length));
            const normalized = rows.map(row => Array.from({length:width},(_,index)=>row[index] || ''));
            blocks.push([`| ${normalized[0].join(' | ')} |`, `| ${normalized[0].map(()=> '---').join(' | ')} |`, ...normalized.slice(1).map(row=>`| ${row.join(' | ')} |`)].join('\n'));
          }
          return;
        }
        for (const child of node.children) block(child);
      };
      block(root);
      const markdown = blocks.join('\n\n').replace(/\n{3,}/g,'\n\n').trim();
      return markdown || String(root.innerText || root.textContent || '').replace(/\n{3,}/g,'\n\n').trim();
    }

    const agentHostPendingRequests = new Map();
    function handleAgentHostMessage(event) {
      const result = event?.data;
      if (!result || typeof result.requestId !== 'string') return;
      const pending = agentHostPendingRequests.get(result.requestId);
      if (!pending) return;
      if (result.type === 'runmind.agentHostProgress') {
        try { pending.onProgress?.({ bytes: Number(result.bytes) || 0, total: result.total == null ? null : (Number.isFinite(Number(result.total)) ? Number(result.total) : null), message: String(result.message || '') }); } catch (_) {}
        return;
      }
      if (result.type !== 'runmind.agentHostResult') return;
      agentHostPendingRequests.delete(result.requestId);
      clearTimeout(pending.timeout);
      pending.signal?.removeEventListener('abort', pending.onAbort);
      if (result.ok) pending.resolve(result.result);
      else pending.reject(new Error(String(result.error || 'Native Agent operation failed.')));
    }
    window.chrome?.webview?.addEventListener('message', handleAgentHostMessage);
    function callAgentHost(operation, args = {}, signal, timeoutMs = 2 * 60 * 1000, onProgress = null) {
      const bridge = window.chrome?.webview;
      if (!bridge || typeof bridge.postMessage !== 'function') return Promise.reject(new Error('This native Agent capability is unavailable in the browser-only preview.'));
      if (signal?.aborted) return Promise.reject(new DOMException('Native operation cancelled.', 'AbortError'));
      const requestId = `agent-host-${Date.now()}-${crypto.randomUUID()}`;
      return new Promise((resolve, reject) => {
        const finish = (callback, value) => {
          const pending = agentHostPendingRequests.get(requestId);
          if (!pending) return;
          agentHostPendingRequests.delete(requestId);
          clearTimeout(pending.timeout);
          signal?.removeEventListener('abort', pending.onAbort);
          callback(value);
        };
        const onAbort = () => {
          try { bridge.postMessage({ type: 'runmind.agentHostCancel', requestId }); } catch (_) {}
          finish(reject, new DOMException('Native operation cancelled.', 'AbortError'));
        };
        const timeout = setTimeout(() => {
          try { bridge.postMessage({ type: 'runmind.agentHostCancel', requestId }); } catch (_) {}
          finish(reject, new Error('Native Agent operation timed out.'));
        }, timeoutMs);
        agentHostPendingRequests.set(requestId, { resolve, reject, timeout, signal, onAbort, onProgress });
        signal?.addEventListener('abort', onAbort, { once: true });
        if (signal?.aborted) return onAbort();
        try { bridge.postMessage({ type: 'runmind.agentHostRequest', requestId, operation, args }); }
        catch (error) { finish(reject, error); }
      });
    }
    const nativeReaderPendingRequests = new Map();
    function handleNativeReaderMessage(event) {
      const result = event?.data;
      if (!result || result.type !== 'runmind.nativeReaderResult' || typeof result.requestId !== 'string') return;
      const pending = nativeReaderPendingRequests.get(result.requestId);
      if (!pending) return;
      nativeReaderPendingRequests.delete(result.requestId);
      clearTimeout(pending.timeout);
      pending.signal?.removeEventListener('abort', pending.onAbort);
      if (!result.ok) pending.reject(new Error(String(result.error || 'Native Reader could not retrieve this page.')));
      else pending.resolve(result);
    }
    window.chrome?.webview?.addEventListener('message', handleNativeReaderMessage);
    function readPageWithNativeReader(url, signal) {
      const bridge = window.chrome?.webview;
      if (!bridge || typeof bridge.postMessage !== 'function') return Promise.reject(new Error('The native Reader bridge is unavailable in this browser-only preview.'));
      if (signal?.aborted) return Promise.reject(new DOMException('Page fetch cancelled.', 'AbortError'));
      const requestId = `reader-${Date.now()}-${Math.random().toString(36).slice(2, 12)}`;
      return new Promise((resolve, reject) => {
        const finish = (callback, value) => {
          const pending = nativeReaderPendingRequests.get(requestId);
          if (!pending) return;
          nativeReaderPendingRequests.delete(requestId);
          clearTimeout(pending.timeout);
          signal?.removeEventListener('abort', pending.onAbort);
          callback(value);
        };
        const onAbort = () => { try { bridge.postMessage({ type: 'runmind.nativeReaderCancel', requestId }); } catch (_) {} finish(reject, new DOMException('Page fetch cancelled.', 'AbortError')); };
        const timeout = setTimeout(() => { try { bridge.postMessage({ type: 'runmind.nativeReaderCancel', requestId }); } catch (_) {} finish(reject, new Error('Native Reader did not respond within 30 seconds.')); }, 30000);
        nativeReaderPendingRequests.set(requestId, { resolve, reject, timeout, signal, onAbort, url: String(url || '') });
        signal?.addEventListener('abort', onAbort, { once: true });
        if (signal?.aborted) return onAbort();
        try { bridge.postMessage({ type: 'runmind.nativeReaderRequest', requestId, url }); }
        catch (error) { finish(reject, new Error(`Could not contact the native Reader: ${error?.message || error}`)); }
      });
    }
    function convertFetchedPageToReadable(raw, contentType, finalUrl, retrieval) {
      let content = String(raw || '');
      let title = new URL(finalUrl).hostname;
      let description = '', author = '', publishedAt = '', modifiedAt = '';
      if (!isReadableWebContentType(contentType))
        throw new Error(`Browser fetch received a non-text resource (${contentType || 'unknown content type'}); trying the configured Reader fallback.`);
      if (/json/i.test(contentType)) {
        try { content = JSON.stringify(JSON.parse(content), null, 2); } catch (_) { /* Keep readable malformed/plain JSON as-is. */ }
      } else if (/html|xhtml/i.test(contentType) || /<html[\s>]/i.test(content.slice(0, 1000))) {
        const doc = new DOMParser().parseFromString(content, 'text/html');
        const meta = (...names) => {
          for (const name of names) {
            const found = doc.querySelector(`meta[property="${name}" i]`) || doc.querySelector(`meta[name="${name}" i]`);
            if (found?.content) return found.content.trim();
          }
          return '';
        };
        const structured = extractStructuredPageMetadata(doc);
        title = meta('og:title', 'twitter:title') || doc.title || title;
        description = meta('description', 'og:description');
        author = meta('author', 'article:author') || structured.author;
        publishedAt = meta('article:published_time', 'datePublished', 'date', 'publish-date', 'pubdate') || structured.publishedAt;
        modifiedAt = meta('article:modified_time', 'dateModified', 'last-modified') || structured.modifiedAt;
        doc.querySelectorAll('script,style,noscript,svg,nav,footer,form,template,iframe').forEach(node => node.remove());
        content = htmlDocumentToMarkdown(doc, finalUrl);
      }
      const readableContent = String(content || '').trim();
      if (!readableContent) throw new Error('The page response contained no readable text.');
      if (/^(?:enable javascript|javascript is required|checking your browser|just a moment\.\.\.|verify you are human)[.! ]*$/i.test(readableContent))
        throw new Error('The page returned a JavaScript-only shell or browser-verification interstitial.');
      return {
        url: finalUrl, title, description, author, publishedAt, modifiedAt,
        contentType: contentType || 'text/plain', content: String(content).slice(0, 90000), retrieval,
      };
    }
    async function fetchReadableWebPage(urlValue, signal, options = {}) {
      let target;
      try { target = new URL(String(urlValue || '').trim()); } catch (_) { throw new Error('Provide a valid absolute URL.'); }
      if (!['http:', 'https:'].includes(target.protocol)) throw new Error('Only HTTP and HTTPS URLs can be fetched.');
      if (target.username || target.password) throw new Error('URLs containing embedded credentials are not supported.');
      const originalTarget = new URL(target.href);
      if (target.protocol === 'http:') target.protocol = 'https:';
      let directError = options.skipDirect ? (options.directError || new Error('Direct browser fetch was already attempted.')) : null;

      if (!options.skipDirect) {
        if (window.chrome?.webview && typeof window.chrome.webview.postMessage === 'function') {
          try {
            const nativePage = await readPageWithNativeReader(target.href, signal);
            if (!isResearchDomainAllowed(nativePage.url || target.href)) {
              const blocked = new Error(`The Native Reader resolved the URL to a domain blocked by the current Deep Research policy: ${new URL(nativePage.url || target.href).hostname}`);
              blocked.code = 'RESEARCH_DOMAIN_BLOCKED';
              throw blocked;
            }
            return convertFetchedPageToReadable(nativePage.content, nativePage.contentType, nativePage.url || target.href, 'Native C# Reader');
          } catch (error) {
            if (signal?.aborted || error?.name === 'AbortError') throw new DOMException('Page fetch cancelled.', 'AbortError');
            if (error?.code === 'RESEARCH_DOMAIN_BLOCKED') throw error;
            directError = new Error(`Native Reader failed: ${error?.message || error}`);
          }
        }
        const candidates = target.href === originalTarget.href ? [target] : [target, originalTarget];
        for (const candidate of candidates) {
          try {
            if (signal?.aborted) throw new DOMException('Page fetch cancelled.', 'AbortError');
            const response = await fetch(candidate.href, { signal, headers: { Accept: 'text/html,application/xhtml+xml,application/json,text/plain,*/*' } });
            if (!response.ok) {
              try { await response.body?.cancel(); } catch (_) {}
              throw new Error(`Page returned HTTP ${response.status}.`);
            }
            const finalUrl = response.url || candidate.href;
            const contentType = response.headers.get('content-type') || '';
            const raw = await readBoundedResponseText(response, 2 * 1024 * 1024);
            if (!isResearchDomainAllowed(finalUrl)) {
              const blocked = new Error(`The page redirected to a domain blocked by the current Deep Research policy: ${new URL(finalUrl).hostname}`);
              blocked.code = 'RESEARCH_DOMAIN_BLOCKED';
              throw blocked;
            }
            return convertFetchedPageToReadable(raw, contentType, finalUrl, 'Direct browser fetch');
          } catch (error) {
            if (signal?.aborted || error?.name === 'AbortError') throw new DOMException('Page fetch cancelled.', 'AbortError');
            if (error?.code === 'RESEARCH_DOMAIN_BLOCKED') throw error;
            directError = directError ? new Error(`${directError.message || directError}; ${error?.message || error}`) : error;
          }
        }
      }

      try {
        const page = await fetchWithReaderFallback(target, signal, directError);
        if (!isResearchDomainAllowed(page.url || target.href)) {
          const blocked = new Error(`The Reader resolved the URL to a domain blocked by the current Deep Research policy: ${new URL(page.url || target.href).hostname}`);
          blocked.code = 'RESEARCH_DOMAIN_BLOCKED';
          throw blocked;
        }
        page.directFetchFailure = String(directError?.message || directError || 'Direct browser fetch did not return readable content').slice(0, 900);
        return page;
      } catch (readerError) {
        if (readerError?.code === 'RESEARCH_DOMAIN_BLOCKED') throw readerError;
        if (originalTarget.href !== target.href && !signal?.aborted) {
          try {
            const page = await fetchWithReaderFallback(originalTarget, signal, new Error(`${directError?.message || 'Direct browser fetch failed'}; secure Reader attempt: ${readerError?.message || readerError}`));
            if (!isResearchDomainAllowed(page.url || originalTarget.href)) {
              const blocked = new Error(`The Reader resolved the URL to a domain blocked by the current Deep Research policy: ${new URL(page.url || originalTarget.href).hostname}`);
              blocked.code = 'RESEARCH_DOMAIN_BLOCKED';
              throw blocked;
            }
            page.directFetchFailure = String(directError?.message || directError || 'Direct browser fetch did not return readable content').slice(0, 900);
            return page;
          } catch (originalReaderError) {
            if (originalReaderError?.code === 'RESEARCH_DOMAIN_BLOCKED') throw originalReaderError;
            throw new Error(`Direct browser fetch failed: ${directError?.message || 'unknown error'}. HTTPS Reader attempt failed: ${readerError?.message || readerError}. Original HTTP Reader attempt failed: ${originalReaderError?.message || originalReaderError}`);
          }
        }
        throw readerError;
      }
    }

