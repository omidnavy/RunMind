(function (root) {
  'use strict';

  const encoder = new TextEncoder();
  const xml = value => {
    let clean = '';
    for (const character of String(value ?? '')) {
      const code = character.codePointAt(0);
      if (code === 0x09 || code === 0x0A || code === 0x0D || (code >= 0x20 && code <= 0xD7FF) || (code >= 0xE000 && code <= 0xFFFD) || (code >= 0x10000 && code <= 0x10FFFF)) clean += character;
    }
    return clean.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
  };
  const utf8 = value => encoder.encode(String(value ?? ''));

  const crcTable = (() => {
    const table = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let value = n;
      for (let bit = 0; bit < 8; bit++) value = value & 1 ? 0xEDB88320 ^ (value >>> 1) : value >>> 1;
      table[n] = value >>> 0;
    }
    return table;
  })();

  function crc32(bytes) {
    let crc = 0xFFFFFFFF;
    for (const byte of bytes) crc = crcTable[(crc ^ byte) & 255] ^ (crc >>> 8);
    return (crc ^ 0xFFFFFFFF) >>> 0;
  }

  function concatBytes(parts) {
    const size = parts.reduce((total, part) => total + part.length, 0);
    const output = new Uint8Array(size);
    let offset = 0;
    for (const part of parts) { output.set(part, offset); offset += part.length; }
    return output;
  }

  function zipStored(entries) {
    if (!Array.isArray(entries) || !entries.length || entries.length > 100) throw new Error('An Open XML package must contain between 1 and 100 ZIP entries.');
    const localParts = [];
    const centralParts = [];
    let offset = 0;
    let totalBytes = 0;
    for (const entry of entries) {
      const name = utf8(entry.name);
      const data = entry.data instanceof Uint8Array ? entry.data : utf8(entry.data);
      if (!name.length || name.length > 65535 || data.length > 0xFFFFFFFF) throw new Error(`Invalid package entry '${entry.name}'.`);
      totalBytes += data.length;
      if (totalBytes > 25 * 1024 * 1024) throw new Error('Generated Office document exceeds the 25 MB package limit.');
      const checksum = crc32(data);
      const local = new Uint8Array(30 + name.length);
      const localView = new DataView(local.buffer);
      localView.setUint32(0, 0x04034B50, true);
      localView.setUint16(4, 20, true);
      localView.setUint16(6, 0x0800, true);
      localView.setUint16(8, 0, true);
      localView.setUint16(10, 0, true);
      localView.setUint16(12, 0, true);
      localView.setUint32(14, checksum, true);
      localView.setUint32(18, data.length, true);
      localView.setUint32(22, data.length, true);
      localView.setUint16(26, name.length, true);
      localView.setUint16(28, 0, true);
      local.set(name, 30);
      localParts.push(local, data);

      const central = new Uint8Array(46 + name.length);
      const centralView = new DataView(central.buffer);
      centralView.setUint32(0, 0x02014B50, true);
      centralView.setUint16(4, 0x0314, true);
      centralView.setUint16(6, 20, true);
      centralView.setUint16(8, 0x0800, true);
      centralView.setUint16(10, 0, true);
      centralView.setUint16(12, 0, true);
      centralView.setUint16(14, 0, true);
      centralView.setUint32(16, checksum, true);
      centralView.setUint32(20, data.length, true);
      centralView.setUint32(24, data.length, true);
      centralView.setUint16(28, name.length, true);
      centralView.setUint16(30, 0, true);
      centralView.setUint16(32, 0, true);
      centralView.setUint16(34, 0, true);
      centralView.setUint16(36, 0, true);
      centralView.setUint32(38, 0, true);
      centralView.setUint32(42, offset, true);
      central.set(name, 46);
      centralParts.push(central);
      offset += local.length + data.length;
    }
    const centralDirectory = concatBytes(centralParts);
    const end = new Uint8Array(22);
    const endView = new DataView(end.buffer);
    endView.setUint32(0, 0x06054B50, true);
    endView.setUint16(4, 0, true);
    endView.setUint16(6, 0, true);
    endView.setUint16(8, entries.length, true);
    endView.setUint16(10, entries.length, true);
    endView.setUint32(12, centralDirectory.length, true);
    endView.setUint32(16, offset, true);
    endView.setUint16(20, 0, true);
    return concatBytes([...localParts, centralDirectory, end]);
  }

  function markdownRuns(value) {
    const parts = String(value ?? '').split(/(\*\*[^*]+\*\*|\*[^*]+\*|`[^`]+`)/g).filter(Boolean);
    return parts.map(part => {
      let text = part, properties = '';
      if (part.startsWith('**') && part.endsWith('**')) { text = part.slice(2, -2); properties = '<w:b/>'; }
      else if (part.startsWith('*') && part.endsWith('*')) { text = part.slice(1, -1); properties = '<w:i/>'; }
      else if (part.startsWith('`') && part.endsWith('`')) { text = part.slice(1, -1); properties = '<w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:shd w:fill="F2F3F5"/>'; }
      return `<w:r>${properties ? `<w:rPr>${properties}</w:rPr>` : ''}<w:t xml:space="preserve">${xml(text)}</w:t></w:r>`;
    }).join('');
  }

  function docxParagraph(text, style = '', numbering = 0, code = false) {
    const styleXml = style ? `<w:pStyle w:val="${style}"/>` : '';
    const numberingXml = numbering ? `<w:numPr><w:ilvl w:val="0"/><w:numId w:val="${numbering}"/></w:numPr>` : '';
    const paragraphProps = styleXml || numberingXml || code ? `<w:pPr>${styleXml}${numberingXml}${code ? '<w:shd w:fill="F4F5F7"/><w:spacing w:before="40" w:after="40"/>' : ''}</w:pPr>` : '';
    const runs = code ? `<w:r><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:sz w:val="19"/></w:rPr><w:t xml:space="preserve">${xml(text)}</w:t></w:r>` : markdownRuns(text);
    return `<w:p>${paragraphProps}${runs || '<w:r><w:t></w:t></w:r>'}</w:p>`;
  }

  function docxTable(rows) {
    const columns = Math.max(1, rows[0]?.length || 0);
    if (rows.some(row => row.length > columns)) throw new Error('DOCX table rows cannot contain more cells than the header row.');
    const grid = `<w:tblGrid>${Array.from({ length: columns }, () => '<w:gridCol w:w="2400"/>').join('')}</w:tblGrid>`;
    const borders = '<w:tblBorders><w:top w:val="single" w:sz="4" w:color="B7C3D0"/><w:left w:val="single" w:sz="4" w:color="B7C3D0"/><w:bottom w:val="single" w:sz="4" w:color="B7C3D0"/><w:right w:val="single" w:sz="4" w:color="B7C3D0"/><w:insideH w:val="single" w:sz="4" w:color="D7DEE7"/><w:insideV w:val="single" w:sz="4" w:color="D7DEE7"/></w:tblBorders>';
    const body = rows.map((row, rowIndex) => `<w:tr>${Array.from({ length: columns }, (_, index) => {
      const value = row[index] == null ? '' : String(row[index]);
      const shading = rowIndex === 0 ? '<w:shd w:fill="EAF0F7"/>' : '';
      const runProps = rowIndex === 0 ? '<w:rPr><w:b/></w:rPr>' : '';
      return `<w:tc><w:tcPr><w:tcW w:w="2400" w:type="dxa"/>${shading}</w:tcPr><w:p><w:r>${runProps}<w:t xml:space="preserve">${xml(value)}</w:t></w:r></w:p></w:tc>`;
    }).join('')}</w:tr>`).join('');
    return `<w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblLayout w:type="autofit"/>${borders}</w:tblPr>${grid}${body}</w:tbl>`;
  }

  function markdownToDocx(content) {
    const lines = String(content ?? '').replace(/\r/g, '').split('\n');
    const output = [];
    let code = false;
    let codeLines = [];
    let tableRows = [];
    const flushCode = () => { if (codeLines.length) output.push(...codeLines.map(line => docxParagraph(line || ' ', '', 0, true))); codeLines = []; };
    const flushTable = () => { if (tableRows.length) output.push(docxTable(tableRows)); tableRows = []; };
    for (const raw of lines) {
      const line = raw.trimEnd();
      if (/^\s*```/.test(line)) { flushTable(); if (code) flushCode(); code = !code; continue; }
      if (code) { codeLines.push(line); continue; }
      if (/^\s*\|.*\|\s*$/.test(line)) {
        const cells = line.trim().replace(/^\||\|$/g, '').split('|').map(cell => cell.trim());
        if (cells.every(cell => /^:?-{2,}:?$/.test(cell))) continue;
        if (tableRows.length >= 100) throw new Error('DOCX tables are limited to 100 rows each.');
        tableRows.push(cells);
        continue;
      }
      flushTable();
      if (!line.trim()) { output.push('<w:p/>'); continue; }
      const heading = line.match(/^\s*(#{1,6})\s+(.+)$/);
      if (heading) { output.push(docxParagraph(heading[2], `Heading${Math.min(6, heading[1].length)}`)); continue; }
      if (/^\s*[-*+]\s+/.test(line)) { output.push(docxParagraph(line.replace(/^\s*[-*+]\s+/, ''), '', 1)); continue; }
      if (/^\s*\d+[.)]\s+/.test(line)) { output.push(docxParagraph(line.replace(/^\s*\d+[.)]\s+/, ''), '', 2)); continue; }
      output.push(docxParagraph(line));
    }
    flushTable();
    if (code) flushCode();
    return output.join('');
  }

  function createDocx(options = {}) {
    const title = String(options.title || 'RunMind document').slice(0, 200);
    const author = String(options.author || 'RunMind').slice(0, 120);
    const content = String(options.content ?? '');
    if (!content.trim()) throw new Error('DOCX content cannot be empty.');
    if (content.length > 1_000_000) throw new Error('DOCX content is limited to 1 MB.');
    const body = `${docxParagraph(title, 'Title')}${markdownToDocx(content)}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>`;
    const documentXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>${body}</w:body></w:document>`;
    const stylesXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/><w:sz w:val="22"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="34"/><w:color w:val="23364D"/></w:rPr><w:pPr><w:spacing w:after="360"/></w:pPr></w:style>${Array.from({ length: 6 }, (_, index) => { const size = Math.max(22, 32 - index * 2); return `<w:style w:type="paragraph" w:styleId="Heading${index + 1}"><w:name w:val="heading ${index + 1}"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:rPr><w:b/><w:sz w:val="${size}"/><w:color w:val="23364D"/></w:rPr><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="120"/></w:pPr></w:style>`; }).join('')}</w:styles>`;
    const numberingXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="singleLevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="•"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum><w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="singleLevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num><w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num></w:numbering>`;
    const now = new Date().toISOString();
    const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/><Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/></Types>`;
    const rootRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>`;
    const documentRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/></Relationships>`;
    const core = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>${xml(title)}</dc:title><dc:creator>${xml(author)}</dc:creator><dcterms:created xsi:type="dcterms:W3CDTF">${now}</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">${now}</dcterms:modified></cp:coreProperties>`;
    const app = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>RunMind</Application></Properties>`;
    return zipStored([
      { name: '[Content_Types].xml', data: contentTypes }, { name: '_rels/.rels', data: rootRels },
      { name: 'word/document.xml', data: documentXml }, { name: 'word/_rels/document.xml.rels', data: documentRels },
      { name: 'word/styles.xml', data: stylesXml }, { name: 'word/numbering.xml', data: numberingXml },
      { name: 'docProps/core.xml', data: core }, { name: 'docProps/app.xml', data: app }
    ]);
  }

  function columnName(index) {
    let value = index + 1, output = '';
    while (value > 0) { const remainder = (value - 1) % 26; output = String.fromCharCode(65 + remainder) + output; value = Math.floor((value - 1) / 26); }
    return output;
  }

  function excelCell(value, reference, style = 0) {
    const styleAttribute = style ? ` s="${style}"` : '';
    if (value == null || value === '') return `<c r="${reference}"${styleAttribute}/>`;
    if (typeof value === 'number') {
      if (!Number.isFinite(value)) throw new Error(`XLSX cell ${reference} contains a non-finite number.`);
      return `<c r="${reference}"${styleAttribute}><v>${String(value)}</v></c>`;
    }
    if (typeof value === 'boolean') return `<c r="${reference}" t="b"${styleAttribute}><v>${value ? 1 : 0}</v></c>`;
    const text = String(value);
    if (text.length > 32000) throw new Error(`XLSX cell ${reference} exceeds Excel's 32,767-character cell limit.`);
    return `<c r="${reference}" t="inlineStr"${styleAttribute}><is><t xml:space="preserve">${xml(text)}</t></is></c>`;
  }

  function createXlsx(options = {}) {
    const sheets = Array.isArray(options.sheets) ? options.sheets : [];
    if (!sheets.length || sheets.length > 10) throw new Error('XLSX requires between 1 and 10 worksheets.');
    let totalCells = 0;
    const seenNames = new Set();
    const sheetEntries = [];
    const relationships = [];
    const overrides = [];
    const workbookSheets = [];
    sheets.forEach((sheet, sheetIndex) => {
      let name = String(sheet?.name || `Sheet${sheetIndex + 1}`).replace(/[\\/*?:\[\]]/g, ' ').trim().slice(0, 31) || `Sheet${sheetIndex + 1}`;
      const base = name; let suffix = 2;
      while (seenNames.has(name.toLowerCase())) name = `${base.slice(0, 27)} ${suffix++}`.slice(0, 31);
      seenNames.add(name.toLowerCase());
      const headers = Array.isArray(sheet?.headers) ? sheet.headers.map(value => String(value ?? '')) : [];
      const rows = Array.isArray(sheet?.rows) ? sheet.rows : [];
      if (headers.length > 50) throw new Error(`Worksheet '${name}' exceeds 50 columns.`);
      if (rows.length > 10000) throw new Error(`Worksheet '${name}' exceeds 10,000 data rows.`);
      const columnCount = Math.max(headers.length, ...rows.map(row => Array.isArray(row) ? row.length : 0));
      if (columnCount > 50) throw new Error(`Worksheet '${name}' exceeds 50 columns.`);
      if (rows.some(row => !Array.isArray(row))) throw new Error(`Every row in worksheet '${name}' must be an array.`);
      totalCells += (headers.length ? columnCount : 0) + rows.reduce((count, row) => count + row.length, 0);
      if (totalCells > 100000) throw new Error('XLSX output is limited to 100,000 cells across all worksheets.');
      const allRows = headers.length ? [headers, ...rows] : rows;
      const rowXml = allRows.map((row, rowIndex) => {
        const cells = Array.from({ length: columnCount }, (_, columnIndex) => excelCell(row[columnIndex], `${columnName(columnIndex)}${rowIndex + 1}`, headers.length && rowIndex === 0 ? 1 : 0));
        return `<row r="${rowIndex + 1}">${cells.join('')}</row>`;
      }).join('');
      const widths = Array.from({ length: columnCount }, (_, index) => {
        let maximum = headers[index] ? headers[index].length : 10;
        for (const row of rows.slice(0, 100)) maximum = Math.max(maximum, String(row[index] ?? '').length);
        return `<col min="${index + 1}" max="${index + 1}" width="${Math.max(10, Math.min(60, maximum + 2))}" customWidth="1"/>`;
      }).join('');
      const lastCell = `${columnName(Math.max(0, columnCount - 1))}${Math.max(1, allRows.length)}`;
      const freeze = headers.length ? '<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>' : '<sheetViews><sheetView workbookViewId="0"/></sheetViews>';
      const filter = headers.length && allRows.length > 1 ? `<autoFilter ref="A1:${lastCell}"/>` : '';
      const worksheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:${lastCell}"/>${freeze}<cols>${widths}</cols><sheetData>${rowXml}</sheetData>${filter}<pageMargins left="0.35" right="0.35" top="0.5" bottom="0.5" header="0.2" footer="0.2"/></worksheet>`;
      const fileName = `sheet${sheetIndex + 1}.xml`;
      sheetEntries.push({ name: `xl/worksheets/${fileName}`, data: worksheet });
      relationships.push(`<Relationship Id="rId${sheetIndex + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/${fileName}"/>`);
      overrides.push(`<Override PartName="/xl/worksheets/${fileName}" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`);
      workbookSheets.push(`<sheet name="${xml(name)}" sheetId="${sheetIndex + 1}" r:id="rId${sheetIndex + 1}"/>`);
    });
    relationships.push(`<Relationship Id="rId${sheets.length + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>`);
    const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>${overrides.join('')}</Types>`;
    const rootRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`;
    const workbook = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>${workbookSheets.join('')}</sheets><calcPr calcId="191029"/></workbook>`;
    const workbookRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${relationships.join('')}</Relationships>`;
    const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Aptos"/></font><font><b/><color rgb="FF20344A"/><sz val="11"/><name val="Aptos"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEAF0F7"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD7DEE7"/></left><right style="thin"><color rgb="FFD7DEE7"/></right><top style="thin"><color rgb="FFD7DEE7"/></top><bottom style="thin"><color rgb="FFD7DEE7"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>`;
    return zipStored([
      { name: '[Content_Types].xml', data: contentTypes }, { name: '_rels/.rels', data: rootRels },
      { name: 'xl/workbook.xml', data: workbook }, { name: 'xl/_rels/workbook.xml.rels', data: workbookRels },
      { name: 'xl/styles.xml', data: styles }, ...sheetEntries
    ]);
  }

  const winAnsiMap = new Map([
    [0x20AC,0x80],[0x201A,0x82],[0x0192,0x83],[0x201E,0x84],[0x2026,0x85],[0x2020,0x86],[0x2021,0x87],[0x02C6,0x88],[0x2030,0x89],[0x0160,0x8A],[0x2039,0x8B],[0x0152,0x8C],[0x017D,0x8E],[0x2018,0x91],[0x2019,0x92],[0x201C,0x93],[0x201D,0x94],[0x2022,0x95],[0x2013,0x96],[0x2014,0x97],[0x02DC,0x98],[0x2122,0x99],[0x0161,0x9A],[0x203A,0x9B],[0x0153,0x9C],[0x017E,0x9E],[0x0178,0x9F]
  ]);

  function pdfBytesHex(text) {
    const output = [];
    let replacements = 0;
    for (const character of String(text)) {
      const code = character.codePointAt(0);
      let value;
      if (code >= 0x20 && code <= 0x7E || code >= 0xA0 && code <= 0xFF) value = code;
      else if (winAnsiMap.has(code)) value = winAnsiMap.get(code);
      else if (code === 9) value = 0x20;
      else { value = 0x3F; replacements++; }
      output.push(value.toString(16).padStart(2, '0'));
    }
    return { hex: output.join(''), replacements };
  }

  function pdfPlainLines(content, maximumLineLength = 92) {
    const source = String(content ?? '').replace(/\r/g, '').split('\n');
    const lines = [];
    let code = false;
    for (const raw of source) {
      let line = raw;
      let heading = false;
      if (/^\s*```/.test(line)) { code = !code; continue; }
      if (!code) {
        heading = /^\s*#{1,6}\s+/.test(line);
        line = line.replace(/^\s*#{1,6}\s*/, '').replace(/^\s*[-*+]\s+/, '• ').replace(/^\s*\d+[.)]\s+/, '• ');
        line = line.replace(/\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g, '$1 — $2').replace(/\*\*(.*?)\*\*/g, '$1').replace(/\*(.*?)\*/g, '$1').replace(/`([^`]+)`/g, '$1').replace(/<[^>]+>/g, '');
        if (/^\s*\|.*\|\s*$/.test(line)) line = line.trim().replace(/^\||\|$/g, '').split('|').map(value => value.trim()).join('  |  ');
      }
      const words = line.trim().split(/\s+/).filter(Boolean);
      if (!words.length) { lines.push({ text: '', heading: false }); continue; }
      let current = '';
      for (const word of words) {
        if (!current) current = word;
        else if ((current + ' ' + word).length <= maximumLineLength) current += ' ' + word;
        else { lines.push({ text: current, heading }); current = word; }
      }
      if (current) lines.push({ text: current, heading });
    }
    return lines.length ? lines : [{ text: '', heading: false }];
  }

  function createPdf(options = {}) {
    const title = String(options.title || 'RunMind report').slice(0, 200);
    const content = String(options.content ?? '');
    if (!content.trim()) throw new Error('PDF content cannot be empty.');
    if (content.length > 1_000_000) throw new Error('PDF content is limited to 1 MB.');
    const lines = pdfPlainLines(content);
    const pages = [];
    let pageLines = [];
    let verticalPosition = 752;
    for (const item of lines) {
      const lineHeight = !item.text ? 8 : item.heading ? 18 : 14;
      if (pageLines.length && verticalPosition - lineHeight < 54) {
        pages.push(pageLines);
        pageLines = [];
        verticalPosition = 790;
      }
      pageLines.push(item);
      verticalPosition -= lineHeight;
    }
    if (pageLines.length || !pages.length) pages.push(pageLines);
    const objects = [];
    const add = value => { objects.push(value); return objects.length; };
    const catalogId = add('');
    const pagesId = add('');
    const regularFontId = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>');
    const boldFontId = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>');
    const pageIds = [];
    let replacementCount = 0;
    pages.forEach((pageLines, pageIndex) => {
      const commands = [];
      if (pageIndex === 0) {
        const heading = pdfBytesHex(title); replacementCount += heading.replacements;
        commands.push(`BT /F2 19 Tf 1 0 0 1 48 790 Tm <${heading.hex}> Tj ET`);
        commands.push('0.75 w 48 776 m 547 776 l S');
      }
      let y = pageIndex === 0 ? 752 : 790;
      for (const item of pageLines) {
        if (!item.text) { y -= 8; continue; }
        const value = pdfBytesHex(item.text); replacementCount += value.replacements;
        const isHeading = item.heading;
        commands.push(`BT /${isHeading ? 'F2' : 'F1'} ${isHeading ? 12 : 10} Tf 1 0 0 1 48 ${y} Tm <${value.hex}> Tj ET`);
        y -= isHeading ? 18 : 14;
      }
      const pageNumber = pdfBytesHex(`Page ${pageIndex + 1} of ${pages.length}`);
      commands.push(`BT /F1 8 Tf 1 0 0 1 480 28 Tm <${pageNumber.hex}> Tj ET`);
      const stream = commands.join('\n');
      const streamLength = utf8(stream).length;
      const contentId = add(`<< /Length ${streamLength} >>\nstream\n${stream}\nendstream`);
      const pageId = add(`<< /Type /Page /Parent ${pagesId} 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 ${regularFontId} 0 R /F2 ${boldFontId} 0 R >> >> /Contents ${contentId} 0 R >>`);
      pageIds.push(pageId);
    });
    objects[catalogId - 1] = `<< /Type /Catalog /Pages ${pagesId} 0 R >>`;
    objects[pagesId - 1] = `<< /Type /Pages /Kids [${pageIds.map(id => `${id} 0 R`).join(' ')}] /Count ${pageIds.length} >>`;
    const infoId = add(`<< /Title <${pdfBytesHex(title).hex}> /Author <${pdfBytesHex(String(options.author || 'RunMind').slice(0, 120)).hex}> /Creator <${pdfBytesHex('RunMind 2.0.1').hex}> >>`);
    let document = '%PDF-1.4\n%RunMind\n';
    const offsets = [0];
    objects.forEach((object, index) => {
      offsets.push(utf8(document).length);
      document += `${index + 1} 0 obj\n${object}\nendobj\n`;
    });
    const xrefOffset = utf8(document).length;
    document += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
    for (let index = 1; index < offsets.length; index++) document += `${String(offsets[index]).padStart(10, '0')} 00000 n \n`;
    document += `trailer\n<< /Size ${objects.length + 1} /Root ${catalogId} 0 R /Info ${infoId} 0 R >>\nstartxref\n${xrefOffset}\n%%EOF`;
    return { bytes: utf8(document), pages: pages.length, replacements: replacementCount };
  }

  const gfExp = new Uint8Array(512);
  const gfLog = new Uint8Array(256);
  (() => {
    let value = 1;
    for (let index = 0; index < 255; index++) {
      gfExp[index] = value;
      gfLog[value] = index;
      value <<= 1;
      if (value & 0x100) value ^= 0x11D;
    }
    for (let index = 255; index < 512; index++) gfExp[index] = gfExp[index - 255];
  })();

  function gfMultiply(left, right) {
    return left && right ? gfExp[gfLog[left] + gfLog[right]] : 0;
  }

  function reedSolomonGenerator(degree) {
    let polynomial = [1];
    for (let index = 0; index < degree; index++) {
      const next = new Array(polynomial.length + 1).fill(0);
      for (let item = 0; item < polynomial.length; item++) {
        next[item] ^= polynomial[item];
        next[item + 1] ^= gfMultiply(polynomial[item], gfExp[index]);
      }
      polynomial = next;
    }
    return polynomial;
  }

  function reedSolomonRemainder(data, degree) {
    const generator = reedSolomonGenerator(degree);
    const result = [...data, ...new Array(degree).fill(0)];
    for (let index = 0; index < data.length; index++) {
      const factor = result[index];
      if (!factor) continue;
      for (let item = 0; item < generator.length; item++) result[index + item] ^= gfMultiply(generator[item], factor);
    }
    return result.slice(data.length);
  }

  function createQrMatrix(text) {
    const bytes = [...utf8(text)];
    if (!bytes.length) throw new Error('QR content cannot be empty.');
    const versions = [{ data: 19, ecc: 7 }, { data: 34, ecc: 10 }, { data: 55, ecc: 15 }, { data: 80, ecc: 20 }, { data: 108, ecc: 26 }];
    const version = versions.findIndex(item => 4 + 8 + bytes.length * 8 <= item.data * 8) + 1;
    if (!version) throw new Error(`QR content is ${bytes.length} UTF-8 bytes; the built-in offline generator supports up to 106 bytes (QR version 5, error correction L). Use a shorter URL or text.`);
    const { data: dataCount, ecc: eccCount } = versions[version - 1];
    const bits = [];
    const appendBits = (value, length) => { for (let bit = length - 1; bit >= 0; bit--) bits.push((value >>> bit) & 1); };
    appendBits(0b0100, 4);
    appendBits(bytes.length, 8);
    for (const byte of bytes) appendBits(byte, 8);
    const capacity = dataCount * 8;
    appendBits(0, Math.min(4, capacity - bits.length));
    while (bits.length % 8) bits.push(0);
    const data = [];
    for (let index = 0; index < bits.length; index += 8) data.push(bits.slice(index, index + 8).reduce((value, bit) => (value << 1) | bit, 0));
    for (let pad = 0; data.length < dataCount; pad++) data.push(pad % 2 === 0 ? 0xEC : 0x11);
    const codewords = [...data, ...reedSolomonRemainder(data, eccCount)];
    const size = version * 4 + 17;
    const modules = Array.from({ length: size }, () => new Array(size).fill(false));
    const reserved = Array.from({ length: size }, () => new Array(size).fill(false));
    const setFunction = (x, y, dark) => { if (x >= 0 && y >= 0 && x < size && y < size) { modules[y][x] = !!dark; reserved[y][x] = true; } };
    const finder = (left, top) => {
      for (let dy = -1; dy <= 7; dy++) for (let dx = -1; dx <= 7; dx++) {
        const inside = dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6;
        const dark = inside && (dx === 0 || dx === 6 || dy === 0 || dy === 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4));
        setFunction(left + dx, top + dy, dark);
      }
    };
    finder(0, 0); finder(size - 7, 0); finder(0, size - 7);
    for (let index = 8; index < size - 8; index++) {
      if (!reserved[6][index]) setFunction(index, 6, index % 2 === 0);
      if (!reserved[index][6]) setFunction(6, index, index % 2 === 0);
    }
    if (version >= 2) {
      const center = size - 7;
      for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) setFunction(center + dx, center + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
    }
    const formatData = (0b01 << 3) | 0;
    let remainder = formatData;
    for (let index = 0; index < 10; index++) remainder = (remainder << 1) ^ (((remainder >>> 9) & 1) * 0x537);
    const formatBits = ((formatData << 10) | remainder) ^ 0x5412;
    const bit = index => ((formatBits >>> index) & 1) !== 0;
    for (let index = 0; index <= 5; index++) setFunction(8, index, bit(index));
    setFunction(8, 7, bit(6)); setFunction(8, 8, bit(7)); setFunction(7, 8, bit(8));
    for (let index = 9; index < 15; index++) setFunction(14 - index, 8, bit(index));
    for (let index = 0; index < 8; index++) setFunction(size - 1 - index, 8, bit(index));
    for (let index = 8; index < 15; index++) setFunction(8, size - 15 + index, bit(index));
    setFunction(8, size - 8, true);

    let bitIndex = 0;
    for (let right = size - 1; right >= 1; right -= 2) {
      if (right === 6) right = 5;
      for (let vertical = 0; vertical < size; vertical++) {
        const upward = ((right + 1) & 2) === 0;
        const y = upward ? size - 1 - vertical : vertical;
        for (let offset = 0; offset < 2; offset++) {
          const x = right - offset;
          if (reserved[y][x]) continue;
          let dark = false;
          if (bitIndex < codewords.length * 8) dark = ((codewords[bitIndex >>> 3] >>> (7 - (bitIndex & 7))) & 1) !== 0;
          bitIndex++;
          if ((x + y) % 2 === 0) dark = !dark;
          modules[y][x] = dark;
        }
      }
    }
    return modules;
  }

  function createQrSvg(options = {}) {
    const text = String(options.text ?? '');
    const matrix = createQrMatrix(text);
    const size = matrix.length;
    const quiet = 4;
    const full = size + quiet * 2;
    const foreground = /^#[0-9a-f]{6}$/i.test(String(options.foreground || '')) ? options.foreground : '#111111';
    const background = /^#[0-9a-f]{6}$/i.test(String(options.background || '')) ? options.background : '#ffffff';
    const rectangles = [];
    for (let y = 0; y < size; y++) {
      for (let x = 0; x < size; x++) if (matrix[y][x]) rectangles.push(`M${x + quiet} ${y + quiet}h1v1h-1z`);
    }
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${full} ${full}" width="${full * 12}" height="${full * 12}" shape-rendering="crispEdges" role="img" aria-label="QR code"><rect width="100%" height="100%" fill="${background}"/><path d="${rectangles.join('')}" fill="${foreground}"/></svg>`;
    return { svg, version: (size - 17) / 4, modules: size, bytes: utf8(text).length, errorCorrection: 'L' };
  }

  function buildMermaid(options = {}) {
    const nodes = Array.isArray(options.nodes) ? options.nodes : [];
    const edges = Array.isArray(options.edges) ? options.edges : [];
    if (!nodes.length || nodes.length > 16) throw new Error('Provide between 1 and 16 diagram nodes.');
    if (edges.length > 40) throw new Error('Diagrams support at most 40 connections.');
    const direction = ['TD', 'TB', 'LR', 'RL', 'BT'].includes(String(options.direction || '').toUpperCase()) ? String(options.direction).toUpperCase() : 'TD';
    const lines = [`flowchart ${direction}`];
    nodes.forEach((node, index) => {
      const label = String(node ?? '').replace(/"/g, '#quot;').replace(/[\r\n]+/g, ' ').slice(0, 200);
      lines.push(`  N${index}["${label}"]`);
    });
    for (const edge of edges) {
      const from = Number(edge?.from), to = Number(edge?.to);
      if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || to < 0 || from >= nodes.length || to >= nodes.length || from === to) throw new Error('Every diagram edge must connect two different, valid zero-based node indexes.');
      lines.push(`  N${from} --> N${to}`);
    }
    return lines.join('\n') + '\n';
  }

  function buildChartSvg(options = {}) {
    const labels = Array.isArray(options.labels) ? options.labels.map(value => String(value ?? '').slice(0, 80)) : [];
    if (!labels.length || labels.length > 40) throw new Error('Provide between 1 and 40 chart labels.');
    const type = ['bar', 'line', 'pie', 'doughnut'].includes(String(options.type || 'bar').toLowerCase()) ? String(options.type || 'bar').toLowerCase() : 'bar';
    let series = Array.isArray(options.series) && options.series.length
      ? options.series.map((item, index) => ({ name: String(item?.name || `Series ${index + 1}`).slice(0, 60), values: item?.values }))
      : [{ name: String(options.series_name || 'Value').slice(0, 60), values: options.values }];
    if (series.length > 8) throw new Error('Charts support at most 8 data series.');
    if ((type === 'pie' || type === 'doughnut') && series.length !== 1) throw new Error('Pie and doughnut charts support exactly one data series.');
    for (const item of series) {
      if (!Array.isArray(item.values) || item.values.length !== labels.length) throw new Error(`Series '${item.name}' must contain exactly ${labels.length} values.`);
      item.values = item.values.map(value => Number(value));
      if (item.values.some(value => !Number.isFinite(value))) throw new Error(`Series '${item.name}' contains a non-finite value.`);
    }
    if ((type === 'pie' || type === 'doughnut') && (series[0].values.some(value => value < 0) || series[0].values.every(value => value === 0))) throw new Error('Pie and doughnut values must be non-negative and have a positive total.');
    const colors = ['#4f68c8', '#d18b38', '#359a83', '#bd647b', '#7c69b2', '#6d8eae', '#9b9b45', '#b06d45'];
    const width = 1000, height = (type === 'pie' || type === 'doughnut') ? Math.max(620, 150 + labels.length * 30) : 620, title = xml(String(options.title || 'Chart').slice(0, 160));
    const text = (x, y, value, extra = '') => `<text x="${x}" y="${y}" ${extra}>${xml(value)}</text>`;
    const legend = series.map((item, index) => `<g transform="translate(${width - 240},${70 + index * 26})"><rect width="14" height="14" rx="3" fill="${colors[index]}"/><text x="22" y="12" font-size="14" fill="#344054">${xml(item.name)}</text></g>`).join('');
    let body = '';
    if (type === 'pie' || type === 'doughnut') {
      const values = series[0].values, total = values.reduce((sum, value) => sum + value, 0);
      if (!Number.isFinite(total) || total <= 0) throw new Error('Pie and doughnut values must have a finite positive total.');
      const cx = 420, cy = 335, radius = 220;
      let angle = -Math.PI / 2;
      const slices = values.map((value, index) => {
        const next = angle + value / total * Math.PI * 2;
        const x1 = cx + Math.cos(angle) * radius, y1 = cy + Math.sin(angle) * radius;
        const x2 = cx + Math.cos(next) * radius, y2 = cy + Math.sin(next) * radius;
        const large = next - angle > Math.PI ? 1 : 0;
        const path = `M ${cx} ${cy} L ${x1.toFixed(2)} ${y1.toFixed(2)} A ${radius} ${radius} 0 ${large} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`;
        const result = `<path d="${path}" fill="${colors[index % colors.length]}" stroke="#fff" stroke-width="2"><title>${xml(labels[index])}: ${value}</title></path>`;
        angle = next;
        return result;
      }).join('');
      body = slices + (type === 'doughnut' ? `<circle cx="${cx}" cy="${cy}" r="118" fill="#fff"/><text x="${cx}" y="${cy + 8}" text-anchor="middle" font-size="24" font-weight="600" fill="#27364b">${xml(String(total))}</text>` : '');
      body += labels.map((label, index) => `<g transform="translate(730,${100 + index * 30})"><rect width="13" height="13" rx="3" fill="${colors[index % colors.length]}"/><text x="22" y="12" font-size="14" fill="#344054">${xml(label.slice(0, 28))} (${(values[index] / total * 100).toFixed(1)}%)</text></g>`).join('');
    } else {
      const plot = { left: 90, top: 95, width: 770, height: 410 };
      const allValues = series.flatMap(item => item.values);
      let min = Math.min(0, ...allValues), max = Math.max(0, ...allValues);
      if (min === max) { min -= 1; max += 1; }
      const range = max - min;
      if (!Number.isFinite(range) || range <= 0) throw new Error('Chart values exceed the supported numeric range.');
      const y = value => plot.top + (max - value) / range * plot.height;
      const x = index => plot.left + (labels.length <= 1 ? plot.width / 2 : index * plot.width / (labels.length - 1));
      const zeroY = y(0);
      const grid = Array.from({ length: 6 }, (_, index) => {
        const value = max - range * index / 5, yy = y(value);
        return `<line x1="${plot.left}" y1="${yy}" x2="${plot.left + plot.width}" y2="${yy}" stroke="#e4e8ef"/><text x="${plot.left - 14}" y="${yy + 5}" font-size="13" text-anchor="end" fill="#596579">${Number(value.toPrecision(4))}</text>`;
      }).join('');
      const labelInterval = Math.max(1, Math.ceil(labels.length / 16));
      const xLabels = labels.map((label, index) => (index % labelInterval === 0 || index === labels.length - 1) ? text(x(index), plot.top + plot.height + 30, label.slice(0, 12), 'font-size="12" text-anchor="middle" fill="#596579"') : '').join('');
      if (type === 'bar') {
        const groupWidth = Math.min(54, plot.width / labels.length * 0.72);
        const barWidth = groupWidth / series.length;
        body = series.map((item, seriesIndex) => item.values.map((value, index) => {
          const left = x(index) - groupWidth / 2 + seriesIndex * barWidth;
          const top = Math.min(y(value), zeroY), barHeight = Math.max(1, Math.abs(zeroY - y(value)));
          return `<rect x="${left.toFixed(2)}" y="${top.toFixed(2)}" width="${Math.max(2, barWidth - 2).toFixed(2)}" height="${barHeight.toFixed(2)}" rx="2" fill="${colors[seriesIndex]}"><title>${xml(item.name)} · ${xml(labels[index])}: ${value}</title></rect>`;
        }).join('')).join('');
      } else {
        body = series.map((item, seriesIndex) => {
          const points = item.values.map((value, index) => `${x(index).toFixed(2)},${y(value).toFixed(2)}`).join(' ');
          const dots = item.values.map((value, index) => `<circle cx="${x(index).toFixed(2)}" cy="${y(value).toFixed(2)}" r="5" fill="${colors[seriesIndex]}"><title>${xml(item.name)} · ${xml(labels[index])}: ${value}</title></circle>`).join('');
          return `<polyline points="${points}" fill="none" stroke="${colors[seriesIndex]}" stroke-width="3" stroke-linejoin="round" stroke-linecap="round"/>${dots}`;
        }).join('');
      }
      body = `${grid}<line x1="${plot.left}" y1="${zeroY}" x2="${plot.left + plot.width}" y2="${zeroY}" stroke="#7b8798" stroke-width="1.5"/>${body}${xLabels}`;
    }
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-label="${title}"><rect width="100%" height="100%" fill="#fff"/><text x="48" y="48" font-size="25" font-weight="700" fill="#23364d">${title}</text>${type === 'bar' || type === 'line' ? legend : ''}${body}</svg>`;
  }

  root.RunMindArtifactTools = Object.freeze({
    createDocx,
    createXlsx,
    createPdf,
    createQrSvg,
    buildMermaid,
    buildChartSvg,
    zipStored,
    crc32
  });
})(globalThis);
