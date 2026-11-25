    'use strict';

    function resolveCsvDelimiter(path, requested, source) {
      if (requested !== undefined && requested !== null && String(requested) !== '') return normalizeCsvDelimiter(requested);
      if (/\.tsv$/i.test(String(path || ''))) return '\t';
      const sampleLimit = 128 * 1024;
      let sample = String(source || '').slice(0, sampleLimit);
      if (sample.length < String(source || '').length) sample = sample.slice(0, Math.max(0, sample.lastIndexOf('\n')));
      const candidates = [',', '\t', ';', '|'];
      let best = { delimiter: ',', score: 0 };
      for (const delimiter of candidates) {
        try {
          const rows = parseCsvRows(sample, delimiter).slice(0, 25);
          if (rows.length < 2) continue;
          const counts = rows.map(row => row.length).filter(count => count > 1);
          if (!counts.length) continue;
          const frequency = new Map();
          for (const count of counts) frequency.set(count, (frequency.get(count) || 0) + 1);
          const [columns, matching] = [...frequency.entries()].sort((a, b) => b[1] - a[1] || b[0] - a[0])[0];
          const consistency = matching / rows.length;
          const score = (columns - 1) * consistency;
          if (score > best.score) best = { delimiter, score };
        } catch (_) {}
      }
      return best.delimiter;
    }

    function safeJsonPath(pathValue) {
      const parts = String(pathValue || '').split('.').filter(Boolean);
      if (parts.some(part => ['__proto__', 'prototype', 'constructor'].includes(part))) throw new Error('JSON paths cannot address prototype-related keys.');
      return parts;
    }
    function readJsonPath(value, pathValue) {
      let current = value;
      for (const part of safeJsonPath(pathValue)) {
        if (current == null || typeof current !== 'object' || !Object.prototype.hasOwnProperty.call(current, part)) return undefined;
        current = current[part];
      }
      return current;
    }
    function writeJsonPath(value, pathValue, nextValue) {
      const parts = safeJsonPath(pathValue);
      if (!parts.length) throw new Error('A non-empty JSON field path is required.');
      let current = value;
      for (const part of parts.slice(0, -1)) {
        if (!current[part] || typeof current[part] !== 'object' || Array.isArray(current[part])) current[part] = {};
        current = current[part];
      }
      current[parts.at(-1)] = nextValue;
    }
    function transformJsonValue(value, args) {
      const mode = String(args.mode || 'pretty');
      const sort = input => Array.isArray(input) ? input.map(sort) : input && typeof input === 'object' ? Object.fromEntries(Object.keys(input).sort().map(key => [key, sort(input[key])])) : input;
      const safeKey = key => {
        const value = String(key);
        if (['__proto__', 'prototype', 'constructor'].includes(value)) throw new Error(`JSON key '${value}' is not allowed in structural transforms.`);
        return value;
      };
      if (mode === 'pretty' || mode === 'minify') return value;
      if (mode === 'sort_keys') return sort(value);
      if (mode === 'pick_fields' || mode === 'omit_fields') {
        const fields = new Set((Array.isArray(args.fields) ? args.fields : []).map(safeKey));
        if (!fields.size) throw new Error(`${mode} requires a non-empty fields array.`);
        const apply = item => {
          if (!item || typeof item !== 'object' || Array.isArray(item)) return item;
          const output = {};
          for (const [key, entry] of Object.entries(item)) if (mode === 'pick_fields' ? fields.has(key) : !fields.has(key)) Object.defineProperty(output, key, { value: entry, enumerable: true, configurable: true, writable: true });
          return output;
        };
        return Array.isArray(value) ? value.map(apply) : apply(value);
      }
      if (mode === 'rename_keys') {
        const mapping = args.mapping && typeof args.mapping === 'object' && !Array.isArray(args.mapping) ? args.mapping : {};
        if (!Object.keys(mapping).length) throw new Error('rename_keys requires a non-empty mapping object.');
        const apply = item => {
          if (Array.isArray(item)) return item.map(apply);
          if (!item || typeof item !== 'object') return item;
          const output = {};
          const targets = new Set();
          for (const [key, entry] of Object.entries(item)) {
            const target = safeKey(mapping[key] || key);
            if (targets.has(target)) throw new Error(`Renaming would create duplicate key '${target}'.`);
            targets.add(target);
            Object.defineProperty(output, target, { value: apply(entry), enumerable: true, configurable: true, writable: true });
          }
          return output;
        };
        return apply(value);
      }
      if (mode === 'flatten') {
        const separator = String(args.separator || '.').slice(0, 3);
        if (!separator) throw new Error('flatten separator cannot be empty.');
        const output = {};
        const visit = (item, prefix) => {
          if (item && typeof item === 'object' && !Array.isArray(item)) {
            const entries = Object.entries(item);
            if (!entries.length && prefix) Object.defineProperty(output, prefix, { value: {}, enumerable: true, configurable: true, writable: true });
            for (const [key, entry] of entries) {
              safeKey(key);
              const next = prefix ? `${prefix}${separator}${key}` : key;
              visit(entry, next);
            }
          } else {
            if (Object.prototype.hasOwnProperty.call(output, prefix)) throw new Error(`Flattening creates a duplicate key '${prefix}'.`);
            Object.defineProperty(output, prefix, { value: item, enumerable: true, configurable: true, writable: true });
          }
        };
        if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('flatten requires a JSON object at the root.');
        visit(value, '');
        return output;
      }
      if (mode === 'unflatten') {
        const separator = String(args.separator || '.').slice(0, 3);
        if (!separator) throw new Error('unflatten separator cannot be empty.');
        if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('unflatten requires a JSON object at the root.');
        const output = {};
        for (const [key, entry] of Object.entries(value)) {
          const parts = key.split(separator).map(safeKey);
          if (!parts.length || parts.some(part => !part)) throw new Error(`Invalid flattened key '${key}'.`);
          let current = output;
          for (const part of parts.slice(0, -1)) {
            if (current[part] == null) current[part] = {};
            if (typeof current[part] !== 'object' || Array.isArray(current[part])) throw new Error(`Unflattening conflicts at '${part}'.`);
            current = current[part];
          }
          const last = parts.at(-1);
          if (Object.prototype.hasOwnProperty.call(current, last)) throw new Error(`Unflattening creates a duplicate key '${key}'.`);
          current[last] = entry;
        }
        return output;
      }
      if (mode === 'filter_array' || mode === 'map_values') {
        const arrayPath = String(args.array_path || '');
        const array = arrayPath ? readJsonPath(value, arrayPath) : value;
        if (!Array.isArray(array)) throw new Error(`'${arrayPath || '(root)'}' must refer to a JSON array.`);
        const output = JSON.parse(JSON.stringify(value));
        const target = arrayPath ? readJsonPath(output, arrayPath) : output;
        const field = String(args.field || '');
        if (!field) throw new Error(`${mode} requires a field path.`);
        if (mode === 'filter_array') {
          const operator = String(args.operator || 'equals');
          const wanted = args.value;
          const allowed = Array.isArray(args.values) ? args.values : [wanted];
          const matches = item => {
            const current = readJsonPath(item, field);
            const left = current == null ? '' : String(current);
            const right = wanted == null ? '' : String(wanted);
            switch (operator) {
              case 'equals': return left === right;
              case 'not_equals': return left !== right;
              case 'contains': return left.toLowerCase().includes(right.toLowerCase());
              case 'gt': return Number.isFinite(Number(current)) && Number(current) > Number(wanted);
              case 'gte': return Number.isFinite(Number(current)) && Number(current) >= Number(wanted);
              case 'lt': return Number.isFinite(Number(current)) && Number(current) < Number(wanted);
              case 'lte': return Number.isFinite(Number(current)) && Number(current) <= Number(wanted);
              case 'empty': return !left.trim();
              case 'not_empty': return !!left.trim();
              case 'one_of': return allowed.some(value => String(value) === left);
              default: throw new Error(`Unsupported JSON filter operator '${operator}'.`);
            }
          };
          const filtered = target.filter(matches);
          if (!arrayPath) return filtered;
          writeJsonPath(output, arrayPath, filtered);
          return output;
        }
        const operation = String(args.operation || 'trim');
        for (const item of target) {
          const current = readJsonPath(item, field);
          if (current === undefined) continue;
          let next = current;
          if (operation === 'trim') next = typeof current === 'string' ? current.trim() : current;
          else if (operation === 'lowercase') next = typeof current === 'string' ? current.toLowerCase() : current;
          else if (operation === 'uppercase') next = typeof current === 'string' ? current.toUpperCase() : current;
          else if (operation === 'to_number') { next = current === '' || current == null ? null : Number(current); if (next !== null && !Number.isFinite(next)) throw new Error(`Field '${field}' contains a value that cannot be converted to a number.`); }
          else if (operation === 'to_string') next = current == null ? '' : String(current);
          else if (operation === 'null_to_empty') next = current == null ? '' : current;
          else throw new Error(`Unsupported JSON map operation '${operation}'.`);
          writeJsonPath(item, field, next);
        }
        return output;
      }
      throw new Error(`Unsupported JSON transform mode '${mode}'.`);
    }

    async function validateDataset(args) {
      const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024);
      const delimiter = resolveCsvDelimiter(args.path, args.delimiter, text);
      const dataset = csvDataset(args.path, text, delimiter);
      const issues = [];
      const maxIssues = Math.max(1, Math.min(200, Math.floor(Number(args.max_issues) || 50)));
      const addIssue = (row, column, rule, message) => { if (issues.length < maxIssues) issues.push({ row, column, rule, message }); };
      const requiredColumns = Array.isArray(args.required_columns) ? args.required_columns : [];
      const indexes = new Map();
      for (const column of requiredColumns) {
        try { indexes.set(column, datasetColumnIndex(dataset.headers, column)); }
        catch (error) { addIssue(0, column, 'required_column', error.message); }
      }
      const uniqueIndexes = [];
      for (const column of (Array.isArray(args.unique_columns) ? args.unique_columns : [])) {
        try { uniqueIndexes.push({ column, index: datasetColumnIndex(dataset.headers, column), seen: new Set() }); }
        catch (error) { addIssue(0, column, 'unique_column', error.message); }
      }
      const rules = Array.isArray(args.rules) ? args.rules.slice(0, 100) : [];
      const compiled = [];
      for (const rule of rules) {
        try { compiled.push({ ...rule, index: datasetColumnIndex(dataset.headers, rule.column) }); }
        catch (error) { addIssue(0, rule?.column || '', 'rule_column', error.message); }
      }
      const rowsChecked = Math.min(dataset.rows.length, 100000);
      for (let rowIndex = 0; rowIndex < rowsChecked; rowIndex++) {
        const row = dataset.rows[rowIndex];
        for (const item of uniqueIndexes) {
          const value = String(row[item.index] ?? '').trim();
          if (!value) continue;
          if (item.seen.has(value)) addIssue(rowIndex + 2, item.column, 'unique', `Duplicate value '${value.slice(0, 100)}'.`);
          else item.seen.add(value);
        }
        for (const rule of compiled) {
          const raw = String(row[rule.index] ?? '').trim();
          const expected = rule.value == null ? '' : String(rule.value);
          const number = Number(raw);
          const values = Array.isArray(rule.values) ? rule.values.map(String) : [expected];
          let valid = true;
          switch (String(rule.operator || 'not_empty')) {
            case 'not_empty': valid = !!raw; break;
            case 'is_number': valid = !!raw && Number.isFinite(number); break;
            case 'min': valid = !!raw && Number.isFinite(number) && number >= Number(rule.value); break;
            case 'max': valid = !!raw && Number.isFinite(number) && number <= Number(rule.value); break;
            case 'equals': valid = raw === expected; break;
            case 'not_equals': valid = raw !== expected; break;
            case 'contains': valid = raw.toLowerCase().includes(expected.toLowerCase()); break;
            case 'one_of': valid = values.includes(raw); break;
            case 'is_email': valid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(raw); break;
            case 'is_url': { try { const url = new URL(raw); valid = ['http:', 'https:'].includes(url.protocol); } catch (_) { valid = false; } break; }
            default: addIssue(0, rule.column, 'unknown_rule', `Unsupported validation operator '${rule.operator}'.`); valid = true;
          }
          if (!valid) addIssue(rowIndex + 2, rule.column, rule.operator, `Value '${raw.slice(0, 100)}' failed ${rule.operator}${expected ? ` (${expected})` : ''}.`);
          if (issues.length >= maxIssues) break;
        }
        if (issues.length >= maxIssues) break;
      }
      return { path: args.path, delimiter: delimiter === '\t' ? 'TAB' : delimiter, rows: dataset.rows.length, rowsChecked, columns: dataset.headers, valid: issues.length === 0 && rowsChecked === dataset.rows.length, issueCount: issues.length, issues, issuesTruncated: issues.length >= maxIssues, rowsTruncated: rowsChecked < dataset.rows.length, note: 'Validation is deterministic for the supplied rules. No source file was modified.' };
    }

    function reportMarkdown(args) {
      const content = String(args.content || '');
      const title = String(args.title || '').trim().slice(0, 200);
      const summary = String(args.summary || '').trim().slice(0, 1000);
      const parts = [];
      if (title) parts.push(`# ${title}`);
      if (summary) parts.push(`> **Summary:** ${summary}`);
      if (args.include_toc) {
        const headings = content.split(/\r?\n/).map(line => line.match(/^(#{1,6})\s+(.+)$/)).filter(Boolean);
        if (headings.length) {
          const slugCounts = new Map();
          const rows = headings.slice(0, 100).map(match => {
            const label = match[2].trim();
            let slug = label.toLowerCase().normalize('NFKD').replace(/[\u0300-\u036f]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'section';
            const count = (slugCounts.get(slug) || 0) + 1; slugCounts.set(slug, count); if (count > 1) slug += `-${count}`;
            return `${'  '.repeat(Math.max(0, match[1].length - 1))}- [${label}](#${slug})`;
          });
          parts.push(`## Contents\n\n${rows.join('\n')}`);
        }
      }
      if (content) parts.push(content);
      parts.push(`---\n\nGenerated by RunMind 2.0.8 · ${new Date().toISOString()}`);
      return parts.join('\n\n') + '\n';
    }

    function markdownReportHtml(content) {
      const rendered = renderMarkdown(content);
      const document = new DOMParser().parseFromString(`<body>${rendered}</body>`, 'text/html');
      const headings = [...document.body.querySelectorAll('h1,h2,h3,h4,h5,h6')];
      const counts = new Map();
      for (const heading of headings) {
        const base = heading.textContent.toLowerCase().normalize('NFKD').replace(/[\u0300-\u036f]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'section';
        const count = (counts.get(base) || 0) + 1; counts.set(base, count);
        heading.id = count > 1 ? `${base}-${count}` : base;
      }
      const toc = headings.length ? `<nav class="report-toc"><strong>Contents</strong><ol>${headings.map(heading => `<li class="level-${heading.tagName.slice(1)}"><a href="#${escapeHtml(heading.id)}">${escapeHtml(heading.textContent)}</a></li>`).join('')}</ol></nav>` : '';
      return { html: document.body.innerHTML, toc };
    }

    function galleryRelativePath(outputPath, imagePath) {
      const from = safeRelativePath(outputPath).slice(0, -1);
      const to = safeRelativePath(imagePath);
      let common = 0;
      while (common < from.length && common < to.length && from[common].toLowerCase() === to[common].toLowerCase()) common++;
      const parts = [...new Array(from.length - common).fill('..'), ...to.slice(common)];
      return parts.map(part => part === '..' ? '..' : encodeURIComponent(part)).join('/');
    }
    async function buildImageGalleryHtml(args, outputPath) {
      if (!agentDirectoryHandle) throw new Error('Select a workspace folder before creating an image gallery.');
      const images = Array.isArray(args.images) ? args.images : [];
      if (!images.length || images.length > 200) throw new Error('Image galleries require between 1 and 200 images.');
      const cards = [];
      let totalImageBytes = 0;
      for (const item of images) {
        const path = String(item?.path || '').trim();
        if (!path) throw new Error('Every gallery image requires a workspace-relative path.');
        const { file, source } = await readTaskFile(path, 25 * 1024 * 1024);
        if (source !== 'workspace') throw new Error(`Gallery image '${path}' must be inside the selected workspace, not a conversation attachment.`);
        totalImageBytes += file.size;
        if (totalImageBytes > 100 * 1024 * 1024) throw new Error('Image galleries are limited to 100 MB of source images in total.');
        const extension = path.split('.').pop().toLowerCase();
        const mime = String(file.type || '').toLowerCase();
        const allowed = ['image/png', 'image/jpeg', 'image/webp', 'image/gif'];
        const inferred = ({ png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp', gif: 'image/gif' })[extension];
        if (!(allowed.includes(mime) || (!mime && allowed.includes(inferred)))) throw new Error(`'${path}' is not a supported PNG, JPEG, WebP, or GIF image.`);
        const signature = new Uint8Array(await file.slice(0, 12).arrayBuffer());
        const png = signature.length >= 8 && signature[0] === 0x89 && signature[1] === 0x50 && signature[2] === 0x4E && signature[3] === 0x47;
        const jpeg = signature[0] === 0xFF && signature[1] === 0xD8 && signature[2] === 0xFF;
        const gif = String.fromCharCode(...signature.slice(0, 6)).match(/^GIF8[79]a$/);
        const webp = String.fromCharCode(...signature.slice(0, 4)) === 'RIFF' && String.fromCharCode(...signature.slice(8, 12)) === 'WEBP';
        if (!(png || jpeg || gif || webp)) throw new Error(`'${path}' does not have a valid supported image signature.`);
        const href = galleryRelativePath(outputPath, path);
        const alt = String(item?.alt || item?.caption || file.name).slice(0, 240);
        const caption = String(item?.caption || file.name).slice(0, 500);
        cards.push(`<figure><a href="${href}" target="_blank" rel="noopener noreferrer"><img src="${href}" alt="${escapeHtml(alt)}" loading="lazy"></a><figcaption>${escapeHtml(caption)}</figcaption></figure>`);
      }
      const title = escapeHtml(String(args.title || 'Image gallery').slice(0, 200));
      return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${title}</title><style>:root{color-scheme:light dark}*{box-sizing:border-box}body{margin:0;padding:32px;font:15px/1.5 system-ui,sans-serif;background:#f4f6f8;color:#202833}main{max-width:1500px;margin:auto}h1{margin:0 0 24px}.gallery{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:18px}figure{margin:0;padding:10px;background:white;border:1px solid #d7dee7;border-radius:12px;box-shadow:0 3px 14px #17212b12}img{display:block;width:100%;height:220px;object-fit:contain;background:#eef1f5;border-radius:7px}figcaption{padding:10px 3px 2px;overflow-wrap:anywhere;color:#465364}@media(prefers-color-scheme:dark){body{background:#202326;color:#eee}figure{background:#2c3034;border-color:#454b52}img{background:#191c1f}figcaption{color:#d3d8df}}@media(max-width:560px){body{padding:18px}.gallery{grid-template-columns:repeat(auto-fill,minmax(150px,1fr))}img{height:160px}}</style></head><body><main><h1>${title}</h1><div class="gallery">${cards.join('')}</div></main></body></html>`;
    }

    async function verifyWorkspaceArtifact(path, requestedFormat = 'auto', signal, requestedDelimiter = '') {
      const { file } = await readTaskFile(path, 50 * 1024 * 1024);
      const extension = String(path).split('.').pop().toLowerCase();
      const format = requestedFormat && requestedFormat !== 'auto' ? requestedFormat : ({ docx: 'docx', xlsx: 'xlsx', pdf: 'pdf', svg: 'svg', html: 'html', htm: 'html', json: 'json', csv: 'csv', tsv: 'tsv', mmd: 'mermaid', md: 'markdown' })[extension] || 'binary';
      const checks = [];
      const pass = (name, detail) => checks.push({ name, passed: true, detail });
      if (format === 'docx' || format === 'xlsx') {
        const archive = await parseWorkspaceZip({ path }, signal);
        const entries = new Map(archive.entries.map(entry => [entry.path, entry]));
        const required = format === 'docx' ? ['[Content_Types].xml', '_rels/.rels', 'word/document.xml'] : ['[Content_Types].xml', '_rels/.rels', 'xl/workbook.xml'];
        for (const name of required) if (!entries.has(name)) throw new Error(`${format.toUpperCase()} package is missing required entry '${name}'.`);
        const bytes = new Uint8Array(await file.arrayBuffer());
        const requiredXml = format === 'docx' ? ['word/document.xml', 'word/styles.xml', 'word/numbering.xml', '[Content_Types].xml'] : ['xl/workbook.xml', '[Content_Types].xml'];
        for (const name of requiredXml) if (!entries.has(name)) throw new Error(`Package XML '${name}' is missing.`);
        const xmlParts = archive.entries.filter(entry => /\.(?:xml|rels)$/i.test(entry.path));
        for (const entry of xmlParts) {
          const body = await decompressZipEntry(bytes, entry, signal);
          const document = new DOMParser().parseFromString(new TextDecoder().decode(body), 'application/xml');
          if (document.querySelector('parsererror') || !document.documentElement) throw new Error(`Package XML '${entry.path}' is invalid.`);
          pass(`XML ${entry.path}`, document.documentElement.localName || document.documentElement.nodeName);
        }
        if (format === 'xlsx' && !xmlParts.some(entry => /^xl\/worksheets\/sheet\d+\.xml$/i.test(entry.path))) throw new Error('XLSX workbook contains no worksheet XML.');
        pass('ZIP structure', `${archive.entryCount} entries, CRC-32 checked for every XML/relationship part`);
        return { valid: true, format, size: file.size, checks, note: 'Structural validation only; this does not prove visual layout or every Office application feature.' };
      }
      if (format === 'pdf') {
        const bytes = new Uint8Array(await file.arrayBuffer());
        const text = new TextDecoder('latin1').decode(bytes);
        if (!/^%PDF-1\.[0-7]/.test(text) || !/%%EOF\s*$/.test(text) || !/startxref\s+\d+/.test(text)) throw new Error('PDF header, cross-reference pointer, or EOF marker is missing.');
        const pages = (text.match(/\/Type\s*\/Page\b/g) || []).length;
        if (!pages) throw new Error('PDF contains no page objects.');
        pass('PDF structure', `${pages} page object(s), header and EOF markers present`);
      } else if (format === 'svg' || format === 'html') {
        const text = await file.text();
        const document = new DOMParser().parseFromString(text, format === 'svg' ? 'image/svg+xml' : 'text/html');
        if (format === 'svg' && (document.querySelector('parsererror') || document.documentElement.localName !== 'svg')) throw new Error('SVG document is not well-formed or has no svg root.');
        if (format === 'html' && document.documentElement.localName.toLowerCase() !== 'html') throw new Error('HTML document has no html root.');
        pass(`${format.toUpperCase()} document`, `Root element: ${document.documentElement.localName}`);
      } else if (format === 'mermaid') {
        const text = await file.text();
        if (!/^flowchart\s+(?:TD|TB|LR|RL|BT)\s*\r?\n/.test(text) || !/\bN\d+\s*\[/.test(text)) throw new Error('Mermaid source must contain a supported flowchart declaration and at least one node.');
        pass('Mermaid source', 'Flowchart declaration and node definitions found');
      } else if (format === 'markdown') {
        const text = await file.text();
        if (!text.trim()) throw new Error('Markdown artifact is empty.');
        pass('Markdown document', `${text.length.toLocaleString()} readable characters`);
      } else if (format === 'json') {
        JSON.parse(await file.text());
        pass('JSON syntax', 'JSON.parse completed without errors');
      } else if (format === 'csv' || format === 'tsv') {
        const text = await file.text();
        const delimiter = requestedDelimiter ? normalizeCsvDelimiter(requestedDelimiter) : format === 'tsv' ? '\t' : resolveCsvDelimiter(path, '', text);
        const rows = parseCsvRows(text, delimiter);
        if (!rows.length) throw new Error('Dataset contains no rows.');
        pass('Delimited data', `${Math.max(0, rows.length - 1)} data row(s), ${rows[0].length} column(s)`);
      } else {
        pass('File readability', 'File exists and is readable; no format-specific validator is available.');
      }
      return { valid: true, format, size: file.size, checks, note: 'Structural validation only; content correctness remains a separate review.' };
    }

    async function saveGeneratedArtifact(path, blob, format, title, detail, signal) {
      if (!agentDirectoryHandle) throw new Error('Select a workspace folder before creating files.');
      const approved = await requireAgentApproval({ title, subtitle: `New ${format.toUpperCase()} file · existing files are never overwritten`, detail: `${path}\n${detail || `${blob.size.toLocaleString()} bytes`}`, risky: true }, signal);
      if (!approved) return { cancelled: true, message: 'User declined artifact creation.' };
      const written = await writeWorkspaceFile(path, blob, false);
      try {
        const verification = await verifyWorkspaceArtifact(path, format, signal);
        return { created: written.path, bytes: written.bytes, format: format.toUpperCase(), verified: !!verification.valid, verification };
      } catch (error) {
        return { created: written.path, bytes: written.bytes, format: format.toUpperCase(), verified: false, verificationError: error?.message || String(error), warning: 'The file was written, but automatic structural verification did not complete.' };
      }
    }

    async function assertNewWorkspacePath(path) {
      const parts = safeRelativePath(path);
      const name = parts.pop();
      const directory = await getDirectoryAtPath(agentDirectoryHandle, parts.join('/'));
      try { await directory.getFileHandle(name, { create: false }); throw new Error(`File '${path}' already exists; overwriting is disabled.`); }
      catch (error) { if (error?.name !== 'NotFoundError') throw error; }
    }

    function buildChartSvg(args) {
      if (!window.RunMindArtifactTools?.buildChartSvg) throw new Error('The local chart generator did not load.');
      return window.RunMindArtifactTools.buildChartSvg(args);
    }
    function buildDiagramSvg(args) {
      const nodes = Array.isArray(args.nodes) ? args.nodes.map(value => String(value).slice(0, 90)) : [];
      const edges = Array.isArray(args.edges) ? args.edges : [];
      if (!nodes.length || nodes.length > 16) throw new Error('Provide between 1 and 16 node labels.');
      if (edges.length > 40) throw new Error('Diagrams support at most 40 connections.');
      const direction = ['TD', 'TB', 'LR', 'RL', 'BT'].includes(String(args.direction || '').toUpperCase()) ? String(args.direction).toUpperCase() : 'TD';
      const horizontal = direction === 'LR' || direction === 'RL';
      const columns = horizontal ? Math.min(4, nodes.length) : 1;
      const rows = Math.ceil(nodes.length / columns);
      const nodeW = horizontal ? 250 : 320, nodeH = 70, gapX = 70, gapY = 65;
      const width = horizontal ? 100 + columns * (nodeW + gapX) : 760;
      const height = 100 + rows * (nodeH + gapY);
      const centers = nodes.map((_, index) => {
        let row = horizontal ? Math.floor(index / columns) : index;
        let column = horizontal ? index % columns : 0;
        if (direction === 'RL') column = columns - 1 - column;
        if (direction === 'BT') row = rows - 1 - row;
        return { x: horizontal ? 50 + column * (nodeW + gapX) + nodeW / 2 : width / 2, y: 65 + row * (nodeH + gapY) + nodeH / 2 };
      });
      const edgeSvg = edges.map(edge => {
        const from = Number(edge?.from), to = Number(edge?.to);
        if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || to < 0 || from >= nodes.length || to >= nodes.length || from === to) throw new Error('Every diagram edge must connect two different, valid zero-based node indexes.');
        const a = centers[from], b = centers[to], dx = b.x - a.x, dy = b.y - a.y;
        const scaleA = Math.min((nodeW / 2) / Math.max(1, Math.abs(dx)), (nodeH / 2) / Math.max(1, Math.abs(dy)));
        const scaleB = Math.min((nodeW / 2) / Math.max(1, Math.abs(dx)), (nodeH / 2) / Math.max(1, Math.abs(dy)));
        const sx = a.x + dx * scaleA, sy = a.y + dy * scaleA;
        const ex = b.x - dx * scaleB, ey = b.y - dy * scaleB;
        return `<path d="M ${sx.toFixed(1)} ${sy.toFixed(1)} L ${ex.toFixed(1)} ${ey.toFixed(1)}" fill="none" stroke="#778395" stroke-width="2.2" marker-end="url(#arrow)"/>`;
      }).join('');
      const nodeSvg = nodes.map((label, index) => {
        const center = centers[index];
        return `<g><rect x="${center.x - nodeW / 2}" y="${center.y - nodeH / 2}" width="${nodeW}" height="${nodeH}" rx="10" fill="#edf3fb" stroke="#607fae" stroke-width="1.5"/><text x="${center.x}" y="${center.y + 5}" text-anchor="middle" font-size="15" fill="#23364d">${escapeHtml(label)}</text></g>`;
      }).join('');
      const title = escapeHtml(String(args.title || 'Diagram').slice(0, 120));
      return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" role="img" aria-label="${title}"><defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto"><path d="M0,0 L0,6 L7,3 z" fill="#778395"/></marker></defs><text x="${width / 2}" y="30" text-anchor="middle" font-size="21" font-weight="600" fill="#23364d">${title}</text>${edgeSvg}${nodeSvg}</svg>`;
    }

    function evaluateSafeArithmetic(source) {
      const tokens = String(source).match(/\d+(?:\.\d+)?|\*\*|[()+\-*/%]/g) || [];
      if (tokens.join('').replace(/\s/g, '') !== String(source).replace(/\s/g, '')) throw new Error('Invalid arithmetic expression.');
      let index = 0;
      const peek = () => tokens[index];
      const take = () => tokens[index++];
      function primary() {
        if (peek() === '+') { take(); return primary(); }
        if (peek() === '-') { take(); return -primary(); }
        if (peek() === '(') { take(); const value = sum(); if (take() !== ')') throw new Error('Missing closing parenthesis.'); return value; }
        const token = take(); if (!token || !/^\d+(?:\.\d+)?$/.test(token)) throw new Error('Expected a number.'); return Number(token);
      }
      function power() { let left = primary(); if (peek() === '**') { take(); left = left ** power(); } return left; }
      function product() { let left = power(); while (['*','/','%'].includes(peek())) { const op = take(), right = power(); left = op === '*' ? left * right : op === '/' ? left / right : left % right; } return left; }
      function sum() { let left = product(); while (['+','-'].includes(peek())) { const op = take(), right = product(); left = op === '+' ? left + right : left - right; } return left; }
      const value = sum(); if (index !== tokens.length || !Number.isFinite(value)) throw new Error('Invalid or non-finite arithmetic result.'); return value;
    }
