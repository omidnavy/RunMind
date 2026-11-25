    'use strict';

    const OCR_SETTINGS_KEY = 'runmind.ocr.settings.v1';
    const OCR_CACHE_PATH = 'runmind-ocr';
    const OCR_PDFJS_BASE = 'https://localhost/Vendor/PdfJs';
    const OCR_TESSERACT_BASE = 'https://localhost/Vendor/Tesseract';
    const OCR_BUNDLED_LANG_PATH = 'https://localhost/OCR/tessdata';
    const OCR_REMOTE_LANG_PATH = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main';
    const OCR_LANGUAGES = Object.freeze([
      { code: 'eng', name: 'English', locale: 'en', included: true },
      { code: 'fas', name: 'Persian (فارسی)', locale: 'fa_IR', included: true },
      { code: 'fra', name: 'French', locale: 'fr' },
      { code: 'spa', name: 'Spanish', locale: 'es' },
      { code: 'ara', name: 'Arabic', locale: 'ar' },
      { code: 'chi_sim', name: 'Chinese (Simplified)', locale: 'zh_CN' },
      { code: 'chi_tra', name: 'Chinese (Traditional)', locale: 'zh_TW' },
      { code: 'rus', name: 'Russian', locale: 'ru' },
      { code: 'fin', name: 'Finnish', locale: 'fi' },
      { code: 'deu', name: 'German', locale: 'de' },
      { code: 'ita', name: 'Italian', locale: 'it' },
      { code: 'por', name: 'Portuguese', locale: 'pt' },
      { code: 'jpn', name: 'Japanese', locale: 'ja' },
      { code: 'kor', name: 'Korean', locale: 'ko' },
      { code: 'tur', name: 'Turkish', locale: 'tr' },
      { code: 'urd', name: 'Urdu', locale: 'ur' }
    ]);
    const OCR_LANGUAGE_CODES = new Set(OCR_LANGUAGES.map(language => language.code));
    let pdfjsPromise = null;
    let tesseractPromise = null;
    let ocrLanguageInstallBusy = false;

    function normalizeOcrSettings(value) {
      const installed = Array.isArray(value?.installed)
        ? [...new Set(value.installed.filter(code => OCR_LANGUAGE_CODES.has(code) && !OCR_LANGUAGES.find(language => language.code === code)?.included))]
        : [];
      const available = new Set([...installed, 'eng', 'fas']);
      const selected = Array.isArray(value?.selected)
        ? [...new Set(value.selected.filter(code => available.has(code)))]
        : ['eng', 'fas'];
      return { selected: selected.length ? selected : ['eng'], installed };
    }
    function loadOcrLanguageSettings() {
      try { return normalizeOcrSettings(JSON.parse(localStorage.getItem(OCR_SETTINGS_KEY) || 'null')); }
      catch (_) { return normalizeOcrSettings(null); }
    }
    function saveOcrLanguageSettings(settings) {
      try { localStorage.setItem(OCR_SETTINGS_KEY, JSON.stringify(normalizeOcrSettings(settings))); return true; }
      catch (_) { return false; }
    }
    function renderOcrLanguageSettings() {
      if (!ocrLanguageList || !ocrLanguageStatus) return;
      const settings = loadOcrLanguageSettings();
      ocrLanguageList.replaceChildren();
      for (const language of OCR_LANGUAGES) {
        const isInstalled = language.included || settings.installed.includes(language.code);
        const row = document.createElement('div'); row.className = 'ocr-language-row';
        const label = document.createElement('label'); label.className = 'ocr-language-choice';
        const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.checked = settings.selected.includes(language.code); checkbox.disabled = !isInstalled;
        checkbox.addEventListener('change', () => {
          const next = loadOcrLanguageSettings();
          if (checkbox.checked) { if (!next.selected.includes(language.code)) next.selected.push(language.code); }
          else next.selected = next.selected.filter(code => code !== language.code);
          if (!next.selected.length) { checkbox.checked = true; ocrLanguageStatus.textContent = 'Select at least one OCR language.'; return; }
          if (!saveOcrLanguageSettings(next)) ocrLanguageStatus.textContent = 'Could not save OCR language settings in this WebView2 profile.';
          else ocrLanguageStatus.textContent = `Selected: ${next.selected.map(code => OCR_LANGUAGES.find(item => item.code === code)?.name || code).join(' + ')}.`;
        });
        const meta = document.createElement('span'); meta.className = 'ocr-language-meta';
        const name = document.createElement('span'); name.textContent = language.name;
        const locale = document.createElement('small'); locale.textContent = `${language.locale} · ${language.code}`;
        meta.append(name, locale); label.append(checkbox, meta); row.append(label);
        const action = document.createElement('span'); action.className = 'ocr-language-action';
        const status = document.createElement('small'); status.className = 'setting-note'; status.textContent = language.included ? 'Included' : isInstalled ? 'Downloaded' : '';
        if (!language.included && !isInstalled) {
          const button = document.createElement('button'); button.type = 'button'; button.className = 'setting-reset'; button.textContent = 'Download'; button.disabled = ocrLanguageInstallBusy;
          button.addEventListener('click', () => { void installOcrLanguage(language.code); }); action.append(button);
        } else action.append(status);
        row.append(action); ocrLanguageList.append(row);
      }
      if (!ocrLanguageInstallBusy) {
        ocrLanguageStatus.textContent = `Selected: ${settings.selected.map(code => OCR_LANGUAGES.find(item => item.code === code)?.name || code).join(' + ')}. Optional packs are stored in this WebView2 profile.`;
      }
    }
    async function loadTesseractLibrary() {
      if (window.Tesseract?.createWorker) return window.Tesseract;
      if (!tesseractPromise) {
        tesseractPromise = new Promise((resolve, reject) => {
          const existing = document.querySelector('script[data-runmind-tesseract]');
          const script = existing || document.createElement('script');
          const fail = () => { tesseractPromise = null; script.remove(); reject(new Error('Could not load the bundled Tesseract.js runtime. Run Prepare-RunMind-OCR.ps1 and rebuild RunMind.')); };
          if (!existing) { script.src = `${OCR_TESSERACT_BASE}/tesseract.min.js`; script.async = true; script.dataset.runmindTesseract = 'true'; }
          script.addEventListener('load', () => window.Tesseract?.createWorker ? resolve(window.Tesseract) : fail(), { once: true });
          script.addEventListener('error', fail, { once: true });
          if (!existing) document.head.append(script);
          else if (window.Tesseract?.createWorker) resolve(window.Tesseract);
        });
      }
      return tesseractPromise;
    }
    async function loadPdfJs() {
      if (window.pdfjsLib?.getDocument) return window.pdfjsLib;
      if (!pdfjsPromise) {
        pdfjsPromise = import(`${OCR_PDFJS_BASE}/build/pdf.mjs`).then(pdfjs => {
          pdfjs.GlobalWorkerOptions.workerSrc = `${OCR_PDFJS_BASE}/build/pdf.worker.mjs`;
          return pdfjs;
        }).catch(error => { pdfjsPromise = null; throw new Error(`Could not load the bundled PDF.js engine. Run Prepare-RunMind-OCR.ps1 and rebuild RunMind. ${error?.message || error}`); });
      }
      return pdfjsPromise;
    }
    function createTesseractWorker(languages, logger) {
      return loadTesseractLibrary().then(Tesseract => Tesseract.createWorker(languages, 1, {
        workerPath: `${OCR_TESSERACT_BASE}/worker.min.js`,
        corePath: `${OCR_TESSERACT_BASE}/core`,
        langPath: OCR_BUNDLED_LANG_PATH,
        cachePath: OCR_CACHE_PATH,
        cacheMethod: 'write',
        gzip: false,
        workerBlobURL: true,
        logger: logger || (() => {})
      }));
    }
    async function installOcrLanguage(code) {
      const language = OCR_LANGUAGES.find(item => item.code === code);
      if (!language || language.included || ocrLanguageInstallBusy) return;
      ocrLanguageInstallBusy = true; renderOcrLanguageSettings();
      ocrLanguageStatus.textContent = `Downloading ${language.name} OCR data…`;
      let worker = null; let outcomeMessage = '';
      try {
        const Tesseract = await loadTesseractLibrary();
        worker = await Tesseract.createWorker(code, 1, {
          workerPath: `${OCR_TESSERACT_BASE}/worker.min.js`,
          corePath: `${OCR_TESSERACT_BASE}/core`,
          langPath: OCR_REMOTE_LANG_PATH,
          cachePath: OCR_CACHE_PATH,
          cacheMethod: 'write',
          gzip: false,
          workerBlobURL: true,
          logger: message => {
            if (!ocrLanguageStatus || !message?.status) return;
            const percent = Number.isFinite(message.progress) ? ` ${Math.round(message.progress * 100)}%` : '';
            ocrLanguageStatus.textContent = `${language.name}: ${message.status}${percent}`;
          }
        });
        await worker.terminate(); worker = null;
        const settings = loadOcrLanguageSettings();
        settings.installed.push(code); settings.selected.push(code);
        if (!saveOcrLanguageSettings(settings)) throw new Error('The language data downloaded, but its settings could not be saved in this WebView2 profile.');
        outcomeMessage = `${language.name} downloaded and cached. It can now be used offline in this WebView2 profile.`;
      } catch (error) {
        outcomeMessage = `${language.name} could not be downloaded: ${error?.message || error}. Check the connection, then retry.`;
      } finally {
        if (worker) { try { await worker.terminate(); } catch (_) {} }
        ocrLanguageInstallBusy = false; renderOcrLanguageSettings();
        if (outcomeMessage) ocrLanguageStatus.textContent = outcomeMessage;
      }
    }
    function pdfPageTextLooksUsable(value) {
      const text = String(value || '').replace(/\u0000/g, '').trim();
      const visible = text.replace(/\s/gu, '');
      if (visible.length < 24) return false;
      const meaningful = (visible.match(/[\p{L}\p{N}]/gu) || []).length;
      const replacements = (visible.match(/[\uFFFD\u25A1\u25AF]/gu) || []).length;
      const words = text.match(/[\p{L}\p{N}]{2,}/gu) || [];
      return meaningful >= 16 && words.length >= 4 && meaningful / visible.length >= 0.45 && replacements === 0;
    }
    function pdfJsItemsToText(items) {
      const lines = []; let line = '';
      for (const item of items || []) {
        if (typeof item.str === 'string') line += item.str;
        if (item.hasEOL) { if (line.trim()) lines.push(line.trimEnd()); line = ''; }
      }
      if (line.trim()) lines.push(line.trimEnd());
      return lines.join('\n').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
    }
    function throwIfPdfCancelled(signal) {
      if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
    }
    async function renderPdfPageForOcr(page, signal) {
      throwIfPdfCancelled(signal);
      const baseViewport = page.getViewport({ scale: 1 });
      const maxPixels = 12_000_000;
      const basePixels = Math.max(1, baseViewport.width * baseViewport.height);
      const scale = Math.min(4, Math.sqrt(maxPixels / basePixels));
      if (scale < 0.25) throw new Error('A PDF page is too large to render safely for OCR.');
      const viewport = page.getViewport({ scale });
      if (viewport.width > 6000 || viewport.height > 6000 || viewport.width * viewport.height > maxPixels * 1.02) throw new Error('A PDF page is too large to render safely for OCR.');
      const canvas = document.createElement('canvas');
      canvas.width = Math.ceil(viewport.width); canvas.height = Math.ceil(viewport.height);
      const context = canvas.getContext('2d', { alpha: false });
      if (!context) throw new Error('Could not create a canvas for PDF OCR.');
      const renderTask = page.render({ canvasContext: context, viewport, background: '#ffffff' });
      const cancel = () => { try { renderTask.cancel(); } catch (_) {} };
      signal?.addEventListener('abort', cancel, { once: true });
      try { await renderTask.promise; throwIfPdfCancelled(signal); return canvas; }
      catch (error) { if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError'); throw error; }
      finally { signal?.removeEventListener('abort', cancel); }
    }
    async function readWorkspacePdf(args, signal) {
      const input = await readTaskFile(args.path, 25 * 1024 * 1024); const file = input.file;
      if (file.size > 25 * 1024 * 1024) throw new Error('PDF reading is limited to 25 MB per file.');
      const bytes = new Uint8Array(await file.arrayBuffer());
      if (bytes.length < 5 || new TextDecoder('ascii').decode(bytes.subarray(0, 5)) !== '%PDF-') throw new Error('The selected file does not have a valid PDF header.');
      throwIfPdfCancelled(signal);
      const pdfjs = await loadPdfJs();
      const loadingTask = pdfjs.getDocument({
        data: bytes,
        cMapUrl: `${OCR_PDFJS_BASE}/cmaps/`,
        cMapPacked: true,
        iccUrl: `${OCR_PDFJS_BASE}/iccs/`,
        standardFontDataUrl: `${OCR_PDFJS_BASE}/standard_fonts/`,
        wasmUrl: `${OCR_PDFJS_BASE}/wasm/`,
        isEvalSupported: false,
        useSystemFonts: true
      });
      let pdf = null; let ocrWorker = null; let cancelOcrWorker = null;
      try {
        pdf = await loadingTask.promise;
        if (pdf.numPages > 500) throw new Error('PDF reading is limited to 500 pages per file.');
        const metadata = await pdf.getMetadata().catch(() => null);
        const title = String(metadata?.info?.Title || '').slice(0, 500);
        const pages = new Array(pdf.numPages);
        const ocrPageNumbers = [];
        for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) {
          throwIfPdfCancelled(signal);
          const page = await pdf.getPage(pageNumber);
          try {
            let text = '';
            try {
              const content = await page.getTextContent({ includeMarkedContent: false });
              text = pdfJsItemsToText(content.items);
            } catch (error) {
              if (signal?.aborted || error?.name === 'AbortError') throw error;
            }
            pages[pageNumber - 1] = { page: pageNumber, text, method: 'pdfjs', confidence: null };
            if (!pdfPageTextLooksUsable(text)) ocrPageNumbers.push(pageNumber);
          } finally { page.cleanup(); }
        }
        const settings = loadOcrLanguageSettings();
        const languages = settings.selected.length ? settings.selected : ['eng'];
        const optionalMissing = languages.filter(code => !['eng', 'fas'].includes(code) && !settings.installed.includes(code));
        if (ocrPageNumbers.length && optionalMissing.length) {
          throw new Error(`OCR needs language pack(s) ${optionalMissing.join(', ')}. Open PDF OCR languages and download the selected pack(s), then retry.`);
        }
        if (ocrPageNumbers.length) {
          showToast(`PDF text found on ${pdf.numPages - ocrPageNumbers.length}/${pdf.numPages} pages. OCR is processing ${ocrPageNumbers.length} page(s)…`);
          try {
            ocrWorker = await createTesseractWorker(languages, message => {
              if (message?.status && message.status !== 'recognizing text') return;
              const percent = Number.isFinite(message?.progress) ? ` ${Math.round(message.progress * 100)}%` : '';
              showToast(`OCR ${message?.status || 'processing'}${percent}`);
            });
            cancelOcrWorker = () => { if (ocrWorker) void ocrWorker.terminate().catch(() => {}); };
            signal?.addEventListener('abort', cancelOcrWorker, { once: true });
          } catch (error) {
            const optionalSelected = languages.filter(code => !['eng', 'fas'].includes(code));
            if (optionalSelected.length) throw new Error(`Could not load OCR language pack(s) ${optionalSelected.join(', ')}. The WebView2 cache may have been cleared; open PDF OCR languages and download the pack(s) again. ${error?.message || error}`);
            throw error;
          }
          for (let index = 0; index < ocrPageNumbers.length; index++) {
            throwIfPdfCancelled(signal);
            const pageNumber = ocrPageNumbers[index];
            const page = await pdf.getPage(pageNumber);
            let canvas = null;
            try {
              canvas = await renderPdfPageForOcr(page, signal);
              const result = await ocrWorker.recognize(canvas);
              throwIfPdfCancelled(signal);
              const recognized = String(result?.data?.text || '').trim();
              pages[pageNumber - 1] = { page: pageNumber, text: recognized, method: 'ocr', confidence: Number.isFinite(result?.data?.confidence) ? result.data.confidence : null };
            } finally {
              if (canvas) { canvas.width = 0; canvas.height = 0; }
              page.cleanup();
            }
            showToast(`OCR page ${index + 1}/${ocrPageNumbers.length} (PDF page ${pageNumber})`);
          }
        }
        const lowConfidencePages = pages.filter(item => item.method === 'ocr' && (item.confidence == null || item.confidence < 55)).map(item => item.page);
        const textPages = pages.filter(item => item.method === 'pdfjs').map(item => item.page);
        const ocrPages = pages.filter(item => item.method === 'ocr').map(item => item.page);
        const text = pages.map(item => `--- Page ${item.page} · ${item.method.toUpperCase()}${item.confidence == null ? '' : ` · confidence ${Math.round(item.confidence)}%`} ---\n${item.text || '[No readable text found on this page.]'}`).join('\n\n');
        const combined = text.slice(0, 90000);
        const methods = ocrPages.length ? 'pdfjs+ocr' : 'pdfjs';
        const result = {
          path: args.path, source: input.source, title, pageCount: pdf.numPages, bytes: file.size,
          method: methods, languages, textPages, ocrPages, lowConfidencePages,
          characters: text.length, text: combined, truncated: text.length > combined.length,
          note: ocrPages.length
            ? `PDF.js extracted text and OCR processed pages ${ocrPages.join(', ')} using ${languages.join('+')}. Low-confidence or unreadable OCR pages: ${lowConfidencePages.length ? lowConfidencePages.join(', ') : 'none reported'}. Language detection is not automatic. Verify OCR against the original, especially for complex layouts, handwriting, low-resolution scans, and right-to-left documents.`
            : 'PDF.js extracted selectable text. No OCR was needed based on per-page text-quality checks. Scanned content embedded alongside substantial selectable text may not be OCRed automatically; verify layout-sensitive documents against the original.'
        };
        return result;
      } finally {
        if (cancelOcrWorker) signal?.removeEventListener('abort', cancelOcrWorker);
        if (ocrWorker) { try { await ocrWorker.terminate(); } catch (_) {} }
        if (pdf) { try { await pdf.destroy(); } catch (_) {} }
        else { try { await loadingTask.destroy(); } catch (_) {} }
      }
    }
    if (ocrLanguageMenu) ocrLanguageMenu.addEventListener('toggle', () => { if (ocrLanguageMenu.open) renderOcrLanguageSettings(); });
    const ZIP_CRC_TABLE = (() => { const table = new Uint32Array(256); for (let index = 0; index < 256; index++) { let value = index; for (let bit = 0; bit < 8; bit++) value = value & 1 ? 0xEDB88320 ^ (value >>> 1) : value >>> 1; table[index] = value >>> 0; } return table; })();
    function zipCrc32(bytes) { let crc = 0xFFFFFFFF; for (const byte of bytes) crc = ZIP_CRC_TABLE[(crc ^ byte) & 255] ^ (crc >>> 8); return (crc ^ 0xFFFFFFFF) >>> 0; }
    function zipSafeEntryPath(value, directory) {
      const path = String(value || '').replace(/\\/g, '/').replace(/\/$/, '');
      if (!path || path.startsWith('/') || /^[a-z]:/i.test(path) || path.includes('\u0000') || path.split('/').some(part => !part || part === '.' || part === '..' || /[<>:"|?*]/.test(part) || /[. ]$/.test(part) || /^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?$/i.test(part))) throw new Error(`Unsafe ZIP entry path: '${value}'.`);
      return { path, parts: path.split('/'), directory };
    }
    async function parseWorkspaceZip(args, signal) {
      const input = await readTaskFile(args.path, 50 * 1024 * 1024); const file = input.file;
      if (file.size > 50 * 1024 * 1024) throw new Error('ZIP inspection is limited to 50 MB per archive.');
      const bytes = new Uint8Array(await file.arrayBuffer()); const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
      const read16 = offset => view.getUint16(offset, true); const read32 = offset => view.getUint32(offset, true);
      let eocd = -1; const searchStart = Math.max(0, bytes.length - 65557);
      for (let offset = bytes.length - 22; offset >= searchStart; offset--) if (read32(offset) === 0x06054B50) { eocd = offset; break; }
      if (eocd < 0) throw new Error('The file is not a supported ZIP archive (end-of-central-directory record not found).');
      const disk = read16(eocd + 4), directoryDisk = read16(eocd + 6), entriesOnDisk = read16(eocd + 8), entryCount = read16(eocd + 10), directorySize = read32(eocd + 12), directoryOffset = read32(eocd + 16);
      if (disk || directoryDisk || entriesOnDisk !== entryCount) throw new Error('Multi-disk ZIP archives are not supported.');
      if (entryCount === 0xFFFF || directorySize === 0xFFFFFFFF || directoryOffset === 0xFFFFFFFF) throw new Error('ZIP64 archives are not supported by this build.');
      if (entryCount > 500) throw new Error('ZIP archives are limited to 500 entries.');
      if (directoryOffset + directorySize > eocd || directoryOffset >= eocd) throw new Error('ZIP central directory offsets are invalid.');
      const entries = []; const seen = new Set(); let offset = directoryOffset; let totalBytes = 0;
      for (let index = 0; index < entryCount; index++) {
        if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
        if (offset + 46 > bytes.length || read32(offset) !== 0x02014B50) throw new Error(`Invalid ZIP central directory record at entry ${index + 1}.`);
        const flags = read16(offset + 8), method = read16(offset + 10), crc = read32(offset + 16), compressedSize = read32(offset + 20), size = read32(offset + 24), nameLength = read16(offset + 28), extraLength = read16(offset + 30), commentLength = read16(offset + 32), startDisk = read16(offset + 34), externalAttributes = read32(offset + 38), localOffset = read32(offset + 42);
        if (startDisk !== 0) throw new Error('Multi-disk ZIP entry records are not supported.');
        if (compressedSize === 0xFFFFFFFF || size === 0xFFFFFFFF || localOffset === 0xFFFFFFFF) throw new Error('ZIP64 entries are not supported by this build.');
        const nameStart = offset + 46; if (nameStart + nameLength + extraLength + commentLength > bytes.length) throw new Error('ZIP entry metadata is truncated.');
        const nameBytes = bytes.subarray(nameStart, nameStart + nameLength); const isDirectory = nameBytes[nameBytes.length - 1] === 47 || nameBytes[nameBytes.length - 1] === 92;
        const name = new TextDecoder(flags & 0x0800 ? 'utf-8' : 'windows-1252').decode(nameBytes);
        const safe = zipSafeEntryPath(name, isDirectory); const collisionKey = safe.path.toLowerCase();
        if (seen.has(collisionKey)) throw new Error(`ZIP contains duplicate or case-colliding path '${safe.path}'.`); seen.add(collisionKey);
        const unixMode = externalAttributes >>> 16; const fileType = unixMode & 0xF000;
        if (fileType === 0xA000) throw new Error(`ZIP symbolic links are not extracted ('${safe.path}').`);
        if (!isDirectory && size > 25 * 1024 * 1024) throw new Error(`ZIP entry '${safe.path}' exceeds the 25 MB per-file limit.`);
        if (compressedSize > 0 && size / compressedSize > 200) throw new Error(`ZIP entry '${safe.path}' exceeds the 200:1 compression-ratio limit.`);
        totalBytes += size; if (totalBytes > 100 * 1024 * 1024) throw new Error('ZIP expanded size exceeds the 100 MB total limit.');
        entries.push({ path: safe.path, directory: isDirectory || fileType === 0x4000, size, compressedSize, method, crc32: crc.toString(16).padStart(8, '0'), encrypted: !!(flags & 1), localOffset, flags });
        offset = nameStart + nameLength + extraLength + commentLength;
      }
      return { path: args.path, source: input.source, archiveBytes: file.size, entryCount, totalUncompressedBytes: totalBytes, entries };
    }
    async function decompressZipEntry(archiveBytes, entry, signal) {
      if (entry.encrypted) throw new Error(`ZIP entry '${entry.path}' is encrypted and cannot be extracted.`);
      if (![0, 8].includes(entry.method)) throw new Error(`ZIP entry '${entry.path}' uses unsupported compression method ${entry.method}.`);
      const view = new DataView(archiveBytes.buffer, archiveBytes.byteOffset, archiveBytes.byteLength); const offset = entry.localOffset;
      if (offset + 30 > archiveBytes.length || view.getUint32(offset, true) !== 0x04034B50) throw new Error(`ZIP local header is invalid for '${entry.path}'.`);
      const nameLength = view.getUint16(offset + 26, true), extraLength = view.getUint16(offset + 28, true), start = offset + 30 + nameLength + extraLength, end = start + entry.compressedSize;
      if (end > archiveBytes.length) throw new Error(`Compressed data is truncated for '${entry.path}'.`);
      let output = archiveBytes.slice(start, end);
      if (entry.method === 8) {
        if (typeof DecompressionStream !== 'function') throw new Error('This WebView does not support ZIP deflate decompression.');
        const reader = new Blob([output]).stream().pipeThrough(new DecompressionStream('deflate-raw')).getReader(); const chunks = []; let total = 0;
        try { while (true) { if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError'); const { done, value } = await reader.read(); if (done) break; total += value.byteLength; if (total > 25 * 1024 * 1024 || total > entry.size) throw new Error(`Expanded ZIP entry '${entry.path}' exceeds its declared size or safety limit.`); chunks.push(value); } } finally { reader.releaseLock(); }
        output = new Uint8Array(total); let at = 0; for (const chunk of chunks) { output.set(chunk, at); at += chunk.length; }
      }
      if (output.length !== entry.size) throw new Error(`Expanded size mismatch for '${entry.path}' (expected ${entry.size}, received ${output.length}).`);
      const checksum = zipCrc32(output).toString(16).padStart(8, '0'); if (checksum !== entry.crc32) throw new Error(`CRC-32 validation failed for '${entry.path}'. The archive may be corrupt.`);
      return output;
    }
    async function inferCsvSchema(args) {
      const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024); const dataset = csvDataset(args.path, text, resolveCsvDelimiter(args.path, args.delimiter, text));
      const rows = dataset.rows.slice(0, 10000); const fields = dataset.headers.map((name, index) => {
        const values = rows.map(row => String(row[index] ?? '').trim()).filter(value => value !== '');
        const type = values.length ? values.every(value => /^(?:true|false)$/i.test(value)) ? 'boolean' : values.every(value => /^[-+]?\d+$/.test(value)) ? 'integer' : values.every(value => value !== '' && Number.isFinite(Number(value))) ? 'number' : values.every(value => /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(`${value}T00:00:00Z`))) ? 'date' : 'string' : 'null';
        return { name, columnIndex: index, inferredType: type, nullable: values.length < rows.length, nonEmptyValues: values.length, uniqueValues: new Set(values).size, sample: [...new Set(values)].slice(0, 5) };
      });
      return { path: args.path, rowsAnalyzed: rows.length, totalRows: dataset.rows.length, columns: fields, note: 'Inference is heuristic and uses at most the first 10,000 data rows. Review before using the result as a validation contract.' };
    }
    async function joinCsvDatasets(args, signal) {
      const aText = await readWorkspaceText(args.path_a, 10 * 1024 * 1024);
      const bText = await readWorkspaceText(args.path_b, 10 * 1024 * 1024);
      const aggregateInputBytes = aText.file.size + bText.file.size;
      if (aggregateInputBytes > CSV_JOIN_INPUT_BYTES_LIMIT) throw new Error('Joined CSV inputs exceed the 20 MB aggregate input-size limit.');
      const delimiterA = resolveCsvDelimiter(args.path_a, args.delimiter_a || args.delimiter, aText.text); const delimiterB = resolveCsvDelimiter(args.path_b, args.delimiter_b || args.delimiter, bText.text);
      const a = csvDataset(args.path_a, aText.text, delimiterA), b = csvDataset(args.path_b, bText.text, delimiterB);
      const keyA = datasetColumnIndex(a.headers, args.key_a), keyB = datasetColumnIndex(b.headers, args.key_b); const joinType = String(args.join_type || 'inner').toLowerCase();
      if (!['inner','left','full'].includes(joinType)) throw new Error('join_type must be inner, left, or full.');
      const outputHeaders = [...a.headers];
      for (const original of b.headers) { let candidate = a.headers.some(item => item.toLowerCase() === original.toLowerCase()) ? `b_${original}` : original; let suffix = 2; while (outputHeaders.some(item => item.toLowerCase() === candidate.toLowerCase())) candidate = `b_${original}_${suffix++}`; outputHeaders.push(candidate); }
      const index = new Map();
      for (let rowIndex = 0; rowIndex < b.rows.length; rowIndex++) {
        if ((rowIndex & 255) === 0 && signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
        const row = b.rows[rowIndex], key = String(row[keyB] ?? '');
        if (!key) continue;
        if (!index.has(key)) index.set(key, []);
        index.get(key).push({ row, rowIndex });
      }
      const matched = new Set();
      const outputLines = [csvRowsToText([outputHeaders], delimiterA)];
      let outputBytes = new Blob([outputLines[0]]).size;
      let outputRowCount = 0, unmatchedLeftRows = 0, matchOperations = 0;
      const appendOutputRow = row => {
        const line = csvRowsToText([row], delimiterA);
        outputBytes += new Blob([line]).size + 2;
        if (outputBytes > CSV_OUTPUT_BYTES_LIMIT) throw new Error('Joined CSV output exceeds the 50 MB output-size limit. Refine the join keys or input datasets.');
        if (outputRowCount >= 100000) throw new Error('Joined dataset would exceed 100,000 output rows. Refine the join keys or input datasets.');
        outputLines.push(line); outputRowCount++;
      };
      for (const rowA of a.rows) {
        if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
        const key = String(rowA[keyA] ?? ''); const matches = key ? (index.get(key) || []) : [];
        if (matches.length) {
          for (const item of matches) {
            if ((matchOperations++ & 255) === 0 && signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
            matched.add(item.rowIndex); appendOutputRow([...rowA, ...item.row]);
          }
        } else {
          unmatchedLeftRows++;
          if (joinType !== 'inner') appendOutputRow([...rowA, ...b.headers.map(() => '')]);
        }
      }
      if (joinType === 'full') {
        for (let rowIndex = 0; rowIndex < b.rows.length; rowIndex++) {
          if ((rowIndex & 255) === 0 && signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
          if (!matched.has(rowIndex)) appendOutputRow([...a.headers.map(() => ''), ...b.rows[rowIndex]]);
        }
      }
      const destination = String(args.destination || '').trim(); if (!destination) throw new Error('A destination CSV path is required.');
      const content = outputLines.join('\r\n');
      const approved = await requireAgentApproval({ title: 'Create joined CSV dataset?', subtitle: `New file · ${joinType} join · existing files will not be overwritten`, detail: `${args.path_a} (${a.rows.length} rows)\n${args.path_b} (${b.rows.length} rows)\nKeys: ${args.key_a} = ${args.key_b}\nOutput: ${destination} · ${outputRowCount} rows · ${outputBytes.toLocaleString()} bytes`, risky: true }, signal);
      if (!approved) return { cancelled: true };
      const written = await writeWorkspaceFile(destination, new Blob([content], { type: 'text/csv;charset=utf-8' }), false);
      return { created: written.path, rows: outputRowCount, columns: outputHeaders.length, joinType, inputBytes: aggregateInputBytes, bytes: written.bytes, unmatchedLeftRows: unmatchedLeftRows, unmatchedRightRows: joinType === 'full' ? b.rows.length - matched.size : undefined };
    }
    function classifyConnectionFailure(error) {
      const message = String(error?.message || error || 'Unknown error'); const lower = message.toLowerCase();
      if (/abort|timed? ?out|deadline|exceeded.*time/.test(lower)) return 'timeout';
      if (/401|unauthorized|invalid api key|authentication/.test(lower)) return 'authentication';
      if (/403|forbidden/.test(lower)) return 'forbidden';
      if (/404|not found/.test(lower)) return 'endpoint-or-route';
      if (/429|rate limit/.test(lower)) return 'rate-limit';
      if (/5\d\d|server error/.test(lower)) return 'provider-server';
      if (/failed to fetch|networkerror|network request|cors|load failed/.test(lower)) return 'network-or-cors';
      if (/invalid json|response has no|malformed/.test(lower)) return 'protocol-or-response';
      return 'unknown';
    }
    function redactConnectionUrl(value) {
      try { const url = new URL(normalizeBase(value)); return `${url.origin}${url.pathname}`; }
      catch (_) { return '(invalid endpoint URL)'; }
    }
    async function diagnoseModelConnection(signal, selection = selectedModel()) {
      const connection = selection?.connection || activeModelConnection || modelConnections.find(item => item.enabled !== false);
      if (!connection) return { connected: false, classification: 'configuration', diagnosis: 'No model server connection is configured.' };
      const baseUrl = redactConnectionUrl(connection.baseUrl); const started = performance.now();
      try {
        if (connection.enabled === false) return { connected: false, classification: 'configuration', connection: connection.name, baseUrl, diagnosis: 'This connection is disabled.' };
        const provider = PROVIDERS[connection.type]; if (!provider) throw new Error(`Unsupported provider type '${connection.type}'.`);
        const models = await provider.discoverModels(connection);
        return { connected: true, classification: 'ok', connection: connection.name, provider: connection.type, baseUrl, selectedModel: selection?.model || null, selectedModelAvailable: selection?.model ? models.includes(selection.model) : null, modelCount: models.length, models: models.slice(0, 30), elapsedMs: Math.round(performance.now() - started), authenticationConfigured: !!connection.apiKey, scope: 'Model discovery endpoint only; this does not send a generation request or prove that the selected model supports tool calling.' };
      } catch (error) {
        if (signal?.aborted || error?.name === 'AbortError') throw new DOMException('Diagnostics cancelled.', 'AbortError');
        return { connected: false, classification: classifyConnectionFailure(error), connection: connection.name, provider: connection.type, baseUrl, selectedModel: selection?.model || null, elapsedMs: Math.round(performance.now() - started), authenticationConfigured: !!connection.apiKey, error: String(error?.message || error), guidance: 'Network/CORS failures can look identical in WebView2. Confirm the service is listening, the URL and API path are correct, the server permits the https://localhost origin, and any configured key/header is valid.' };
      }
    }

    async function runSelectedModelDiagnostics() {
      const selection = snapshotModelSelection();
      if (!selection?.model || !selection.connection) {
        showToast('Select a model connection before running diagnostics.'); return;
      }
      if (researchDiagnosticsController) researchDiagnosticsController.abort();
      const controller = new AbortController(); researchDiagnosticsController = controller;
      researchRunDiagnosticsButton.disabled = true; researchDiagnosticsResult.replaceChildren();
      const appendResult = (label, status, detail) => {
        const row = document.createElement('div'); row.className = 'research-diagnostic-line';
        const left = document.createElement('span'); left.textContent = label;
        const right = document.createElement('span'); right.textContent = `${status}${detail ? ` · ${detail}` : ''}`;
        row.append(left, right); researchDiagnosticsResult.appendChild(row);
      };
      const connection = selection.connection;
      const base = normalizeBase(connection.baseUrl);
      const headers = { 'Content-Type': 'application/json', 'Accept': 'application/json', ...modelAuthorizationHeaders(connection) };
      const timedFetch = async (url, body, timeoutMs = 45000) => {
        const requestController = new AbortController();
        const abort = () => requestController.abort(controller.signal.reason || new DOMException('Diagnostics cancelled.', 'AbortError'));
        if (controller.signal.aborted) abort(); else controller.signal.addEventListener('abort', abort, { once: true });
        const timeout = setTimeout(() => requestController.abort(new DOMException('Diagnostic request timed out.', 'TimeoutError')), timeoutMs);
        try {
          const response = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body), signal: requestController.signal });
          const text = await readBoundedResponseText(response, 2 * 1024 * 1024);
          if (!response.ok) throw new Error(`HTTP ${response.status}: ${text.slice(0, 320)}`);
          let data; try { data = JSON.parse(text); } catch (_) { throw new Error('Provider returned malformed JSON.'); }
          return data;
        } finally {
          clearTimeout(timeout); controller.signal.removeEventListener('abort', abort);
        }
      };
      try {
        appendResult('Connection discovery', 'Running');
        const discovery = await diagnoseModelConnection(controller.signal, selection);
        researchDiagnosticsResult.lastChild?.remove();
        appendResult('Connection discovery', discovery.connected ? 'PASS' : 'FAIL', discovery.connected ? `${discovery.modelCount} models · ${discovery.elapsedMs} ms` : `${discovery.classification}: ${discovery.error || discovery.diagnosis}`);
        if (!discovery.connected) return;
        if (discovery.selectedModelAvailable === false) appendResult('Selected model', 'WARNING', 'The selected model is not listed by this connection.');
        else appendResult('Selected model', discovery.selectedModelAvailable === true ? 'PASS' : 'UNKNOWN', discovery.selectedModelAvailable === true ? selection.model : 'Discovery did not confirm model membership.');
        if (selection.provider === 'lmstudio' && (selection.lmStudioApiMode || settingLmStudioApiMode.value) === 'native') {
          appendResult('Text generation', 'NOT TESTED', 'Native REST diagnostic generation is not implemented in this release.');
          appendResult('Tool calling', 'NOT APPLICABLE', 'RunMind Agent and Deep Research require LM Studio OpenAI-compatible mode.');
          return;
        }
        const generationStarted = performance.now();
        try {
          appendResult('Text generation', 'Running');
          const generationUrl = selection.provider === 'ollama' ? `${base}/api/chat` : `${base.replace(/\/$/, '')}/chat/completions`;
          const generationBody = selection.provider === 'ollama'
            ? { model: selection.model, messages: [{ role: 'user', content: 'Reply with exactly RUNMIND_OK.' }], stream: false, think: false, options: { num_predict: 12, temperature: 0 } }
            : { model: selection.model, messages: [{ role: 'user', content: 'Reply with exactly RUNMIND_OK.' }], stream: false, max_tokens: 12, temperature: 0 };
          const generated = await timedFetch(generationUrl, generationBody);
          const generatedText = selection.provider === 'ollama' ? generated?.message?.content : generated?.choices?.[0]?.message?.content;
          if (typeof generatedText !== 'string' || !generatedText.trim()) throw new Error('Provider returned no assistant text.');
          appendResult('Text generation', 'PASS', `${Math.round(performance.now() - generationStarted)} ms · response received`);
        } catch (error) {
          if (controller.signal.aborted) throw error;
          appendResult('Text generation', 'FAIL', `${classifyConnectionFailure(error)}: ${String(error?.message || error).slice(0, 220)}`);
          appendResult('Tool calling', 'NOT TESTED', 'Skipped because the generation probe failed.');
          return;
        }
        try {
          appendResult('Tool calling', 'Running');
          const tool = { type: 'function', function: { name: 'runmind_capability_probe', description: 'Diagnostic-only function. Return the provided marker as arguments.', parameters: { type: 'object', properties: { marker: { type: 'string' } }, required: ['marker'], additionalProperties: false } } };
          const toolUrl = selection.provider === 'ollama' ? `${base}/api/chat` : `${base.replace(/\/$/, '')}/chat/completions`;
          const toolBody = selection.provider === 'ollama'
            ? { model: selection.model, messages: [{ role: 'user', content: 'Call runmind_capability_probe with marker set to RUNMIND_TOOL_OK.' }], tools: [tool], stream: false, think: false, options: { num_predict: 64, temperature: 0 } }
            : { model: selection.model, messages: [{ role: 'user', content: 'Call runmind_capability_probe with marker set to RUNMIND_TOOL_OK.' }], tools: [tool], tool_choice: { type: 'function', function: { name: 'runmind_capability_probe' } }, stream: false, max_tokens: 64, temperature: 0 };
          const probed = await timedFetch(toolUrl, toolBody);
          const message = selection.provider === 'ollama' ? probed?.message : probed?.choices?.[0]?.message;
          const calls = message?.tool_calls || [];
          const matchedCall = calls.find(call => (call?.function?.name || call?.name) === 'runmind_capability_probe')
            || (message?.function_call?.name === 'runmind_capability_probe' ? message.function_call : null);
          let probeArgs = matchedCall?.function?.arguments ?? matchedCall?.arguments ?? '{}';
          if (typeof probeArgs === 'string') { try { probeArgs = JSON.parse(probeArgs); } catch (_) { probeArgs = {}; } }
          if (matchedCall && probeArgs?.marker === 'RUNMIND_TOOL_OK') appendResult('Tool calling', 'PASS', 'The model returned the requested function and marker; it was not executed.');
          else appendResult('Tool calling', 'NOT CONFIRMED', 'The provider did not return the expected function name and marker.');
        } catch (error) {
          if (controller.signal.aborted) throw error;
          appendResult('Tool calling', 'FAIL', `${classifyConnectionFailure(error)}: ${String(error?.message || error).slice(0, 220)}`);
        }
      } catch (error) {
        if (error?.name === 'AbortError') appendResult('Diagnostics', 'CANCELLED', 'The diagnostic request was cancelled.');
        else appendResult('Diagnostics', 'ERROR', String(error?.message || error).slice(0, 260));
      } finally {
        if (researchDiagnosticsController === controller) researchDiagnosticsController = null;
        researchRunDiagnosticsButton.disabled = false;
      }
    }

    async function readWorkspaceImageForModel(args) {
      if (!agentDirectoryHandle) throw new Error('No task folder selected.');
      const { handle } = await getFileAtPath(agentDirectoryHandle, args.path);
      const file = await handle.getFile();
      if (file.size > 8 * 1024 * 1024) throw new Error('Image understanding is limited to 8 MB per image.');
      const mimeType = String(file.type || '').toLowerCase();
      if (!['image/png', 'image/jpeg', 'image/webp', 'image/gif'].includes(mimeType)) {
        throw new Error('Supported image types for model analysis are PNG, JPEG, WebP, and GIF.');
      }
      const bitmap = await createImageBitmap(file);
      let width, height;
      try { width = bitmap.width; height = bitmap.height; }
      finally { bitmap.close?.(); }
      const dataUrl = await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result || ''));
        reader.onerror = () => reject(reader.error || new Error('Could not read image bytes.'));
        reader.readAsDataURL(file);
      });
      if (!/^data:image\/(?:png|jpeg|webp|gif);base64,/i.test(dataUrl)) throw new Error('The selected image could not be encoded for the model.');
      return {
        path: args.path, mimeType, bytes: file.size, width, height,
        message: 'The image is attached to the next model turn. Visual interpretation requires a vision-capable model.',
        __runMindModelImage: dataUrl
      };
    }

