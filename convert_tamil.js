const fs = require('fs');
const path = require('path');

const { unicodeToBamini } = require('./bamini_converter');

function convertTamilText(str) {
    if (!str) return str;
    
    // Preserve speaker prefix (e.g. "ANYA: ", "JONES: ", "PRIBOI: ")
    let prefix = '';
    let dialog = str;
    const colonIdx = str.indexOf(': ');
    if (colonIdx > 0 && colonIdx <= 15) {
        prefix = str.substring(0, colonIdx + 2);
        dialog = str.substring(colonIdx + 2);
    }
    
    const convertedDialog = unicodeToBamini(dialog);
    return prefix + convertedDialog;
}

// 1. Backup and convert translations/tamil.json
const tamilPath = path.join(__dirname, 'translations', 'tamil.json');
const backupPath = path.join(__dirname, 'translations', 'tamil_unicode_backup.json');

if (!fs.existsSync(backupPath)) {
    const rawData = fs.readFileSync(tamilPath, 'utf8');
    fs.writeFileSync(backupPath, rawData, 'utf8');
}

const sourceData = fs.readFileSync(backupPath, 'utf8');
const json = JSON.parse(sourceData);
const convertedJson = {};

let totalEntries = 0;
let convertedEntries = 0;

for (const [key, val] of Object.entries(json)) {
    totalEntries++;
    const convertedVal = convertTamilText(val);
    convertedJson[key] = convertedVal;
    if (convertedVal !== val) {
        convertedEntries++;
    }
}

fs.writeFileSync(tamilPath, JSON.stringify(convertedJson, null, 2), 'utf8');
console.log(`Successfully converted translations/tamil.json! Total: ${totalEntries}, Converted: ${convertedEntries}`);

// Update in bin/ if present
const binTamil = path.join(__dirname, 'bin', 'Debug', 'net10.0', 'mod', 'translations', 'tamil.json');
if (fs.existsSync(path.dirname(binTamil))) {
    fs.writeFileSync(binTamil, JSON.stringify(convertedJson, null, 2), 'utf8');
    console.log(`Updated bin/Debug/net10.0/mod/translations/tamil.json`);
}
