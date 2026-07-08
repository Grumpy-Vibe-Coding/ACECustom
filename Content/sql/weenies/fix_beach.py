import os

file_path = r'C:\ACE\ACECustom\Content\sql\weenies\730001003 T11 Master Generator Placements.sql'
temp_path = file_path + '.tmp'

with open(file_path, 'r') as f_in, open(temp_path, 'w') as f_out:
    for line in f_in:
        stripped = line.strip()
        if stripped.startswith('(2142751218,'):
            # This is 0xF35A0017
            parts = line.split(',')
            parts[1] = '730001003' # Set to Beach
            line = ','.join(parts)
        f_out.write(line)

os.replace(temp_path, file_path)
print("Fixed 0xF35A0017 to Beach")
