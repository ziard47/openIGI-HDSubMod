const fs = require('fs');
const path = require('path');

// 1. Read base converter logic
let code = fs.readFileSync('scratch_converter.js', 'utf8');
let body = code.substring(code.indexOf('function startText()'), code.indexOf('// --- END replacement mapping ---'));
body = body.substring(body.indexOf('// --- BEGIN replacement mapping (preserved) ---'));

// Add Sanyaka Dda & missing glyph mappings
const extra = `
text = text.replace(/ඬ්/g, "å");
text = text.replace(/ඬි/g, "ç");
text = text.replace(/ඬී/g, "é");
text = text.replace(/ඬ/g, "~");
`;

const convertFn = new Function('text', extra + '\n' + body + '\nreturn text;');

function convertSinhalaText(str) {
    if (!str) return str;
    
    // Check for speaker prefix e.g. "ANYA: ", "JONES: ", "PRIBOI: ", "EKK: ", "HARRISON: "
    let prefix = '';
    let dialog = str;
    const colonIdx = str.indexOf(': ');
    if (colonIdx > 0 && colonIdx <= 15) {
        prefix = str.substring(0, colonIdx + 2);
        dialog = str.substring(colonIdx + 2);
    }
    
    const convertedDialog = convertFn(dialog);
    return prefix + convertedDialog;
}

// 2. Backup and convert translations/sinhala.json
const sinhalaPath = path.join(__dirname, 'translations', 'sinhala.json');
const backupPath = path.join(__dirname, 'translations', 'sinhala_unicode_backup.json');

const rawData = fs.readFileSync(sinhalaPath, 'utf8');
fs.writeFileSync(backupPath, rawData, 'utf8');

const json = JSON.parse(rawData);
const convertedJson = {};

let totalEntries = 0;
let convertedEntries = 0;

for (const [key, val] of Object.entries(json)) {
    totalEntries++;
    const convertedVal = convertSinhalaText(val);
    convertedJson[key] = convertedVal;
    if (convertedVal !== val) {
        convertedEntries++;
    }
}

fs.writeFileSync(sinhalaPath, JSON.stringify(convertedJson, null, 2), 'utf8');
console.log(`Successfully converted translations/sinhala.json! Total: ${totalEntries}, Converted: ${convertedEntries}`);

// Also copy to output directory if it exists
const binSinhala = path.join(__dirname, 'bin', 'Debug', 'net10.0', 'mod', 'translations', 'sinhala.json');
if (fs.existsSync(path.dirname(binSinhala))) {
    fs.writeFileSync(binSinhala, JSON.stringify(convertedJson, null, 2), 'utf8');
    console.log(`Updated bin/Debug/net10.0/mod/translations/sinhala.json`);
}

