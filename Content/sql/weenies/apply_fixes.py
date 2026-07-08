import os
import re

fix_path = r'C:\ACE\ACECustom\Content\sql\weenies\730001002 T11 Spawner Terrain Fixes.sql'
master_path = r'C:\Users\Wesle\Desktop\ACE_Backups_2026-07-04_2139\sql_files\730001003 T11 Master Generator Placements.sql'
dest_path = r'C:\ACE\ACECustom\Content\sql\weenies\730001003 T11 Master Generator Placements.sql'

# 1. Parse all the DAT fixes from 730001002
# The GUIDs in 730001002 start at 2142750000, so we add 1000 to map them to 730001003
fixes = {}
with open(fix_path, 'r') as f:
    for line in f:
        match = re.search(r'UPDATE `landblock_instance` SET `weenie_Class_Id` = (\d+) WHERE `guid` = (\d+);', line)
        if match:
            weenie, guid = match.groups()
            fixes[str(int(guid) + 1000)] = weenie

# 2. Define the visual crater hexes
crater_hexes = set()
for x in ['F5', 'F6', 'F7', 'F8']:
    for y in ['5A', '5B', '5C', '5D']:
        crater_hexes.add(f"0X{x}{y}")

# 3. Apply the fixes to the raw random backup
with open(master_path, 'r') as f_in, open(dest_path, 'w') as f_out:
    for line in f_in:
        stripped = line.strip()
        if stripped.startswith('(') and (stripped.endswith('),') or stripped.endswith(')')):
            parts = line.split(',')
            if len(parts) >= 12:
                guid = parts[0].strip().replace('(', '')
                weenie = parts[1].strip()
                cell = parts[2].strip()
                
                if guid.isdigit() and weenie.isdigit() and cell.isdigit():
                    # Apply DAT fix if one exists for this GUID
                    if guid in fixes:
                        weenie = fixes[guid]
                    
                    # Apply visual crater fixes
                    hex_str = hex(int(cell) >> 16).upper()
                    
                    if hex_str in crater_hexes:
                        if weenie == '730001001': # Land -> Obsidian
                            weenie = '730001002'
                    else:
                        if weenie == '730001002': # Obsidian -> Land
                            weenie = '730001001'
                            
                    # Removed manual override for 2142751218 per user request
                    
                    parts[1] = weenie
                    line = ','.join(parts)
        
        f_out.write(line)

print(f"Loaded {len(fixes)} DAT fixes.")
print("Rebuilt 730001003 with fully correct DAT mappings AND visual crater fixes!")
