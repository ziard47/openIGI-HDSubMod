import json
import os
import kruti_converter

def convert_hindi_text(text: str) -> str:
    if not text:
        return text
    
    # Handle nukta decomposition for pre-composed characters
    normalized = text.replace('\u095c', '\u0921\u093c').replace('\u095d', '\u0922\u093c')
    
    prefix = ''
    dialog = normalized
    colon_idx = normalized.find(': ')
    if 0 < colon_idx <= 15:
        prefix = normalized[:colon_idx + 2]
        dialog = normalized[colon_idx + 2:]
        
    converted_dialog = kruti_converter.Unicode_to_KrutiDev(dialog)
    
    # Restore question mark to ASCII ? so it uses the font's question mark glyph instead of backslash
    converted_dialog = converted_dialog.replace('\\', '?')
    
    return prefix + converted_dialog

base_dir = os.path.dirname(os.path.abspath(__file__))
hindi_path = os.path.join(base_dir, 'translations', 'hindi.json')
backup_path = os.path.join(base_dir, 'translations', 'hindi_unicode_backup.json')

with open(hindi_path, 'r', encoding='utf-8') as f:
    raw_data = f.read()

# Only create backup if not already present or if it's Unicode
if not os.path.exists(backup_path):
    with open(backup_path, 'w', encoding='utf-8') as f:
        f.write(raw_data)

# Always load from Unicode backup if available to avoid double-conversion
with open(backup_path, 'r', encoding='utf-8') as f:
    source_data = json.load(f)

converted_data = {}
total = 0
converted_count = 0

for key, val in source_data.items():
    total += 1
    c_val = convert_hindi_text(val)
    converted_data[key] = c_val
    if c_val != val:
        converted_count += 1

with open(hindi_path, 'w', encoding='utf-8') as f:
    json.dump(converted_data, f, ensure_ascii=False, indent=2)

print(f"Successfully converted translations/hindi.json! Total: {total}, Converted: {converted_count}")

bin_hindi = os.path.join(base_dir, 'bin', 'Debug', 'net10.0', 'mod', 'translations', 'hindi.json')
if os.path.exists(os.path.dirname(bin_hindi)):
    with open(bin_hindi, 'w', encoding='utf-8') as f:
        json.dump(converted_data, f, ensure_ascii=False, indent=2)
    print("Updated bin/Debug/net10.0/mod/translations/hindi.json")
