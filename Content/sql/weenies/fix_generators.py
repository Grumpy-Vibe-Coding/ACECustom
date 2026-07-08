import os

file_path = r'C:\ACE\ACECustom\Content\sql\weenies\730001003 T11 Master Generator Placements.sql'
temp_path = file_path + '.tmp'

# Define the 4x4 block of crater hexes
crater_hexes = set()
for x in ['F5', 'F6', 'F7', 'F8']:
    for y in ['5A', '5B', '5C', '5D']:
        crater_hexes.add(f"0X{x}{y}")

fixes_made = {'crater_to_obsidian': 0, 'outside_to_land': 0}

with open(file_path, 'r') as f_in, open(temp_path, 'w') as f_out:
    for line in f_in:
        stripped = line.strip()
        if stripped.startswith('(') and stripped.endswith('),') or stripped.endswith(')'):
            parts = line.split(',')
            if len(parts) >= 12:
                guid = parts[0].strip().replace('(', '')
                weenie = parts[1].strip()
                cell = parts[2].strip()
                
                if guid.isdigit() and weenie.isdigit() and cell.isdigit():
                    hex_str = hex(int(cell) >> 16).upper()
                    
                    new_weenie = weenie
                    if hex_str in crater_hexes:
                        if weenie != '730001002':
                            new_weenie = '730001002'
                            fixes_made['crater_to_obsidian'] += 1
                    else:
                        if weenie == '730001002':
                            new_weenie = '730001001'
                            fixes_made['outside_to_land'] += 1
                    
                    if new_weenie != weenie:
                        parts[1] = new_weenie
                        line = ','.join(parts)
        
        f_out.write(line)

os.replace(temp_path, file_path)

print(f"Fixes applied!")
print(f"Generators IN crater changed to Obsidian (730001002): {fixes_made['crater_to_obsidian']}")
print(f"Obsidian Generators OUTSIDE crater changed to Land (730001001): {fixes_made['outside_to_land']}")
