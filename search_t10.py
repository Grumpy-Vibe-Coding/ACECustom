import json

path = r'C:\Users\Wesle\.gemini\antigravity-ide\brain\0550bd8a-c4b5-403d-ad66-18d470631bb1\.system_generated\logs\transcript.jsonl'

with open(path, encoding='utf-8', errors='ignore') as f:
    lines = f.readlines()

for i, line in enumerate(lines):
    try:
        step = json.loads(line)
        content = str(step.get('content','')) + str(step.get('tool_calls',''))
        if 'Endurance' in content and 'T10' in content and 'Coordination' in content:
            print("=== Step", step.get('step_index'), "type=", step.get('type'), "===")
            idx = content.find('Endurance')
            print(content[max(0,idx-500):idx+2000])
            print()
    except:
        pass
